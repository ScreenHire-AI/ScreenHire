using System.Text.Json.Nodes;
using ScreenHire.IntegrationTests.Automations;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.IntegrationTests.Realtime;
using ScreenHire.Application.Applications;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// A candidate answers an email ScreenHire sent them (docs/architecture/email.md, "Replies"), delivered through the sandbox's
/// dev route so no SendGrid is needed. The email carries a reply address; the answer lands in Messages and Activity; the
/// person who sent the original is told once, in fixed wording that never quotes the reply. Each test uses its own organization.
/// </summary>
public sealed class CandidateRepliesTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_persons_email_carries_a_reply_address_and_the_candidates_answer_lands_in_messages_and_activity()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner, "Jordan Reyes", "Next steps");

        sent.Mail.ReplyTo.Should().NotBeNull("every email to a candidate carries a reply address");
        sent.ReplyAddress.Should().MatchRegex("^reply\\+[a-z2-7]{58}@reply\\.localhost$");

        await using var events = await EventsConnection.ConnectAsync(owner.Session, Ct);
        var outcome = await fixture.DevReplyAsync(
            sent.ReplyAddress, sent.Address, "Yes, Thursday works for me. Zebra-quartz-77.", messageId: "<reply-1@mail.example.org>",
            html: "<p>Yes, <b>Thursday</b> works.</p><script>alert(1)</script>");
        outcome.Should().Be("Stored");

        var received = (await owner.MessagesAsync(sent.CandidateId)).Received().Should().ContainSingle().Subject;
        received.GetProperty("fromAddress").GetString().Should().Be(sent.Address);
        received.GetProperty("subject").GetString().Should().Be("Re: Next steps");
        received.GetProperty("attachmentCount").GetInt32().Should().Be(0);
        received.GetProperty("kept").GetBoolean().Should().BeTrue();
        received.GetProperty("applicationId").GetGuid().Should().Be(sent.ApplicationId);

        var replyId = received.GetProperty("id").GetGuid();
        var reply = (await owner.GetAsync($"/v1/replies/{replyId}")).Expect(HttpStatusCode.OK);
        reply.Text("text").Should().Be("Yes, Thursday works for me. Zebra-quartz-77.");
        reply.Text("fromAddress").Should().Be(sent.Address);
        reply.Text("fromName").Should().Be("Jordan Reyes");
        reply.At("autoReply").GetBoolean().Should().BeFalse();
        reply.Text("html").Should().Contain("<strong>Thursday</strong>", "the editor's elements only").And.NotContain("<script").And.NotContain("alert");
        reply.Count("attachments").Should().Be(0);

        var timeline = (await owner.GetAsync($"/v1/applications/{sent.ApplicationId}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray();
        timeline.Should().ContainSingle(e => e.GetProperty("type").GetString() == "EmailReceived");

        await events.WaitForAsync("CandidateReplied", "applicationId", sent.ApplicationId, Ct);
    }

    [Fact]
    public async Task The_person_who_sent_the_original_is_emailed_once_in_fixed_wording_that_never_quotes_the_reply()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var me = await owner.EmailOfAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner, "Jordan Reyes", "Next steps");

        var marker = $"quartz-{Guid.NewGuid():N}";
        await fixture.DevReplyAsync(sent.ReplyAddress, sent.Address, $"Private details {marker}.", messageId: "<once@mail.example.org>");

        var notification = await fixture.WaitForSubjectAsync(me, "Jordan Reyes replied");
        notification.Text.Should().Contain("Jordan Reyes replied to your email about Role ")
            .And.Contain($"/applications/{sent.ApplicationId}?tab=messages")
            .And.NotContain(marker, "the reply's words stay in ScreenHire");
        notification.Html.Should().NotContain(marker);

        // The same message posted again is a duplicate: nothing more is stored and nobody is told twice.
        (await fixture.DevReplyAsync(sent.ReplyAddress, sent.Address, $"Private details {marker}.", messageId: "<once@mail.example.org>"))
            .Should().Be("Duplicate");
        var replyId = (await owner.MessagesAsync(sent.CandidateId)).Received().Single().GetProperty("id").GetGuid();
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(replyId.ToString(), Ct, nameof(CandidateReplied)) >= 1, "the worker to handle the reply", Ct);
        (await fixture.Mailpit.MessagesAsync(me, Ct)).Count(m => m.Subject == "Jordan Reyes replied").Should().Be(1);
        (await owner.MessagesAsync(sent.CandidateId)).Received().Should().ContainSingle();
    }

    [Fact]
    public async Task An_automatic_answer_is_kept_but_tells_nobody_by_email()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var me = await owner.EmailOfAsync();
        var auto = await fixture.SendToNewCandidateAsync(owner, "Ari Lane", "Hello Ari");
        var human = await fixture.SendToNewCandidateAsync(owner, "Robin Ellis", "Hello Robin");

        await fixture.DevReplyAsync(auto.ReplyAddress, auto.Address, "I am out of the office.", autoReply: true, subject: "Automatic reply: Hello Ari");
        var autoReply = (await owner.MessagesAsync(auto.CandidateId)).Received().Should().ContainSingle().Subject;
        (await owner.GetAsync($"/v1/replies/{autoReply.GetProperty("id").GetGuid()}")).Expect(HttpStatusCode.OK).At("autoReply").GetBoolean().Should().BeTrue();

        // A person's reply is the control: once its notification has arrived the worker has handled the automatic one too.
        await fixture.DevReplyAsync(human.ReplyAddress, human.Address, "Happy to talk.");
        await fixture.WaitForSubjectAsync(me, "Robin Ellis replied");
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(autoReply.GetProperty("id").GetGuid().ToString(), Ct, nameof(CandidateReplied)) >= 1,
            "the worker to handle the automatic answer",
            Ct);

        (await fixture.Mailpit.MessagesAsync(me, Ct)).Select(m => m.Subject).Should().NotContain("Ari Lane replied");
    }

    [Fact]
    public async Task A_reply_to_an_email_an_automation_sent_notifies_no_one_by_email()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var me = await owner.EmailOfAsync();
        var job = await AutomationSamples.JobWithAutomationsAsync(owner, "Automated welcome", AutomationSamples.EmailOnStage("screening", "stage-change"));
        var address = ApiSession.NewEmail("automated");
        var application = (await owner.CreateApplicationAsync(job, address, "Ari Lane")).Id();
        var candidate = (await owner.GetAsync($"/v1/applications/{application}")).Expect(HttpStatusCode.OK).Id("candidate.id");
        (await owner.PostAsync($"/v1/applications/{application}/move", new JsonObject { ["toStageId"] = "screening" })).Expect(HttpStatusCode.OK);

        var mail = (await fixture.Mailpit.WaitForMessagesAsync(address, 1, Ct))[0];
        await WolverineStore.WaitUntilAsync(
            async () => (await owner.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
                .Any(e => e.GetProperty("type").GetString() == "EmailSent"),
            "the automation's email on the timeline",
            Ct);
        (await fixture.DevReplyAsync(mail.ReplyTo!.Address, address, "Thanks for the welcome.")).Should().Be("Stored");

        var human = await fixture.SendToNewCandidateAsync(owner, "Robin Ellis", "Hello Robin");
        await fixture.DevReplyAsync(human.ReplyAddress, human.Address, "Happy to talk.");
        await fixture.WaitForSubjectAsync(me, "Robin Ellis replied");
        var replyId = (await owner.MessagesAsync(candidate)).Received().Single().GetProperty("id").GetGuid();
        var store = await WolverineStore.ForAppHostAsync(fixture, Ct);
        await WolverineStore.WaitUntilAsync(
            async () => await store.CountHandledAsync(replyId.ToString(), Ct, nameof(CandidateReplied)) >= 1, "the worker to handle the reply", Ct);

        (await fixture.Mailpit.MessagesAsync(me, Ct)).Select(m => m.Subject).Should().NotContain("Ari Lane replied", "no person sent the original");
    }

    [Fact]
    public async Task A_reply_with_a_tampered_forged_or_foreign_address_is_ignored_and_stores_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);
        var at = sent.ReplyAddress.IndexOf('+', StringComparison.Ordinal) + 11;
        var tampered = sent.ReplyAddress[..at] + (sent.ReplyAddress[at] == 'a' ? 'b' : 'a') + sent.ReplyAddress[(at + 1)..];

        (await fixture.DevReplyAsync(tampered, sent.Address, "Forged.")).Should().Be("Ignored");
        (await fixture.DevReplyAsync($"reply+{new string('a', 58)}@reply.localhost", sent.Address, "Forged.")).Should().Be("Ignored");
        (await fixture.DevReplyAsync("jordan@reply.localhost", sent.Address, "Not a reply address.")).Should().Be("Ignored");

        (await owner.MessagesAsync(sent.CandidateId)).Received().Should().BeEmpty();
    }

    [Fact]
    public async Task A_reply_is_visible_only_to_its_own_organization()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(ours);
        await fixture.DevReplyAsync(sent.ReplyAddress, sent.Address, "For our eyes only.", attachments: [("cv.pdf", ReplySupport.Pdf())]);
        var reply = (await ours.MessagesAsync(sent.CandidateId)).Received().Single().GetProperty("id").GetGuid();
        var attachment = (await ours.GetAsync($"/v1/replies/{reply}")).Expect(HttpStatusCode.OK).At("attachments.0.id").GetGuid();

        (await theirs.GetAsync($"/v1/replies/{reply}")).Expect(HttpStatusCode.NotFound);
        (await theirs.GetAsync($"/v1/replies/{reply}/attachments/{attachment}")).Expect(HttpStatusCode.NotFound);
        (await theirs.GetAsync($"/v1/candidates/{sent.CandidateId}/emails")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_reply_attachment_is_downloadable_once_scanned_and_a_dropped_one_is_noted_in_the_text()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var sent = await fixture.SendToNewCandidateAsync(owner);

        await fixture.DevReplyAsync(
            sent.ReplyAddress, sent.Address, "My CV is attached.",
            attachments: [("cv.pdf", ReplySupport.Pdf()), ("setup.exe", [0x4D, 0x5A, 0x90, 0x00])]);

        var replyId = (await owner.MessagesAsync(sent.CandidateId)).Received().Single().GetProperty("id").GetGuid();
        var reply = (await owner.GetAsync($"/v1/replies/{replyId}")).Expect(HttpStatusCode.OK);
        reply.Count("attachments").Should().Be(1, "the executable is not kept");
        reply.Text("attachments.0.fileName").Should().Be("cv.pdf");
        reply.Text("attachments.0.contentType").Should().Be("application/pdf");
        reply.Text("text").Should().Contain("My CV is attached.").And.Contain("1 attachment was not kept");
        (await owner.MessagesAsync(sent.CandidateId)).Received().Single().GetProperty("attachmentCount").GetInt32().Should().Be(1);

        var attachmentId = reply.At("attachments.0.id").GetGuid();
        await WolverineStore.WaitUntilAsync(
            async () => (await owner.GetAsync($"/v1/replies/{replyId}")).Expect(HttpStatusCode.OK).Text("attachments.0.scanStatus") != "Pending",
            "the attachment's malware scan",
            Ct);
        (await owner.GetAsync($"/v1/replies/{replyId}")).Text("attachments.0.scanStatus").Should().BeOneOf("Clean", "NotScanned");
        using var download = await owner.Session.Client.GetAsync($"/v1/replies/{replyId}/attachments/{attachmentId}", Ct);
        download.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await owner.GetAsync($"/v1/replies/{replyId}/attachments/{Guid.NewGuid()}")).Expect(HttpStatusCode.NotFound);
    }
}
