using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Saving the careers apply form to finish later (apply-flow.md §7): a draft under the candidate's email with a signed
/// link emailed through Mailpit, saved again and read by that link, deleted when the application is sent, and never an
/// application: a knockout answer in a draft ends nothing. Each test makes its own organization with a job copied from
/// "Software engineer" version 4, whose right-to-work automation queues a rejection review when someone sends "no".
/// </summary>
public sealed partial class ApplicationDraftTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_saved_draft_emails_its_link_and_the_link_reads_and_saves_it_again()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        var email = ApiSession.NewEmail("draft");

        var form = await client.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{organization.Slug}/jobs/{job.Slug}/application-form", Ct);
        form.GetProperty("saveDraftEnabled").GetBoolean().Should().BeTrue("the stack sends email through Mailpit");
        form.GetProperty("questionsPerPage").GetInt32().Should().Be(Organization.DefaultQuestionsPerPage);

        var created = await client.PostAsJsonAsync(DraftsPath(organization, job), Draft(email, ("work_authorization", "yes")), Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var receipt = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var token = receipt.GetProperty("token").GetString()!;
        receipt.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));

        var message = (await fixture.Mailpit.WaitForMessagesAsync(email, 1, Ct)).Single();
        message.Subject.Should().Be($"Finish your application for {job.Title} at {organization.Name}");
        TokenIn(message.Text, organization.Slug, job.Slug).Should().Be(token);

        var read = await client.GetFromJsonAsync<JsonElement>(DraftPath(organization, token), Ct);
        read.GetProperty("jobSlug").GetString().Should().Be(job.Slug);
        read.GetProperty("email").GetString().Should().Be(email.ToLowerInvariant());
        read.GetProperty("answers").EnumerateArray().Select(a => a.GetProperty("questionId").GetString()).Should().Equal("work_authorization");

        var saved = await client.PutAsJsonAsync(DraftPath(organization, token), Draft(email, ("work_authorization", "yes"), ("experience", "three_to_five")), Ct);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
        var again = await client.GetFromJsonAsync<JsonElement>(DraftPath(organization, token), Ct);
        again.GetProperty("answers").GetArrayLength().Should().Be(2);
        (await fixture.Mailpit.MessagesAsync(email, Ct)).Should().ContainSingle("saving again by the link sends no second email");
    }

    [Fact]
    public async Task A_draft_holds_answers_only_and_refuses_files_and_answers_of_the_wrong_shape()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");

        var withFile = await client.PostAsJsonAsync(DraftsPath(organization, job), Draft(ApiSession.NewEmail("draft"), ("resume", Guid.NewGuid().ToString())), Ct);
        var wrongShape = await client.PostAsJsonAsync(DraftsPath(organization, job), Draft(ApiSession.NewEmail("draft"), ("experience", "forever")), Ct);
        var noEmail = await client.PostAsJsonAsync(DraftsPath(organization, job), Draft("", ("work_authorization", "yes")), Ct);

        withFile.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a draft keeps no files");
        wrongShape.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noEmail.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_forged_link_or_another_organizations_link_finds_nothing_and_an_expired_draft_is_gone()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        var (other, otherJob) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        var token = await CreateAsync(client, organization, job, ApiSession.NewEmail("draft"));
        var otherToken = await CreateAsync(client, other, otherJob, ApiSession.NewEmail("draft"));

        (await client.GetAsync(DraftPath(organization, "CfDJ8forged-token-that-reads-as-nothing"), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(DraftPath(organization, otherToken), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync(DraftPath(organization, otherToken), Draft("x@example.com"), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using (var owner = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct)))
        {
            await owner.OpenAsync(Ct);
            await using var expire = new NpgsqlCommand(
                "UPDATE application_drafts SET expires_at = now() - interval '1 minute' WHERE organization_id = @organization", owner);
            expire.Parameters.AddWithValue("organization", organization.Id);
            await expire.ExecuteNonQueryAsync(Ct);
        }

        (await client.GetAsync(DraftPath(organization, token), Ct)).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await client.PutAsJsonAsync(DraftPath(organization, token), Draft("x@example.com"), Ct)).StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task An_address_gets_at_most_five_links_a_day()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        var email = ApiSession.NewEmail("draft");

        for (var i = 0; i < 5; i++)
        {
            await CreateAsync(client, organization, job, email);
        }

        var sixth = await client.PostAsJsonAsync(DraftsPath(organization, job), Draft(email), Ct);

        sixth.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await fixture.Mailpit.WaitForMessagesAsync(email, 5, Ct)).Should().HaveCount(5);
    }

    [Fact]
    public async Task Sending_the_application_deletes_its_drafts_and_records_that_it_was_resumed()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        var email = ApiSession.NewEmail("draft");
        var token = await CreateAsync(client, organization, job, email, ("work_authorization", "yes"), ("experience", "three_to_five"));
        var uploadId = await CareersUploads.UploadAsync(client, organization.Slug, job.Id, "resume", "CV.pdf", CareersUploads.Pdf);

        var applicationId = await SubmitAsync(client, organization, job, email, uploadId, workAuthorization: "yes", experience: "six_to_nine");

        (await client.GetAsync(DraftPath(organization, token), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var db = await TenantDbAsync(organization.Id);
        (await db.ApplicationDrafts.CountAsync(Ct)).Should().Be(0);
        var observation = await db.SubmissionObservations.SingleAsync(o => o.ApplicationId == applicationId, Ct);
        observation.ResumedFromDraft.Should().BeTrue();
        observation.CarriedOverQuestionIds.Should().Equal("work_authorization");
        observation.PageTimes.Select(p => p.Page).Should().Equal("details", "page-1", "review");
    }

    [Fact]
    public async Task A_knockout_answer_in_a_draft_ends_nothing_until_the_application_is_sent()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        var email = ApiSession.NewEmail("draft");

        await CreateAsync(client, organization, job, email, ("work_authorization", "no"));
        await fixture.Mailpit.WaitForMessagesAsync(email, 1, Ct);

        await using (var db = await TenantDbAsync(organization.Id))
        {
            (await db.Applications.CountAsync(Ct)).Should().Be(0, "a draft is never an application");
            (await db.AutomationRuns.CountAsync(Ct)).Should().Be(0, "no automation reads a draft");
            (await db.ApplicationTasks.CountAsync(Ct)).Should().Be(0);
        }

        var uploadId = await CareersUploads.UploadAsync(client, organization.Slug, job.Id, "resume", "CV.pdf", CareersUploads.Pdf);
        var applicationId = await SubmitAsync(client, organization, job, email, uploadId, workAuthorization: "no", experience: "three_to_five");

        // Sent, the same answer is the knockout's to judge, as on any application: the right-to-work automation runs.
        await WolverineStore.WaitUntilAsync(async () =>
        {
            await using var db = await TenantDbAsync(organization.Id);
            return await db.AutomationRuns.AnyAsync(r => r.ApplicationId == applicationId && r.AutomationId == "right_to_work_review", Ct);
        }, "the right-to-work automation on the sent application", Ct);
    }

    [Fact]
    public async Task The_daily_sweep_deletes_expired_drafts_and_keeps_the_rest()
    {
        var (organization, job) = await OrganizationWithEngineeringJobAsync();
        var (other, otherJob) = await OrganizationWithEngineeringJobAsync();
        using var client = fixture.CreateHttpClient("api");
        await CreateAsync(client, organization, job, ApiSession.NewEmail("draft"));
        var kept = await CreateAsync(client, organization, job, ApiSession.NewEmail("draft"));
        // Another organization's draft, expired too: a purge for this organization never reaches it.
        await CreateAsync(client, other, otherJob, ApiSession.NewEmail("draft"));

        await using (var owner = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct)))
        {
            await owner.OpenAsync(Ct);
            await using var expire = new NpgsqlCommand(
                """
                UPDATE application_drafts SET expires_at = now() - interval '1 day'
                WHERE id = (SELECT id FROM application_drafts WHERE organization_id = @organization ORDER BY created_at LIMIT 1)
                   OR organization_id = @other
                """, owner);
            expire.Parameters.AddWithValue("organization", organization.Id);
            expire.Parameters.AddWithValue("other", other.Id);
            await expire.ExecuteNonQueryAsync(Ct);
        }

        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        // What the daily sweep sends for this organization.
        await sender.Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new PurgeOrganizationApplicationDrafts(organization.Id, DateTimeOffset.UtcNow));

        await WolverineStore.WaitUntilAsync(async () =>
        {
            await using var db = await TenantDbAsync(organization.Id);
            return await db.ApplicationDrafts.CountAsync(Ct) == 1;
        }, "the sweep", Ct);
        (await client.GetAsync(DraftPath(organization, kept), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var otherDb = await TenantDbAsync(other.Id);
        (await otherDb.ApplicationDrafts.CountAsync(Ct)).Should().Be(1, "the purge runs under its own organization only");
    }

    [Fact]
    public async Task Questions_per_page_is_an_organization_setting_with_its_history()
    {
        var recruiter = await fixture.SignedInWithOrganizationAsync();
        using var _ = recruiter;

        (await recruiter.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Body.GetProperty("questionsPerPage").GetInt32().Should().Be(8);
        var name = (await recruiter.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("name");

        var saved = (await recruiter.PutAsync("/v1/organization", new JsonObject { ["name"] = name, ["questionsPerPage"] = 12 })).Expect(HttpStatusCode.OK);
        (await recruiter.PutAsync("/v1/organization", new JsonObject { ["name"] = name, ["questionsPerPage"] = 51 })).Expect(HttpStatusCode.BadRequest);
        var renamed = (await recruiter.PutAsync("/v1/organization", new JsonObject { ["name"] = $"{name} Labs" })).Expect(HttpStatusCode.OK);

        saved.Body.GetProperty("questionsPerPage").GetInt32().Should().Be(12);
        renamed.Body.GetProperty("questionsPerPage").GetInt32().Should().Be(12, "a save without it keeps it");
        var history = (await recruiter.GetAsync("/v1/organization/history")).Expect(HttpStatusCode.OK).Body.GetRawText();
        history.Should().Contain("changed questions per page from 8 to ");
    }

    // A draft as the careers server saves it: the candidate and some of the engineering template's answers.
    private static JsonObject Draft(string email, params (string QuestionId, string Value)[] answers) => new()
    {
        ["candidate"] = new JsonObject { ["firstName"] = "Casey", ["lastName"] = "Morgan", ["email"] = email },
        ["answers"] = new JsonArray([.. answers.Select(a => (JsonNode)new JsonObject { ["questionId"] = a.QuestionId, ["value"] = a.Value })]),
        ["aiUseDeclaration"] = "DidNotUse",
    };

    private static async Task<string> CreateAsync(HttpClient client, Organization organization, Job job, string email, params (string, string)[] answers)
    {
        var response = await client.PostAsJsonAsync(DraftsPath(organization, job), Draft(email, answers), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("token").GetString()!;
    }

    private static async Task<Guid> SubmitAsync(
        HttpClient client, Organization organization, Job job, string email, Guid uploadId, string workAuthorization, string experience)
    {
        var response = await client.PostAsJsonAsync($"/v1/public/organizations/{organization.Slug}/jobs/{job.Id}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["firstName"] = "Casey", ["lastName"] = "Morgan", ["email"] = email },
            ["privacyNoticeVersion"] = "2026-10-31.1",
            ["answers"] = new JsonArray(
                new JsonObject { ["questionId"] = "resume", ["value"] = uploadId.ToString() },
                new JsonObject { ["questionId"] = "work_authorization", ["value"] = workAuthorization },
                new JsonObject { ["questionId"] = "experience", ["value"] = experience }),
            ["aiUseDeclaration"] = "DidNotUse",
            ["pageTimes"] = new JsonArray(
                new JsonObject { ["page"] = "details", ["seconds"] = 40 },
                new JsonObject { ["page"] = "page-1", ["seconds"] = 90 },
                new JsonObject { ["page"] = "review", ["seconds"] = 20 }),
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("applicationId").GetGuid();
    }

    private static string DraftsPath(Organization organization, Job job) => $"/v1/public/organizations/{organization.Slug}/jobs/{job.Id}/application-drafts";

    private static string DraftPath(Organization organization, string token) => $"/v1/public/organizations/{organization.Slug}/application-drafts/{token}";

    // The link in the email is the job's careers page followed by /apply?draft={token}.
    private static string TokenIn(string text, string slug, string jobSlug)
    {
        var match = DraftLink().Match(text);
        match.Success.Should().BeTrue($"the email carries the draft link: {text}");
        match.Groups["page"].Value.Should().Contain(slug).And.EndWith($"/jobs/{jobSlug}");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex(@"(?<page>https?://\S+)/apply\?draft=(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex DraftLink();

    private async Task<(Organization Organization, Job Job)> OrganizationWithEngineeringJobAsync()
    {
        HiringTemplateVersion engineering;
        await using (var db = await TenantDbAsync(Guid.NewGuid()))
        {
            engineering = await db.HiringTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == SystemTemplates.SoftwareEngineerVersion4, Ct);
        }

        Job job = null!;
        var organization = await fixture.CreateOrganizationAsync(org =>
        {
            job = TestData.NewJob(org, "engineer");
            job.UseTemplate(engineering);
            job.Publish(DateTimeOffset.UtcNow);
            return [job];
        }, Ct);
        return (organization, job);
    }

    private async Task<Infrastructure.Persistence.ScreenHireDbContext> TenantDbAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return await fixture.CreateDbContextAsync(tenant, Ct);
    }
}
