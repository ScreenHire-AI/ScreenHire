using System.Text;
using System.Text.Json.Nodes;
using ScreenHire.Application.Applications;
using ScreenHire.IntegrationTests.Realtime;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The email provider's two webhooks through the running API (docs/architecture/email.md, ADR-0022): SendGrid Inbound Parse
/// posts a candidate's reply (HTTP Basic credentials, the recipient read from the envelope), and SendGrid's signed event
/// webhook reports bounces and spam complaints that stop email to an address. Each test uses its own organization.
/// </summary>
public sealed class EmailWebhookTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string From(SentToCandidate sent) => $"Jordan Reyes <{sent.Address}>";

    [Fact]
    public async Task The_inbound_webhook_refuses_a_post_without_the_secret_and_stores_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);

        (await fixture.PostInboundAsync(sent.ReplyAddress, From(sent), "No credentials.", authorize: false)).Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.PostInboundAsync(sent.ReplyAddress, From(sent), "Wrong secret.", password: "not-the-secret")).Should().Be(HttpStatusCode.Unauthorized);

        (await owner.MessagesAsync(sent.CandidateId)).Received().Should().BeEmpty();
    }

    [Fact]
    public async Task A_reply_posted_with_the_secret_is_stored_with_its_sender_subject_and_text()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);

        var status = await fixture.PostInboundAsync(
            sent.ReplyAddress, From(sent), "Thursday works.", headers: "Message-ID: <inbound-1@mail.example.org>\r\nSubject: Re: Next steps",
            html: "<p>Thursday <i>works</i>.</p>");

        status.Should().Be(HttpStatusCode.OK);
        var received = (await owner.MessagesAsync(sent.CandidateId)).Received().Should().ContainSingle().Subject;
        received.GetProperty("fromAddress").GetString().Should().Be(sent.Address);
        var reply = (await owner.GetAsync($"/v1/replies/{received.GetProperty("id").GetGuid()}")).Expect(HttpStatusCode.OK);
        reply.Text("fromName").Should().Be("Jordan Reyes");
        reply.Text("subject").Should().Be("Re: Next steps");
        reply.Text("text").Should().Be("Thursday works.");
        reply.Text("html").Should().Contain("Thursday").And.Contain("<em>works</em>");
        reply.At("autoReply").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task The_recipient_is_read_from_the_envelope_so_an_edited_To_header_changes_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);

        (await fixture.PostInboundAsync(
            sent.ReplyAddress, From(sent), "Sent to a list.", toHeader: "everyone@lists.example.org")).Should().Be(HttpStatusCode.OK);

        (await owner.MessagesAsync(sent.CandidateId)).Received().Should().ContainSingle();
    }

    [Fact]
    public async Task A_post_whose_envelope_has_a_forged_address_answers_200_and_stores_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);
        var at = sent.ReplyAddress.IndexOf('+', StringComparison.Ordinal) + 11;
        var tampered = sent.ReplyAddress[..at] + (sent.ReplyAddress[at] == 'a' ? 'b' : 'a') + sent.ReplyAddress[(at + 1)..];

        (await fixture.PostInboundAsync(tampered, From(sent), "Forged.", toHeader: sent.ReplyAddress)).Should().Be(HttpStatusCode.OK, "SendGrid must not retry it");

        (await owner.MessagesAsync(sent.CandidateId)).Received().Should().BeEmpty("the To header is not what is trusted");
    }

    [Fact]
    public async Task The_same_message_id_posted_twice_is_stored_once()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);
        const string headers = "Message-ID: <twice@mail.example.org>";

        (await fixture.PostInboundAsync(sent.ReplyAddress, From(sent), "Once.", headers: headers)).Should().Be(HttpStatusCode.OK);
        (await fixture.PostInboundAsync(sent.ReplyAddress, From(sent), "Once.", headers: headers)).Should().Be(HttpStatusCode.OK);

        (await owner.MessagesAsync(sent.CandidateId)).Received().Should().ContainSingle();
    }

    [Fact]
    public async Task An_auto_submitted_reply_is_stored_as_an_automatic_answer_and_its_sender_is_not_emailed()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var me = await owner.EmailOfAsync();
        var auto = await fixture.SendToNewCandidateAsync(owner, "Ari Lane", "Hello Ari");
        var human = await fixture.SendToNewCandidateAsync(owner, "Robin Ellis", "Hello Robin");

        await fixture.PostInboundAsync(
            auto.ReplyAddress, From(auto), "Away until Monday.", headers: "Auto-Submitted: auto-replied\r\nMessage-ID: <auto@mail.example.org>");
        await fixture.PostInboundAsync(human.ReplyAddress, From(human), "Happy to talk.");

        var autoReply = (await owner.MessagesAsync(auto.CandidateId)).Received().Should().ContainSingle().Subject;
        (await owner.GetAsync($"/v1/replies/{autoReply.GetProperty("id").GetGuid()}")).Expect(HttpStatusCode.OK).At("autoReply").GetBoolean().Should().BeTrue();
        await fixture.WaitForSubjectAsync(me, "Robin Ellis replied");
        // Queue order is not guaranteed, so wait for the worker to have handled the automatic answer itself.
        var store = await Messaging.WolverineStore.ForAppHostAsync(fixture, Ct);
        await Messaging.WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(autoReply.GetProperty("id").GetGuid().ToString(), Ct, nameof(CandidateReplied)) >= 1,
            "the worker to handle the automatic answer",
            Ct);
        (await fixture.Mailpit.MessagesAsync(me, Ct)).Select(m => m.Subject).Should().NotContain("Ari Lane replied");
    }

    [Fact]
    public async Task A_pdf_is_kept_and_scanned_and_an_executable_is_dropped_with_a_note_in_the_text()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);

        (await fixture.PostInboundAsync(
            sent.ReplyAddress, From(sent), "Files attached.",
            attachments: [("portfolio.pdf", ReplySupport.Pdf()), ("installer.exe", [0x4D, 0x5A, 0x90, 0x00])])).Should().Be(HttpStatusCode.OK);

        var replyId = (await owner.MessagesAsync(sent.CandidateId)).Received().Single().GetProperty("id").GetGuid();
        var reply = (await owner.GetAsync($"/v1/replies/{replyId}")).Expect(HttpStatusCode.OK);
        reply.Count("attachments").Should().Be(1);
        reply.Text("attachments.0.fileName").Should().Be("portfolio.pdf");
        reply.Text("text").Should().Contain("Files attached.").And.Contain("1 attachment was not kept");
        var scanned = new List<string>();
        await Messaging.WolverineStore.WaitUntilAsync(
            async () =>
            {
                var status = (await owner.GetAsync($"/v1/replies/{replyId}")).Expect(HttpStatusCode.OK).Text("attachments.0.scanStatus");
                scanned.Add(status);
                return status != "Pending";
            },
            "the attachment's malware scan",
            Ct);
        scanned[^1].Should().BeOneOf("Clean", "NotScanned");
    }

    [Fact]
    public async Task A_pdf_over_10_MB_is_not_kept()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);

        (await fixture.PostInboundAsync(
            sent.ReplyAddress, From(sent), "A big file.", attachments: [("big.pdf", ReplySupport.Pdf(10 * 1024 * 1024 + 1))])).Should().Be(HttpStatusCode.OK);

        var replyId = (await owner.MessagesAsync(sent.CandidateId)).Received().Single().GetProperty("id").GetGuid();
        var reply = (await owner.GetAsync($"/v1/replies/{replyId}")).Expect(HttpStatusCode.OK);
        reply.Count("attachments").Should().Be(0);
        reply.Text("text").Should().Contain("1 attachment was not kept");
    }

    [Fact]
    public async Task The_event_webhook_refuses_an_unsigned_badly_signed_or_stale_post()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);
        var bounce = ReplySupport.EventBody(owner.OrganizationId, sent.SendId, sent.Address, "bounce", "bounce", "550 mailbox not found");

        (await fixture.PostEventsAsync(bounce, sign: false)).Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.PostEventsAsync(bounce, signedBody: "[]")).Should().Be(HttpStatusCode.Unauthorized, "the signature is for another body");
        (await fixture.PostEventsAsync(bounce, signedAt: DateTimeOffset.UtcNow.AddMinutes(-10))).Should().Be(HttpStatusCode.Unauthorized, "a captured post cannot be replayed");

        (await owner.EmailIssueAsync(sent.ApplicationId)).Should().Be("None");
    }

    [Fact]
    public async Task A_signed_body_that_is_not_json_is_a_bad_request()
    {
        (await fixture.PostEventsAsync("this is not json")).Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_sandbox_refuses_an_attachment_that_is_not_base64_as_a_bad_request()
    {
        using var client = fixture.AnonymousClient();
        var body = new JsonObject
        {
            ["to"] = "reply+not-a-token@reply.example.test",
            ["from"] = "jordan.reyes@example.com",
            ["attachments"] = new JsonArray(new JsonObject { ["fileName"] = "cv.pdf", ["contentBase64"] = "not base64!" }),
        };

        using var response = await client.PostAsync(
            "/v1/dev/inbound-email", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_signed_bounce_flags_the_candidate_and_further_emails_to_them_are_skipped_until_a_person_clears_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);
        await using var events = await EventsConnection.ConnectAsync(owner.Session, Ct);

        var bounce = ReplySupport.EventBody(owner.OrganizationId, sent.SendId, sent.Address, "bounce", "bounce", "550 5.1.1 mailbox not found");
        (await fixture.PostEventsAsync(bounce)).Should().Be(HttpStatusCode.OK);

        var application = (await owner.GetAsync($"/v1/applications/{sent.ApplicationId}")).Expect(HttpStatusCode.OK);
        application.Text("candidate.emailIssue").Should().Be("Bounced");
        application.Text("candidate.emailIssueReason").Should().Be("550 5.1.1 mailbox not found");
        application.At("candidate.emailIssueAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);
        await events.WaitForAsync("CandidateEmailStatusChanged", "candidateId", sent.CandidateId, Ct);

        var again = new JsonObject { ["subject"] = "Second try", ["body"] = "Hello again.", ["applicationIds"] = new JsonArray(JsonValue.Create(sent.ApplicationId)) };
        var skipped = (await owner.PostAsync("/v1/emails", again)).Expect(HttpStatusCode.Accepted);
        skipped.At("queued").GetInt32().Should().Be(0);
        skipped.At("skipped").GetInt32().Should().Be(1);

        var cleared = (await owner.PostAsync($"/v1/candidates/{sent.CandidateId}/email-issue/clear")).Expect(HttpStatusCode.OK);
        cleared.Text("emailIssue").Should().Be("None");
        (await owner.EmailIssueAsync(sent.ApplicationId)).Should().Be("None");
        var queued = (await owner.PostAsync("/v1/emails", again)).Expect(HttpStatusCode.Accepted);
        queued.At("queued").GetInt32().Should().Be(1);
        queued.At("skipped").GetInt32().Should().Be(0);
        await fixture.WaitForSubjectAsync(sent.Address, "Second try");
        (await fixture.Mailpit.MessagesAsync(sent.Address, Ct)).Count(m => m.Subject == "Second try").Should().Be(1);
    }

    [Fact]
    public async Task A_blocked_bounce_is_a_soft_failure_and_a_drop_flags_only_for_a_known_bounced_address()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);

        (await fixture.PostEventsAsync(ReplySupport.EventBody(owner.OrganizationId, sent.SendId, sent.Address, "bounce", "blocked", "blocked"))).Should().Be(HttpStatusCode.OK);
        (await fixture.PostEventsAsync(ReplySupport.EventBody(owner.OrganizationId, sent.SendId, sent.Address, "dropped", reason: "Spam Content"))).Should().Be(HttpStatusCode.OK);
        (await fixture.PostEventsAsync(ReplySupport.EventBody(owner.OrganizationId, sent.SendId, sent.Address, "delivered"))).Should().Be(HttpStatusCode.OK);
        (await owner.EmailIssueAsync(sent.ApplicationId)).Should().Be("None");

        (await fixture.PostEventsAsync(ReplySupport.EventBody(owner.OrganizationId, sent.SendId, sent.Address, "dropped", reason: "Bounced Address"))).Should().Be(HttpStatusCode.OK);
        (await owner.EmailIssueAsync(sent.ApplicationId)).Should().Be("Bounced");
    }

    [Fact]
    public async Task A_spam_report_flags_a_complaint_that_a_later_bounce_does_not_downgrade()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);

        (await fixture.PostEventsAsync(ReplySupport.EventBody(owner.OrganizationId, sent.SendId, sent.Address, "spamreport"))).Should().Be(HttpStatusCode.OK);
        (await owner.EmailIssueAsync(sent.ApplicationId)).Should().Be("SpamComplaint");

        (await fixture.PostEventsAsync(ReplySupport.EventBody(owner.OrganizationId, sent.SendId, sent.Address, "bounce", "bounce", "550"))).Should().Be(HttpStatusCode.OK);
        (await owner.EmailIssueAsync(sent.ApplicationId)).Should().Be("SpamComplaint");
    }

    [Fact]
    public async Task An_event_about_another_organizations_send_or_another_address_flags_no_one()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(ours);
        var other = await fixture.SendToNewCandidateAsync(theirs);

        // The batch names their organization but our send, and our organization but their send: neither finds a row.
        (await fixture.PostEventsAsync(ReplySupport.EventBody(theirs.OrganizationId, sent.SendId, sent.Address, "bounce", "bounce", "550"))).Should().Be(HttpStatusCode.OK);
        (await fixture.PostEventsAsync(ReplySupport.EventBody(ours.OrganizationId, other.SendId, other.Address, "bounce", "bounce", "550"))).Should().Be(HttpStatusCode.OK);
        // The right send, but the event is about an address the candidate does not have.
        (await fixture.PostEventsAsync(ReplySupport.EventBody(ours.OrganizationId, sent.SendId, "someone.else@example.com", "bounce", "bounce", "550"))).Should().Be(HttpStatusCode.OK);

        (await ours.EmailIssueAsync(sent.ApplicationId)).Should().Be("None");
        (await theirs.EmailIssueAsync(other.ApplicationId)).Should().Be("None");
    }

    [Fact]
    public async Task A_reply_from_the_flagged_address_clears_the_flag_and_one_from_another_address_does_not()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);
        (await fixture.PostEventsAsync(ReplySupport.EventBody(owner.OrganizationId, sent.SendId, sent.Address, "bounce", "bounce", "552 mailbox full")))
            .Should().Be(HttpStatusCode.OK);
        (await owner.EmailIssueAsync(sent.ApplicationId)).Should().Be("Bounced");

        await fixture.DevReplyAsync(sent.ReplyAddress, ApiSession.NewEmail("assistant"), "Writing on their behalf.", messageId: "<other@mail.example.org>");
        (await owner.EmailIssueAsync(sent.ApplicationId)).Should().Be("Bounced", "another address proves nothing about this one");

        await fixture.DevReplyAsync(sent.ReplyAddress, sent.Address.ToUpperInvariant(), "It works after all.", messageId: "<same@mail.example.org>");
        (await owner.EmailIssueAsync(sent.ApplicationId)).Should().Be("None");
    }

    [Fact]
    public async Task An_automatic_answer_from_the_flagged_address_leaves_the_flag_on()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);
        (await fixture.PostEventsAsync(ReplySupport.EventBody(owner.OrganizationId, sent.SendId, sent.Address, "spamreport")))
            .Should().Be(HttpStatusCode.OK);
        (await owner.EmailIssueAsync(sent.ApplicationId)).Should().Be("SpamComplaint");

        (await fixture.DevReplyAsync(sent.ReplyAddress, sent.Address, "I am out of the office.", messageId: "<away@mail.example.org>", autoReply: true))
            .Should().Be("Stored");

        (await owner.EmailIssueAsync(sent.ApplicationId)).Should().Be("SpamComplaint", "an out-of-office is not the candidate asking for email again");
    }

    [Fact]
    public async Task A_viewer_cannot_clear_an_email_flag_and_another_organization_cannot_find_the_candidate()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        using var outsider = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);

        (await viewer.PostAsync($"/v1/candidates/{sent.CandidateId}/email-issue/clear")).Expect(HttpStatusCode.Forbidden);
        (await outsider.PostAsync($"/v1/candidates/{sent.CandidateId}/email-issue/clear")).Expect(HttpStatusCode.NotFound);
    }
}
