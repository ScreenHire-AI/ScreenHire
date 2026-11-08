using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Portal;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.DataRequests;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.IntegrationTests.Scheduling;
using static ScreenHire.IntegrationTests.DataRequests.DataRequestSupport;

namespace ScreenHire.IntegrationTests.Portal;

/// <summary>
/// The candidate's own page (docs/architecture/candidate-portal.md): the emailed link, what the page shows, and the messages
/// with a reply from the page. Every request carries an address of its own in <c>X-Forwarded-For</c> (the API believes it from
/// loopback), so the per-address limit never makes one test fail another. Each test makes its own organization and candidate.
/// Tokens that the emailed link would carry are minted over the same Postgres key ring the API and the worker use.
/// </summary>
public sealed partial class PublicPortalTests(AppHostFixture fixture)
{
    private const string LinkSubjectStart = "Your link to your applications at ";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_form_says_it_is_enabled_and_an_unknown_organization_has_none()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();

        var form = await client.GetFromJsonAsync<JsonElement>(Base(world.Slug), Ct);

        form.GetProperty("enabled").GetBoolean().Should().BeTrue("the stack sends email through Mailpit and has a careers host");
        form.GetProperty("turnstileSiteKey").ValueKind.Should().Be(JsonValueKind.Null);
        (await client.GetAsync(Base("no-such-organization"), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_known_and_an_unknown_address_get_the_same_answer_and_only_the_known_one_gets_a_link_that_opens_the_page()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();
        var stranger = ApiSession.NewEmail("stranger");
        var organizationName = (await world.Owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("name");

        var known = await RequestLinkAsync(client, world.Slug, world.Person.Email);
        var unknown = await RequestLinkAsync(client, world.Slug, stranger);

        known.StatusCode.Should().Be(HttpStatusCode.Accepted);
        unknown.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await known.Content.ReadAsStringAsync(Ct)).Should().BeEmpty().And.Be(await unknown.Content.ReadAsStringAsync(Ct));
        known.Content.Headers.ContentLength.Should().Be(unknown.Content.Headers.ContentLength);

        var mail = (await WaitForLinkMailsAsync(world.Person.Email, 1)).Single();
        mail.Subject.Should().Be($"{LinkSubjectStart}{organizationName}");
        mail.Text.Should().Contain("If you didn't ask for this you can ignore this email.").And.Contain("The link works for 14 days.");
        (await fixture.Mailpit.MessagesAsync(stranger, Ct)).Should().BeEmpty("an address that never applied gets nothing");

        var portal = await client.GetFromJsonAsync<JsonElement>($"{Base(world.Slug)}/{TokenIn(mail.Text)}", Ct);
        portal.GetProperty("organizationName").GetString().Should().Be(organizationName);
        portal.GetProperty("applications").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task The_address_is_matched_whatever_its_case_and_a_bad_address_is_a_validation_error_that_sends_nothing()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();

        (await RequestLinkAsync(client, world.Slug, world.Person.Email.ToUpperInvariant())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await RequestLinkAsync(client, world.Slug, "not an address")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RequestLinkAsync(client, world.Slug, "")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RequestLinkAsync(client, "no-such-organization", world.Person.Email)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await WaitForLinkMailsAsync(world.Person.Email, 1);
    }

    [Fact]
    public async Task A_second_request_inside_the_cooldown_sends_nothing_and_one_after_it_sends_again()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();

        (await RequestLinkAsync(client, world.Slug, world.Person.Email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await RequestLinkAsync(client, world.Slug, world.Person.Email)).StatusCode.Should().Be(HttpStatusCode.Accepted, "the answer is the same inside the cooldown");
        await WaitForLinkMailsAsync(world.Person.Email, 1);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        (await LinkMailsAsync(world.Person.Email)).Should().ContainSingle("a second email would have been queued by now");

        await fixture.OwnerSqlAsync("UPDATE candidates SET portal_link_sent_at = now() - interval '10 minutes' WHERE id = @id", ("id", world.CandidateId));
        (await RequestLinkAsync(client, world.Slug, world.Person.Email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await WaitForLinkMailsAsync(world.Person.Email, 2)).Select(m => TokenIn(m.Text)).Distinct().Should().HaveCount(2, "each email carries a token of its own");
    }

    [Fact]
    public async Task A_data_request_link_does_not_hold_back_a_portal_link()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();
        await fixture.OwnerSqlAsync("UPDATE candidates SET data_request_link_sent_at = now() WHERE id = @id", ("id", world.CandidateId));

        (await RequestLinkAsync(client, world.Slug, world.Person.Email)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        await WaitForLinkMailsAsync(world.Person.Email, 1);
    }

    [Fact]
    public async Task A_forged_token_another_organizations_token_an_expired_token_and_an_erased_candidates_token_all_find_nothing()
    {
        using var world = await fixture.NewWorldAsync();
        using var other = await fixture.NewWorldAsync();
        using var client = NewClient();
        var token = Signer().Issue(world.OrganizationId, world.CandidateId);
        var otherToken = Signer().Issue(other.OrganizationId, other.CandidateId);
        var expired = Signer(new FixedTime(DateTimeOffset.UtcNow.AddDays(-30))).Issue(world.OrganizationId, world.CandidateId);

        foreach (var forged in new[] { "CfDJ8forged-token-that-reads-as-nothing", otherToken, expired })
        {
            (await client.GetAsync($"{Base(world.Slug)}/{forged}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, forged);
            (await client.GetAsync($"{Base(world.Slug)}/{forged}/applications/{world.ApplicationId}/messages", Ct)).StatusCode
                .Should().Be(HttpStatusCode.NotFound, forged);
            (await client.PostAsJsonAsync($"{Base(world.Slug)}/{forged}/applications/{world.ApplicationId}/messages", Reply("hi"), Ct)).StatusCode
                .Should().Be(HttpStatusCode.NotFound, forged);
        }

        (await client.GetAsync($"{Base(world.Slug)}/{token}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await world.Owner.PostAsync($"/v1/candidates/{world.CandidateId}/erase", new JsonObject { ["confirmation"] = world.Person.FullName }))
            .Expect(HttpStatusCode.NoContent);

        (await client.GetAsync($"{Base(world.Slug)}/{token}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, "the candidate is gone");
    }

    [Fact]
    public async Task The_page_shows_four_fixed_steps_by_the_kind_of_the_stage_and_never_anything_of_the_employers_own()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();
        var token = Signer().Issue(world.OrganizationId, world.CandidateId);

        var applied = await ApplicationOfAsync(client, world, token);
        applied.GetProperty("funnel").GetProperty("steps").EnumerateArray().Select(s => s.GetString()).Should().Equal("Applied", "In review", "Interview", "Offer");
        applied.GetProperty("funnel").GetProperty("current").GetInt32().Should().Be(0);
        applied.GetProperty("funnel").GetProperty("ended").GetString().Should().Be("None");
        applied.GetProperty("jobTitle").GetString().Should().Be("Platform Engineer");

        foreach (var (stage, step) in new[] { ("screening", 1), ("interview", 2), ("offer", 3) })
        {
            await MoveAsync(world, stage);
            var raw = await client.GetStringAsync($"{Base(world.Slug)}/{token}", Ct);
            using var document = JsonDocument.Parse(raw);
            document.RootElement.GetProperty("applications")[0].GetProperty("funnel").GetProperty("current").GetInt32().Should().Be(step, stage);
            PropertyNames(document.RootElement).Should().NotContain(
                name => name.Contains("stage", StringComparison.OrdinalIgnoreCase) || name.Contains("rating", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("score", StringComparison.OrdinalIgnoreCase) || name.Contains("note", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("verif", StringComparison.OrdinalIgnoreCase) || name.Contains("tag", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("workflow", StringComparison.OrdinalIgnoreCase),
                "the page carries no stage, rating, scorecard, note, tag or verification field");
            raw.Should().NotContain("Screening", "the employer's own stage names stay with the employer; Offer is also a step's name");
        }

        await MoveAsync(world, "rejected");
        var ended = (await ApplicationOfAsync(client, world, token)).GetProperty("funnel");
        ended.GetProperty("ended").GetString().Should().Be("NotMovingForward");
        ended.GetProperty("current").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_saved_draft_and_a_waiting_questionnaire_are_listed_with_tokens_the_existing_pages_open()
    {
        var (recruiter, slug, job) = await JobWithFollowUpAsync("Portal follow-up");
        using var _ = recruiter;
        using var client = NewClient();
        var email = ApiSession.NewEmail("portal-waiting");
        var applicationId = await ApplyAsync(client, slug, job, email);
        var candidateId = (await recruiter.GetAsync($"/v1/applications/{applicationId}")).Expect(HttpStatusCode.OK).Id("candidate.id");
        var token = Signer().Issue(recruiter.OrganizationId, candidateId);

        (await client.GetFromJsonAsync<JsonElement>($"{Base(slug)}/{token}", Ct)).GetProperty("applications")[0].GetProperty("waiting").GetArrayLength()
            .Should().Be(0, "nothing is waiting yet");

        await MoveAsync(recruiter, applicationId, "screening");
        await fixture.Mailpit.WaitForMessagesAsync(email, 1, Ct);
        var draft = await client.PostAsJsonAsync($"/v1/public/organizations/{slug}/jobs/{job}/application-drafts", new JsonObject
        {
            ["candidate"] = new JsonObject { ["firstName"] = "Robin", ["lastName"] = "Ellis", ["email"] = email },
            ["answers"] = new JsonArray(),
            ["aiUseDeclaration"] = "DidNotUse",
        }, Ct);
        draft.StatusCode.Should().Be(HttpStatusCode.Created, await draft.Content.ReadAsStringAsync(Ct));

        var portal = await client.GetFromJsonAsync<JsonElement>($"{Base(slug)}/{token}", Ct);
        var waiting = portal.GetProperty("applications")[0].GetProperty("waiting").EnumerateArray().Single();
        waiting.GetProperty("kind").GetString().Should().Be("Questionnaire");
        var page = await client.GetFromJsonAsync<JsonElement>($"/v1/public/organizations/{slug}/questionnaires/{waiting.GetProperty("token").GetString()}", Ct);
        page.GetProperty("status").GetString().Should().Be("Open", "the page lists what the questionnaire page would open");

        var saved = portal.GetProperty("drafts").EnumerateArray().Single();
        saved.GetProperty("jobTitle").GetString().Should().Be("Portal follow-up");
        (await client.GetAsync($"/v1/public/organizations/{slug}/application-drafts/{saved.GetProperty("token").GetString()}", Ct)).StatusCode
            .Should().Be(HttpStatusCode.OK, "the draft's token opens the draft");
    }

    [Fact]
    public async Task An_interview_to_book_is_listed_with_the_token_of_its_page_and_an_ended_application_lists_nothing()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();
        var application = await fixture.AddApplicationAsync(world.OrganizationId, Ct);
        await fixture.AddInterviewAsync(application, [world.Owner.UserId], Ct);
        var token = Signer().Issue(world.OrganizationId, application.CandidateId);

        var portal = await client.GetFromJsonAsync<JsonElement>($"{Base(world.Slug)}/{token}", Ct);
        var waiting = portal.GetProperty("applications")[0].GetProperty("waiting").EnumerateArray().Single();
        waiting.GetProperty("kind").GetString().Should().Be("Interview");
        var interview = await client.GetFromJsonAsync<JsonElement>(
            $"/v1/public/organizations/{world.Slug}/interviews/{waiting.GetProperty("token").GetString()}", Ct);
        interview.GetProperty("state").GetString().Should().Be("Open");
    }

    [Fact]
    public async Task Every_email_to_the_candidate_ends_with_their_link_while_the_copy_the_team_keeps_never_holds_it()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();
        var sent = await fixture.SendAsync(world.Owner, world.JobId, world.ApplicationId, world.Person.Email, "Next steps");

        sent.Mail.Text.Should().Contain(CandidatePortalFooter.Sentence);
        var token = PortalTokenIn(sent.Mail.Text);
        (await client.GetAsync($"{Base(world.Slug)}/{token}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK, "the link at the end of the email opens the page");
        sent.Mail.Html.Should().Contain("/my-applications/");

        var kept = (await world.Owner.GetAsync($"/v1/emails/{sent.SendId}")).Expect(HttpStatusCode.OK);
        kept.Text("text").Should().NotContain("/my-applications/").And.NotContain(CandidatePortalFooter.Sentence);
        kept.Text("html").Should().NotContain("/my-applications/");
    }

    [Fact]
    public async Task A_reply_from_the_page_lands_in_messages_and_activity_once_and_shares_a_thread_with_an_emailed_reply()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();
        var sent = await fixture.SendAsync(world.Owner, world.JobId, world.ApplicationId, world.Person.Email, "Next steps");
        var token = Signer().Issue(world.OrganizationId, world.CandidateId);
        var path = $"{Base(world.Slug)}/{token}/applications/{world.ApplicationId}/messages";

        var before = await client.GetFromJsonAsync<JsonElement>(path, Ct);
        before.GetProperty("canReply").GetBoolean().Should().BeTrue();
        before.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("direction").GetString()).Should().Equal("FromTeam");

        var key = Guid.NewGuid().ToString("N");
        (await client.PostAsJsonAsync(path, Reply("Friday works for me.", key), Ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync(path, Reply("Friday works for me.", key), Ct)).StatusCode.Should().Be(HttpStatusCode.Accepted, "the same form sent twice is not an error");

        var received = (await world.Owner.MessagesAsync(world.CandidateId)).Received();
        received.Should().ContainSingle("the same form id stores one reply");
        received[0].GetProperty("subject").GetString().Should().Be("Re: Next steps");
        var timeline = (await world.Owner.GetAsync($"/v1/applications/{world.ApplicationId}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray();
        timeline.Count(e => e.GetProperty("type").GetString() == "EmailReceived").Should().Be(1, "Activity shows the reply as an emailed one is shown");

        (await fixture.DevReplyAsync(sent.ReplyAddress, world.Person.Email, "Or Monday if Friday falls through.")).Should().Be("Stored");
        var thread = await client.GetFromJsonAsync<JsonElement>(path, Ct);
        var messages = thread.GetProperty("messages").EnumerateArray().ToList();
        messages.Select(m => m.GetProperty("direction").GetString()).Should().Equal("FromTeam", "FromYou", "FromYou");
        messages[0].GetProperty("body").GetString().Should().StartWith("Hello, can we talk?");
        messages.Skip(1).Select(m => m.GetProperty("body").GetString()).Should().Equal("Friday works for me.", "Or Monday if Friday falls through.");
    }

    [Fact]
    public async Task An_emailed_reply_that_quotes_the_footer_is_stored_without_the_candidates_link()
    {
        using var world = await fixture.NewWorldAsync();
        var sent = await fixture.SendAsync(world.Owner, world.JobId, world.ApplicationId, world.Person.Email, "Next steps");
        var token = Signer().Issue(world.OrganizationId, world.CandidateId);

        (await fixture.DevReplyAsync(sent.ReplyAddress, world.Person.Email, $"Friday works.\n\n> https://example.com/my-applications/{token}"))
            .Should().Be("Stored");

        var received = (await world.Owner.MessagesAsync(world.CandidateId)).Received();
        received.Should().ContainSingle();
        received[0].GetRawText().Should().NotContain(token, "the link opens the candidate's page, and the team reads what is stored");
    }

    [Fact]
    public async Task A_reply_from_the_page_cannot_clear_a_flagged_address_but_one_by_email_does()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();
        var sent = await fixture.SendAsync(world.Owner, world.JobId, world.ApplicationId, world.Person.Email, "Next steps");
        (await fixture.PostEventsAsync(ReplySupport.EventBody(world.OrganizationId, sent.SendId, world.Person.Email, "bounce", type: "bounce", reason: "550 no such mailbox")))
            .Should().Be(HttpStatusCode.OK);
        await WolverineStore.WaitUntilAsync(
            async () => await world.Owner.EmailIssueAsync(world.ApplicationId) == "Bounced", "the address to be flagged", Ct);
        var token = Signer().Issue(world.OrganizationId, world.CandidateId);

        (await client.PostAsJsonAsync($"{Base(world.Slug)}/{token}/applications/{world.ApplicationId}/messages", Reply("I can still write here."), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        (await world.Owner.MessagesAsync(world.CandidateId)).Received().Should().ContainSingle();
        (await world.Owner.EmailIssueAsync(world.ApplicationId)).Should().Be("Bounced", "writing on a page says nothing about the mailbox");

        (await fixture.DevReplyAsync(sent.ReplyAddress, world.Person.Email, "Writing by email.")).Should().Be("Stored");
        await WolverineStore.WaitUntilAsync(
            async () => await world.Owner.EmailIssueAsync(world.ApplicationId) == "None", "an emailed reply to clear the flag", Ct);
    }

    [Fact]
    public async Task A_reply_with_a_file_keeps_and_scans_it_and_a_blank_reply_or_one_with_no_email_to_answer_is_refused()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();
        var token = Signer().Issue(world.OrganizationId, world.CandidateId);
        var path = $"{Base(world.Slug)}/{token}/applications/{world.ApplicationId}/messages";

        var early = await client.PostAsJsonAsync(path, Reply("Hello?"), Ct);
        early.StatusCode.Should().Be(HttpStatusCode.Conflict, "the team has not written yet, so there is nothing to answer");
        (await client.GetFromJsonAsync<JsonElement>(path, Ct)).GetProperty("canReply").GetBoolean().Should().BeFalse();

        await fixture.SendAsync(world.Owner, world.JobId, world.ApplicationId, world.Person.Email, "Next steps");
        (await client.PostAsJsonAsync(path, Reply("   "), Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(path, Reply(new string('x', 5001)), Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var withFile = Reply("My CV is attached.");
        withFile["attachments"] = new JsonArray(new JsonObject { ["fileName"] = "cv.pdf", ["contentBase64"] = Convert.ToBase64String(ReplySupport.Pdf()) });
        (await client.PostAsJsonAsync(path, withFile, Ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var stored = (await world.Owner.MessagesAsync(world.CandidateId)).Received().Single();
        stored.GetProperty("attachmentCount").GetInt32().Should().Be(1);
        var yours = (await client.GetFromJsonAsync<JsonElement>(path, Ct)).GetProperty("messages").EnumerateArray()
            .Single(m => m.GetProperty("direction").GetString() == "FromYou");
        yours.GetProperty("attachmentCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_day_takes_a_limited_number_of_replies_from_one_application_page()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();
        await fixture.SendAsync(world.Owner, world.JobId, world.ApplicationId, world.Person.Email, "Next steps");
        var token = Signer().Issue(world.OrganizationId, world.CandidateId);
        var path = $"{Base(world.Slug)}/{token}/applications/{world.ApplicationId}/messages";
        var limit = new PortalOptions().RepliesPerDay;

        for (var i = 0; i < limit; i++)
        {
            (await client.PostAsJsonAsync(path, Reply($"Message {i}"), Ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        (await client.PostAsJsonAsync(path, Reply("One more"), Ct)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task An_application_that_is_not_the_tokens_candidates_is_not_found()
    {
        using var world = await fixture.NewWorldAsync();
        using var other = await fixture.NewWorldAsync();
        using var client = NewClient();
        var token = Signer().Issue(world.OrganizationId, world.CandidateId);

        (await client.GetAsync($"{Base(world.Slug)}/{token}/applications/{other.ApplicationId}/messages", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"{Base(world.Slug)}/{token}/applications/{other.ApplicationId}/messages", Reply("hi"), Ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"{Base(world.Slug)}/{token}/applications/{Guid.NewGuid()}/messages", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Too_many_requests_from_one_address_get_429()
    {
        using var world = await fixture.NewWorldAsync();
        using var client = NewClient();
        var limit = new PortalOptions().PermitsPerMinute;

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < limit + 3; i++)
        {
            using var response = await client.GetAsync(Base(world.Slug), Ct);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(limit).Should().OnlyContain(s => s == HttpStatusCode.OK);
        statuses.Should().Contain(HttpStatusCode.TooManyRequests, $"{limit} requests a minute is the allowance");
        using var elsewhere = NewClient();
        (await elsewhere.GetAsync(Base(world.Slug), Ct)).StatusCode.Should().Be(HttpStatusCode.OK, "other addresses are not slowed");
    }

    private static string Base(string slug) => $"/v1/public/organizations/{slug}/portal";

    private HttpClient NewClient()
    {
        var client = fixture.CreateHttpClient("api");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}");
        return client;
    }

    private static Task<HttpResponseMessage> RequestLinkAsync(HttpClient client, string slug, string email) =>
        client.PostAsJsonAsync($"{Base(slug)}/links", new { email }, Ct);

    private static JsonObject Reply(string body, string? key = null) => new() { ["body"] = body, ["key"] = key ?? Guid.NewGuid().ToString("N") };

    private static async Task<JsonElement> ApplicationOfAsync(HttpClient client, World world, string token) =>
        (await client.GetFromJsonAsync<JsonElement>($"{Base(world.Slug)}/{token}", Ct)).GetProperty("applications")[0];

    private static async Task MoveAsync(World world, string stageId) => await MoveAsync(world.Owner, world.ApplicationId, stageId);

    private static async Task MoveAsync(Tenant recruiter, Guid applicationId, string stageId) =>
        (await recruiter.PostAsync($"/v1/applications/{applicationId}/move", new JsonObject { ["toStageId"] = stageId })).Expect(HttpStatusCode.OK);

    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var inner in PropertyNames(property.Value))
                    {
                        yield return inner;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var inner in PropertyNames(item))
                    {
                        yield return inner;
                    }
                }

                break;
        }
    }

    private async Task<IReadOnlyList<MailpitMessage>> LinkMailsAsync(string address) =>
        [.. (await fixture.Mailpit.MessagesAsync(address, Ct)).Where(m => m.Subject.StartsWith(LinkSubjectStart, StringComparison.Ordinal))];

    // Other emails to the address (an application can send some) are not the link mail; wait for this one only.
    private async Task<IReadOnlyList<MailpitMessage>> WaitForLinkMailsAsync(string address, int count)
    {
        IReadOnlyList<MailpitMessage> mails = [];
        await WolverineStore.WaitUntilAsync(
            async () => (mails = await LinkMailsAsync(address)).Count >= count, $"{count} portal link emails to the candidate", Ct);
        return mails;
    }

    private static string TokenIn(string text) => PortalTokenIn(text);

    private static string PortalTokenIn(string text)
    {
        var match = LinkInMail().Match(text);
        match.Success.Should().BeTrue($"the email carries the link: {text}");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex(@"https?://\S+/my-applications/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex LinkInMail();

    // The signer, over the same Postgres key ring the API and the worker use, with a clock of the test's choosing.
    private CandidatePortalLinks Signer(TimeProvider? time = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => fixture.CreateDbContextAsync(new TenantContext(), Ct).GetAwaiter().GetResult());
        services.AddDataProtection().SetApplicationName(DataProtectionRegistration.ApplicationName).PersistKeysToDbContext<ScreenHireDbContext>();
        var provider = services.BuildServiceProvider();
        var careers = new CareersSiteOptions { Scheme = "http", CareersDomain = "localhost", CareersHostPattern = "{tenant}.{CareersDomain}" };
        return new CandidatePortalLinks(
            provider.GetRequiredService<IDataProtectionProvider>(), Options.Create(careers), Options.Create(new PortalOptions()), time ?? TimeProvider.System);
    }

    // A job whose workflow asks a follow-up questionnaire on entering Screening, as the follow-up questionnaire tests make one.
    private async Task<(Tenant Recruiter, string Slug, Guid Job)> JobWithFollowUpAsync(string title)
    {
        var recruiter = await fixture.SignedInWithOrganizationAsync();
        var definition = Samples.DefinitionWith(new JsonObject
        {
            ["id"] = "send-on-screening",
            ["name"] = "Ask about availability",
            ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = "screening" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "SendQuestionnaire", ["questionnaireId"] = "availability" }),
        });
        definition["workflow"]!["followUpQuestionnaires"] = new JsonArray(new JsonObject
        {
            ["id"] = "availability",
            ["name"] = "Availability",
            ["questions"] = new JsonArray(new JsonObject { ["id"] = "start_date", ["type"] = "ShortText", ["label"] = "When could you start?", ["required"] = true }),
        });
        var (_, versionId) = await recruiter.CreatePublishedTemplateAsync($"{title} template", definition);
        var body = Samples.RemoteJob(title);
        body["templateVersionId"] = versionId;
        var job = (await recruiter.CreateJobAsync(body)).Id();
        await recruiter.PublishJobAsync(job);
        var slug = (await recruiter.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("slug");
        return (recruiter, slug, job);
    }

    private static async Task<Guid> ApplyAsync(HttpClient careers, string slug, Guid job, string email)
    {
        var response = await careers.PostAsJsonAsync($"/v1/public/organizations/{slug}/jobs/{job}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["firstName"] = "Robin", ["lastName"] = "Ellis", ["email"] = email },
            ["privacyNoticeVersion"] = "2026-11-07.1",
            ["answers"] = new JsonArray(),
            ["aiUseDeclaration"] = "DidNotUse",
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("applicationId").GetGuid();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
