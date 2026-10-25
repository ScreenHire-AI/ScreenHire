using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Applications;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Automations;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Job access through each job's hiring team (ADR-0015): a member on Only jobs they're on sees a job, its applications,
/// candidates, tasks and history only while they are on its team, in the dashboard API and in the database alike.
/// Everyone else, API keys and Owners and Admins included, sees every job.
/// </summary>
public sealed class JobAccessTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_recruiter_on_only_their_jobs_creates_a_job_and_is_on_its_team()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await owner.CreateJobAsync(Samples.RemoteJob("Data Engineer"));
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");

        var created = await recruiter.CreateJobAsync(Samples.RemoteJob("Data Engineer"));

        var jobs = (await recruiter.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK);
        jobs.Body.EnumerateArray().Select(j => j.GetProperty("id").GetGuid()).Should().Equal(created.Id());
        created.Text("slug").Should().StartWith("data-engineer-", "a free-looking slug may belong to a job they cannot see");
        var team = (await recruiter.GetAsync($"/v1/jobs/{created.Id()}/hiring-team")).Expect(HttpStatusCode.OK);
        team.Body.EnumerateArray().Select(m => m.GetProperty("userId").GetGuid()).Should().Equal(recruiter.UserId);
    }

    [Fact]
    public async Task A_member_on_only_their_jobs_gets_404_for_every_other_job_and_sees_none_of_it_in_lists()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var theirs = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        var hidden = await owner.CreateJobAsync(Samples.RemoteJob("Payroll Specialist"));
        var both = ApiSession.NewEmail("both");
        var visibleApplication = (await owner.CreateApplicationAsync(theirs.Id(), both)).Id();
        var hiddenApplication = (await owner.CreateApplicationAsync(hidden.Id(), both)).Id();
        var hiddenOnly = await owner.CreateApplicationAsync(hidden.Id(), ApiSession.NewEmail("hidden"));
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        var viewerMember = await owner.MemberIdAsync(viewer.UserId);

        (await viewer.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK).Count().Should().Be(0, "nobody has put them on a team yet");

        await owner.SetHiringTeamAsync(theirs.Id(), viewerMember);

        (await viewer.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK)
            .Body.EnumerateArray().Select(j => j.GetProperty("id").GetGuid()).Should().Equal(theirs.Id());
        (await viewer.GetAsync($"/v1/jobs/{theirs.Id()}")).Expect(HttpStatusCode.OK);
        foreach (var path in new[]
                 {
                     $"/v1/jobs/{hidden.Id()}", $"/v1/jobs/{hidden.Id()}/applications", $"/v1/jobs/{hidden.Id()}/history",
                     $"/v1/jobs/{hidden.Id()}/versions", $"/v1/jobs/{hidden.Id()}/hiring-team", $"/v1/applications/{hiddenApplication}",
                     $"/v1/applications/{hiddenApplication}/timeline", $"/v1/candidates/{hiddenOnly.At("candidate").GetProperty("id").GetGuid()}",
                 })
        {
            (await viewer.GetAsync(path)).Status.Should().Be(HttpStatusCode.NotFound, path);
        }

        (await viewer.GetAsync("/v1/applications")).Expect(HttpStatusCode.OK)
            .Body.EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).Should().Equal(visibleApplication);
        var candidates = (await viewer.GetAsync("/v1/candidates")).Expect(HttpStatusCode.OK);
        candidates.Body.EnumerateArray().Select(c => c.GetProperty("email").GetString()).Should().Equal(both);
        var candidate = (await viewer.GetAsync($"/v1/candidates/{candidates.Id("0.id")}")).Expect(HttpStatusCode.OK);
        candidate.At("applications").EnumerateArray().Select(a => a.GetProperty("id").GetGuid())
            .Should().Equal([visibleApplication], "the candidate's application to the other job stays hidden");

        // The owner still sees everything.
        (await owner.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK).Count().Should().Be(2);

        await owner.SetHiringTeamAsync(theirs.Id());

        (await viewer.GetAsync($"/v1/jobs/{theirs.Id()}")).Status.Should().Be(HttpStatusCode.NotFound, "off the team, the job is gone");
        (await viewer.GetAsync("/v1/applications")).Expect(HttpStatusCode.OK).Count().Should().Be(0);
    }

    [Fact]
    public async Task Every_change_to_access_and_teams_lands_in_the_membership_log_and_a_reader_on_their_jobs_sees_only_theirs()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Site Reliability Engineer"));
        var other = await owner.CreateJobAsync(Samples.RemoteJob("Office Manager"));
        using var member = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        var memberId = await owner.MemberIdAsync(member.UserId);

        (await owner.PutAsync($"/v1/organization/members/{memberId}/job-access", new JsonObject { ["jobAccess"] = "HiringTeamOnly" }))
            .Expect(HttpStatusCode.OK).Body.GetProperty("seesAllJobs").GetBoolean().Should().BeFalse();
        await owner.SetHiringTeamAsync(job.Id(), memberId);
        await owner.SetHiringTeamAsync(other.Id(), memberId);
        await owner.SetHiringTeamAsync(other.Id());

        var log = (await owner.GetAsync("/v1/organization/membership-log")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        log.Should().Contain(e => e.GetProperty("kind").GetString() == "JobAccessChanged"
            && e.GetProperty("previousJobAccess").GetString() == "All" && e.GetProperty("jobAccess").GetString() == "HiringTeamOnly");
        log.Should().Contain(e => e.GetProperty("kind").GetString() == "AddedToHiringTeam"
            && e.GetProperty("jobId").GetGuid() == job.Id() && e.GetProperty("jobTitle").GetString() == "Site Reliability Engineer"
            && e.GetProperty("memberUserId").GetGuid() == member.UserId);
        log.Should().Contain(e => e.GetProperty("kind").GetString() == "RemovedFromHiringTeam" && e.GetProperty("jobId").GetGuid() == other.Id());
        log.Should().Contain(e => e.GetProperty("kind").GetString() == "Invited" && e.GetProperty("jobAccess").GetString() == "All");

        var members = (await owner.GetAsync("/v1/organization/members")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        var listed = members.Single(m => m.GetProperty("id").GetGuid() == memberId);
        listed.GetProperty("jobAccess").GetString().Should().Be("HiringTeamOnly");
        listed.GetProperty("jobCount").GetInt32().Should().Be(1);
        (await owner.GetAsync($"/v1/organization/members/{memberId}/jobs")).Expect(HttpStatusCode.OK)
            .Body.EnumerateArray().Select(j => j.GetProperty("title").GetString()).Should().Equal("Site Reliability Engineer");
    }

    [Fact]
    public async Task Copy_from_another_job_and_a_templates_default_team_both_give_access()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        var recruiterMember = await owner.MemberIdAsync(recruiter.UserId);
        var viewerMember = await owner.MemberIdAsync(viewer.UserId);
        var ownerMember = await owner.MemberIdAsync(owner.UserId);

        var (templateId, versionId) = await owner.CreatePublishedTemplateAsync(".NET roles");
        (await owner.PutAsync($"/v1/templates/{templateId}/hiring-team", new JsonObject
        {
            ["memberIds"] = new JsonArray(JsonValue.Create(viewerMember)),
        })).Expect(HttpStatusCode.OK);
        var fromTemplate = await owner.CreateJobAsync(new JsonObject { ["title"] = "Senior .NET Engineer", ["templateVersionId"] = versionId });

        var team = (await owner.GetAsync($"/v1/jobs/{fromTemplate.Id()}/hiring-team")).Expect(HttpStatusCode.OK);
        team.Body.EnumerateArray().Select(m => m.GetProperty("memberId").GetGuid())
            .Should().BeEquivalentTo([viewerMember, ownerMember], "the template's people and the creator");
        (await viewer.GetAsync($"/v1/jobs/{fromTemplate.Id()}")).Expect(HttpStatusCode.OK);

        var target = await owner.CreateJobAsync(Samples.RemoteJob("Junior .NET Engineer"));
        (await recruiter.GetAsync($"/v1/jobs/{target.Id()}")).Status.Should().Be(HttpStatusCode.NotFound);
        (await owner.PutAsync($"/v1/jobs/{target.Id()}/hiring-team", new JsonObject
        {
            ["memberIds"] = new JsonArray(JsonValue.Create(recruiterMember)),
            ["copyFromJobId"] = fromTemplate.Id(),
        })).Expect(HttpStatusCode.OK).Body.EnumerateArray().Select(m => m.GetProperty("memberId").GetGuid())
            .Should().BeEquivalentTo([recruiterMember, viewerMember, ownerMember]);
        (await viewer.GetAsync($"/v1/jobs/{target.Id()}")).Expect(HttpStatusCode.OK);
        (await recruiter.GetAsync($"/v1/jobs/{target.Id()}")).Expect(HttpStatusCode.OK);

        // The recruiter cannot copy a team from a job they do not see, and nobody adds someone from outside.
        var unseen = await owner.CreateJobAsync(Samples.RemoteJob("Accountant"));
        (await recruiter.PutAsync($"/v1/jobs/{target.Id()}/hiring-team", new JsonObject
        {
            ["memberIds"] = new JsonArray(JsonValue.Create(recruiterMember)),
            ["copyFromJobId"] = unseen.Id(),
        })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("copyFromJobId");
        (await owner.PutAsync($"/v1/jobs/{target.Id()}/hiring-team", new JsonObject
        {
            ["memberIds"] = new JsonArray(JsonValue.Create(Guid.NewGuid())),
        })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("memberIds");
        (await owner.PutAsync($"/v1/templates/{SystemTemplates.SoftwareEngineer}/hiring-team", new JsonObject
        {
            ["memberIds"] = new JsonArray(JsonValue.Create(viewerMember)),
        })).Expect(HttpStatusCode.Conflict);

        // A member who sees only their jobs cannot take themselves off one.
        (await recruiter.PutAsync($"/v1/jobs/{target.Id()}/hiring-team", new JsonObject
        {
            ["memberIds"] = new JsonArray(JsonValue.Create(viewerMember)),
        })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("memberIds");
    }

    [Fact]
    public async Task Owners_admins_and_api_keys_see_every_job_whatever_a_member_was_set_to()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await owner.CreateJobAsync(Samples.RemoteJob("Designer"));
        await owner.CreateJobAsync(Samples.RemoteJob("Writer"));
        using var member = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        var memberId = await owner.MemberIdAsync(member.UserId);
        (await member.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK).Count().Should().Be(0);

        (await owner.PutAsync($"/v1/organization/members/{memberId}/role", new JsonObject { ["role"] = "Admin" })).Expect(HttpStatusCode.OK);
        (await member.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK).Count().Should().Be(2, "an Admin sees every job");
        (await member.GetAsync("/v1/auth/me")).Expect(HttpStatusCode.OK).At("organization.seesAllJobs").GetBoolean().Should().BeTrue();
        (await owner.PutAsync($"/v1/organization/members/{memberId}/job-access", new JsonObject { ["jobAccess"] = "All" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("jobAccess");

        (await owner.PutAsync($"/v1/organization/members/{memberId}/role", new JsonObject { ["role"] = "Recruiter" })).Expect(HttpStatusCode.OK);
        (await member.GetAsync("/v1/jobs")).Expect(HttpStatusCode.OK).Count().Should().Be(0, "back to the jobs they're on, which is none");

        var token = (await owner.PostAsync("/v1/organization/api-keys", new JsonObject
        {
            ["name"] = "Sync", ["scopes"] = new JsonArray("Read"),
        })).Expect(HttpStatusCode.Created).Text("token");
        using var client = new HttpClient { BaseAddress = fixture.GetEndpoint("api") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var keyJobs = await client.GetFromJsonAsync<JsonElement>("/v1/jobs", Ct);
        keyJobs.GetArrayLength().Should().Be(2, "an API key has no member, so it reads the whole organization");
    }

    [Fact]
    public async Task A_member_on_only_their_jobs_cannot_give_anyone_more_than_that()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Team lead's job"));
        var leadRole = (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Team lead", ["copyFrom"] = "Admin" }))
            .Expect(HttpStatusCode.Created).Id();
        using var lead = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        await owner.GiveRoleAsync(lead, leadRole);
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        var viewerMember = await owner.MemberIdAsync(viewer.UserId);
        await owner.SetHiringTeamAsync(job.Id(), await owner.MemberIdAsync(lead.UserId));

        (await lead.PutAsync($"/v1/organization/members/{viewerMember}/job-access", new JsonObject { ["jobAccess"] = "All" }))
            .Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Needs all jobs");
        (await lead.PutAsync($"/v1/organization/members/{viewerMember}/role", new JsonObject { ["role"] = "Admin" }))
            .Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Needs all jobs");
        (await lead.PostAsync("/v1/organization/invitations", new JsonObject
        {
            ["email"] = ApiSession.NewEmail("widened"), ["role"] = "Viewer",
        })).Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Needs all jobs", "an invitation starts at All jobs unless it says otherwise");
        (await lead.PostAsync("/v1/organization/invitations", new JsonObject
        {
            ["email"] = ApiSession.NewEmail("narrow"), ["role"] = "Viewer", ["jobAccess"] = "HiringTeamOnly",
        })).Expect(HttpStatusCode.Created).Text("jobAccess").Should().Be("HiringTeamOnly");
        (await lead.PostAsync("/v1/organization/api-keys", new JsonObject { ["name"] = "Mine", ["scopes"] = new JsonArray("Read") }))
            .Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Needs all jobs");
        (await lead.PostAsync("/v1/webhooks", new JsonObject { ["url"] = "https://hooks.example.com/in", ["events"] = new JsonArray("job.closed") }))
            .Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Needs all jobs");

        // Adding someone to a job they see themselves is fine: it gives nothing they do not have.
        await lead.SetHiringTeamAsync(job.Id(), await owner.MemberIdAsync(lead.UserId), viewerMember);
        (await viewer.GetAsync($"/v1/jobs/{job.Id()}")).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Adding_a_candidate_who_is_only_on_a_hidden_job_is_refused_without_naming_them()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var hidden = await owner.CreateJobAsync(Samples.RemoteJob("Finance Lead"));
        var email = ApiSession.NewEmail("hidden");
        await owner.CreateApplicationAsync(hidden.Id(), email, "Morgan Quill");
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        var theirs = await recruiter.CreateJobAsync(Samples.RemoteJob("Data Engineer"));

        var refused = (await recruiter.PostAsync($"/v1/jobs/{theirs.Id()}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["fullName"] = "Someone Else", ["email"] = email },
        })).Expect(HttpStatusCode.Conflict);
        refused.Body.GetRawText().Should().NotContain("Morgan").And.NotContain("Quill");

        // A new address, or one on a job they see, is added as usual.
        await recruiter.CreateApplicationAsync(theirs.Id(), ApiSession.NewEmail("new"));
        (await owner.PostAsync($"/v1/jobs/{theirs.Id()}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["fullName"] = "Morgan Quill", ["email"] = email },
        })).Expect(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_mention_names_only_people_who_can_see_the_candidate()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Backend Engineer"));
        var application = (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"))).Id();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");

        (await owner.PostAsync($"/v1/applications/{application}/notes", new JsonObject
        {
            ["note"] = $"Worth a look {NoteMentions.Token(viewer.UserId)}",
        })).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("note");

        await owner.SetHiringTeamAsync(job.Id(), await owner.MemberIdAsync(viewer.UserId));
        (await owner.PostAsync($"/v1/applications/{application}/notes", new JsonObject
        {
            ["note"] = $"Worth a look {NoteMentions.Token(viewer.UserId)}",
        })).Expect(HttpStatusCode.Created);
    }

    // The worker sees every job, so its mention handler checks the job itself: someone taken off the team between the
    // note and the email gets no email about the candidate. The mention is sent once while they are on the team, then
    // made unsent again and redelivered after they leave it.
    [Fact]
    public async Task A_mention_email_does_not_go_to_someone_taken_off_the_job_since()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var viewerEmail = ApiSession.NewEmail("viewer");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", viewerEmail, jobAccess: "HiringTeamOnly");
        var viewerMember = await owner.MemberIdAsync(viewer.UserId);
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Backend Engineer"));
        var application = (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"))).Id();
        await owner.SetHiringTeamAsync(job.Id(), viewerMember);

        var noteId = (await owner.PostAsync($"/v1/applications/{application}/notes", new JsonObject
        {
            ["note"] = $"Worth a look {NoteMentions.Token(viewer.UserId)}",
        })).Expect(HttpStatusCode.Created).Id("noteId");
        await WolverineStore.WaitUntilAsync(async () => (await MentionEmailsAsync(viewerEmail)).Count >= 1, "the mention email reaches the viewer", Ct);

        var tenant = new TenantContext();
        tenant.SetOrganization(owner.OrganizationId);
        Guid mentionId;
        await using (var db = await fixture.CreateDbContextAsync(tenant, Ct))
        {
            mentionId = await db.ApplicationNoteMentions.Where(m => m.NoteId == noteId && m.UserId == viewer.UserId).Select(m => m.Id).SingleAsync(Ct);
        }

        // The handler sends the email before it commits EmailedAt, so wait for the commit before making the mention unsent.
        await WolverineStore.WaitUntilAsync(async () =>
        {
            await using var check = await fixture.CreateDbContextAsync(tenant, Ct);
            return await check.ApplicationNoteMentions.AnyAsync(m => m.Id == mentionId && m.EmailedAt != null, Ct);
        }, "the worker to record the first mention email", Ct);

        (await owner.PutAsync($"/v1/jobs/{job.Id()}/hiring-team", new JsonObject { ["memberIds"] = new JsonArray() })).Expect(HttpStatusCode.OK);
        await using (var db = await fixture.CreateDbContextAsync(tenant, Ct))
        {
            await db.ApplicationNoteMentions.Where(m => m.Id == mentionId)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.EmailedAt, (DateTimeOffset?)null), Ct);
        }

        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await using (var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct))
        {
            await publisher.Services.GetRequiredService<IMessageBus>().PublishAsync(new TeammateMentioned(
                owner.OrganizationId, application, job.Id(), noteId, mentionId, viewer.UserId, owner.UserId, DateTimeOffset.UtcNow));
            await WolverineStore.WaitUntilAsync(
                async () => await store.CountHandledAsync(mentionId.ToString(), Ct, nameof(TeammateMentioned)) >= 2, "the worker to handle the mention again", Ct);
        }

        (await MentionEmailsAsync(viewerEmail)).Should().HaveCount(1, "off the team, the candidate is out of sight");
        await using var check = await fixture.CreateDbContextAsync(tenant, Ct);
        (await check.ApplicationNoteMentions.SingleAsync(m => m.Id == mentionId, Ct)).EmailedAt.Should().BeNull();
    }

    // The job filter runs in the query, before the newest 200 entries are taken. Filtered afterwards, 200 newer entries
    // about a job the reader cannot see would leave them an empty log.
    [Fact]
    public async Task The_membership_log_shows_a_reader_on_their_jobs_an_old_entry_about_their_job_under_many_newer_hidden_ones()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var theirs = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        var hidden = await owner.CreateJobAsync(Samples.RemoteJob("Payroll Specialist"));

        // A custom role that reads the log (Manage team), copied from Recruiter. Owners and Admins see every job, so a
        // custom role is how a reader on Only jobs they're on gets the log.
        var copy = (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Team reader", ["copyFrom"] = "Recruiter" }))
            .Expect(HttpStatusCode.Created);
        var roleId = copy.Id();
        (await owner.PutAsync($"/v1/organization/roles/{roleId}", new JsonObject
        {
            ["name"] = "Team reader",
            ["permissions"] = new JsonArray([.. copy.At("permissions").EnumerateArray().Select(p => (JsonNode)p.GetString()!), "ManageTeam"]),
        })).Expect(HttpStatusCode.OK);
        using var reader = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        await owner.GiveRoleAsync(reader, roleId);
        await owner.SetHiringTeamAsync(theirs.Id(), await owner.MemberIdAsync(reader.UserId));

        // 205 later entries about the hidden job, at a fixed time after any real one.
        var tenant = new TenantContext();
        tenant.SetOrganization(owner.OrganizationId);
        await using (var db = await fixture.CreateDbContextAsync(tenant, Ct))
        {
            var start = new DateTimeOffset(2090, 1, 1, 0, 0, 0, TimeSpan.Zero);
            for (var i = 0; i < 205; i++)
            {
                db.MembershipEvents.Add(new MembershipEvent
                {
                    OrganizationId = owner.OrganizationId,
                    Kind = i % 2 == 0 ? MembershipEventKind.AddedToHiringTeam : MembershipEventKind.RemovedFromHiringTeam,
                    ActorUserId = owner.UserId,
                    MemberUserId = reader.UserId,
                    JobId = hidden.Id(),
                    OccurredAt = start.AddMinutes(i),
                });
            }

            await db.SaveChangesAsync(Ct);
        }

        var readerLog = (await reader.GetAsync("/v1/organization/membership-log")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        readerLog.Should().NotContain(e => e.GetProperty("jobId").ValueKind == JsonValueKind.String && e.GetProperty("jobId").GetGuid() == hidden.Id(),
            "the log never names a job the reader cannot see");
        readerLog.Should().Contain(e => e.GetProperty("kind").GetString() == "AddedToHiringTeam" && e.GetProperty("jobId").GetGuid() == theirs.Id()
            && e.GetProperty("jobTitle").GetString() == "Platform Engineer", "the older entry about their own job is still there");

        var ownerLog = (await owner.GetAsync("/v1/organization/membership-log")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        ownerLog.Should().HaveCount(200, "the log is the newest 200 entries");
        ownerLog.Should().OnlyContain(e => e.GetProperty("jobId").GetGuid() == hidden.Id(), "the owner sees every job, and these are the newest 200");
    }

    // A key has no member, so no creator joins the team: the job starts with the template's default team only, and the
    // log names the key as the actor (ADR-0015).
    [Fact]
    public async Task A_job_an_api_key_creates_from_a_template_gets_the_default_team_and_the_log_names_the_key()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        var viewerMember = await owner.MemberIdAsync(viewer.UserId);
        var (templateId, versionId) = await owner.CreatePublishedTemplateAsync("Keyed roles");
        (await owner.PutAsync($"/v1/templates/{templateId}/hiring-team", new JsonObject
        {
            ["memberIds"] = new JsonArray(JsonValue.Create(viewerMember)),
        })).Expect(HttpStatusCode.OK);
        var created = (await owner.PostAsync("/v1/organization/api-keys", new JsonObject
        {
            ["name"] = "Sync", ["scopes"] = new JsonArray("Read", "Write"),
        })).Expect(HttpStatusCode.Created);
        var keyId = created.Id("key.id");
        using var client = new HttpClient { BaseAddress = fixture.GetEndpoint("api") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Text("token"));

        using var response = await client.PostAsJsonAsync("/v1/jobs", new JsonObject { ["title"] = "Senior Keyed Engineer", ["templateVersionId"] = versionId }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var jobId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var team = (await owner.GetAsync($"/v1/jobs/{jobId}/hiring-team")).Expect(HttpStatusCode.OK);
        team.Body.EnumerateArray().Select(m => m.GetProperty("memberId").GetGuid()).Should().Equal([viewerMember], "no creator joins when a key makes the job");
        (await viewer.GetAsync($"/v1/jobs/{jobId}")).Expect(HttpStatusCode.OK);

        var entry = (await owner.GetAsync("/v1/organization/membership-log")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "AddedToHiringTeam"
                && e.GetProperty("jobId").ValueKind == JsonValueKind.String && e.GetProperty("jobId").GetGuid() == jobId);
        entry.GetProperty("memberUserId").GetGuid().Should().Be(viewer.UserId);
        entry.GetProperty("actorApiKeyId").GetString().Should().Be(keyId.ToString());
        entry.GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private async Task<IReadOnlyList<MailpitMessage>> MentionEmailsAsync(string address) =>
        [.. (await fixture.Mailpit.MessagesAsync(address, Ct)).Where(m => m.Subject.Contains("mentioned you", StringComparison.Ordinal))];

    [Fact]
    public async Task Tasks_on_a_job_a_member_does_not_see_stay_out_of_their_task_list()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");
        var job = await AutomationSamples.JobWithAutomationsAsync(owner, "Platform Engineer", new JsonObject
        {
            ["id"] = "prepare",
            ["name"] = "Prepare the interview",
            ["trigger"] = new JsonObject { ["type"] = "Manual" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "CreateTask", ["note"] = "Call the references." }),
        });
        var application = (await owner.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"))).Id();
        (await owner.PostAsync($"/v1/applications/{application}/automations/prepare/run")).Expect(HttpStatusCode.Accepted);
        await AutomationSamples.WaitForRunAsync(owner, application, "prepare", "Completed");

        (await owner.GetAsync("/v1/tasks?scope=Everyone")).Expect(HttpStatusCode.OK).Count().Should().Be(1);
        (await recruiter.GetAsync("/v1/tasks?scope=Everyone")).Expect(HttpStatusCode.OK).Count().Should().Be(0);

        await owner.SetHiringTeamAsync(job, await owner.MemberIdAsync(recruiter.UserId));
        (await recruiter.GetAsync("/v1/tasks?scope=Everyone")).Expect(HttpStatusCode.OK).Count().Should().Be(1);
    }

    [Fact]
    public async Task Row_level_security_alone_hides_a_job_outside_the_members_teams()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Hidden Role"));
        var application = (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"))).Id();
        (await owner.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = "A note." })).Expect(HttpStatusCode.Created);
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        var viewerMember = await owner.MemberIdAsync(viewer.UserId);

        // The query filters are off here, so only the job_scope policies stand between the member and the rows.
        async Task<(int Jobs, int Applications, int Notes, int Events)> CountAsync(Action<TenantContext> scope)
        {
            var tenant = new TenantContext();
            tenant.SetUser(viewer.UserId);
            tenant.SetOrganization(owner.OrganizationId);
            scope(tenant);
            await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
            return (
                await db.Jobs.IgnoreQueryFilters().CountAsync(j => j.OrganizationId == owner.OrganizationId, Ct),
                await db.Applications.IgnoreQueryFilters().CountAsync(a => a.OrganizationId == owner.OrganizationId, Ct),
                await db.ApplicationNotes.IgnoreQueryFilters().CountAsync(n => n.OrganizationId == owner.OrganizationId, Ct),
                await db.ApplicationEvents.IgnoreQueryFilters().CountAsync(e => e.OrganizationId == owner.OrganizationId, Ct));
        }

        (await CountAsync(t => t.SetMemberScope(viewerMember, seesAll: false))).Should().Be((0, 0, 0, 0));
        (await CountAsync(_ => { })).Should().Be((0, 0, 0, 0), "a user whose job access was never resolved sees no jobs");
        (await CountAsync(t => t.SetMemberScope(viewerMember, seesAll: true))).Jobs.Should().Be(1);

        await owner.SetHiringTeamAsync(job.Id(), viewerMember);
        var onTeam = await CountAsync(t => t.SetMemberScope(viewerMember, seesAll: false));
        onTeam.Jobs.Should().Be(1);
        onTeam.Applications.Should().Be(1);
        onTeam.Notes.Should().Be(1);
        onTeam.Events.Should().BePositive();
    }

    [Fact]
    public async Task Every_table_under_a_job_has_a_restrictive_job_scope_policy()
    {
        await using var connection = new Npgsql.NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);

        // A new table with a job or an application id that forgot its job_scope policy would show a member on Only jobs
        // they're on the rows of every job. Candidates are the one exception (ADR-0015), and the membership log keeps job
        // ids of jobs a member may not see, filtered when it is read.
        await using var command = new Npgsql.NpgsqlCommand("""
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'public'
            WHERE c.relkind = 'r'
              AND EXISTS (SELECT FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname IN ('job_id', 'application_id') AND NOT a.attisdropped)
              AND c.relname NOT IN ('membership_events')
              AND NOT EXISTS (SELECT FROM pg_policies p
                              WHERE p.schemaname = 'public' AND p.tablename = c.relname
                                AND p.policyname = 'job_scope' AND p.permissive = 'RESTRICTIVE')
            """, connection);
        var unscoped = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                unscoped.Add(reader.GetString(0));
            }
        }

        unscoped.Should().BeEmpty("every table under a job needs a job_scope policy (migration AddJobAccess)");
    }
}
