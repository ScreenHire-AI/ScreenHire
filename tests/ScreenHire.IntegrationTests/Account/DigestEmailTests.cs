using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Notifications;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.IntegrationTests.Notifications;
using ScreenHire.IntegrationTests.Scheduling;
using ScreenHire.Testing;
using Wolverine;

namespace ScreenHire.IntegrationTests.Account;

/// <summary>
/// The person's email choice decides what a notice does (docs/architecture/notifications.md, "Each person's choices" and "The
/// daily digest"): Instantly emails it, Off sends nothing, Daily digest keeps it for one email at 08:00 their time, sent once a
/// day and only with something in it; the bell keeps what is addressed to them either way. The digest's tick carries its
/// instant, so each test picks the hour.
/// </summary>
public sealed class DigestEmailTests(AppHostFixture fixture)
{
    private const string Choices = "/v1/account/notifications";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Tomorrow at <paramref name="hour"/>:<paramref name="minute"/> UTC: after anything a test creates today.</summary>
    private static DateTimeOffset Tomorrow(int hour, int minute = 0) =>
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(1).AddHours(hour).AddMinutes(minute);

    [Fact]
    public async Task A_digest_holds_the_mention_and_the_task_goes_once_a_day_and_never_when_empty()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var robinEmail = ApiSession.NewEmail("robin");
        using var robin = await fixture.InviteAndJoinAsync(owner, "Recruiter", robinEmail);
        // A member with nothing waiting: off the job's hiring team, so new applications do not reach them on My jobs.
        var quietEmail = ApiSession.NewEmail("quiet");
        using var quiet = await fixture.InviteAndJoinAsync(owner, "Viewer", quietEmail);
        await ChooseAsync(robin, "UTC", new() { ["Mentions"] = "Digest" });
        var application = await ApplicationAsync(owner, "Taylor Brook");

        (await owner.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = $"@[{robin.UserId}] ask about the move." }))
            .Expect(HttpStatusCode.Created);
        (await owner.PostAsync($"/v1/applications/{application}/tasks", new JsonObject { ["note"] = "Call the referee.", ["assigneeUserId"] = robin.UserId }))
            .Expect(HttpStatusCode.Created);

        // Both are in Robin's bell at once, and in no email yet.
        var bell = await robin.WaitForItemsAsync(2);
        bell.Body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("kind").GetString()).Should().BeEquivalentTo(["mention", "task-assigned"]);
        await WaitForDigestItemsAsync(owner.OrganizationId, robin.UserId, 2);
        (await fixture.Mailpit.MessagesAsync(robinEmail, Ct)).Should().NotContain(m => m.Subject.Contains("mentioned you", StringComparison.Ordinal));

        await ReleaseAsync(owner.OrganizationId, Tomorrow(8, 5));
        var digest = await WaitForDigestAsync(robinEmail, 1);
        digest.Text.Should().Contain("mentioned you in a note on Taylor Brook").And.Contain("gave you a task on Taylor Brook");
        digest.Text.Should().NotContain("ask about the move", "the digest never quotes the note").And.NotContain("referee");
        digest.Text.Should().Contain("Change what we email you").And.Contain("/account/notifications");

        // The same morning again: nothing more. The member with nothing waiting never gets one.
        await ReleaseAsync(owner.OrganizationId, Tomorrow(9, 5));
        await WaitHandledAsync(owner.OrganizationId, 2);
        (await DigestsAsync(robinEmail)).Should().HaveCount(1);
        (await DigestsAsync(quietEmail)).Should().BeEmpty();
    }

    [Fact]
    public async Task Something_that_arrives_after_eight_waits_for_the_next_mornings_digest()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var robinEmail = ApiSession.NewEmail("robin");
        using var robin = await fixture.InviteAndJoinAsync(owner, "Recruiter", robinEmail);
        await ChooseAsync(robin, "UTC", new() { ["Mentions"] = "Digest" });
        var application = await ApplicationAsync(owner, "Taylor Brook");
        (await owner.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = $"@[{robin.UserId}] a look?" }))
            .Expect(HttpStatusCode.Created);
        await WaitForDigestItemsAsync(owner.OrganizationId, robin.UserId, 1);

        // As if it came at 10:00 tomorrow: the 10:30 tick has nothing from before 08:00, so no "daily" email that afternoon.
        await using (var db = await fixture.ContextAsync(owner.OrganizationId, Ct))
        {
            await db.Database.ExecuteSqlAsync($"UPDATE digest_items SET created_at = {Tomorrow(10)} WHERE organization_id = {owner.OrganizationId}", Ct);
        }

        await ReleaseAsync(owner.OrganizationId, Tomorrow(10, 30));
        await WaitHandledAsync(owner.OrganizationId, 1);
        (await DigestsAsync(robinEmail)).Should().BeEmpty();

        await ReleaseAsync(owner.OrganizationId, Tomorrow(24 + 8, 5));
        (await WaitForDigestAsync(robinEmail, 1)).Text.Should().Contain("mentioned you");
    }

    [Fact]
    public async Task A_row_turned_off_after_its_items_waited_leaves_them_out_of_the_digest()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var robinEmail = ApiSession.NewEmail("robin");
        using var robin = await fixture.InviteAndJoinAsync(owner, "Recruiter", robinEmail);
        await ChooseAsync(robin, "UTC", new() { ["Mentions"] = "Digest" });
        var application = await ApplicationAsync(owner, "Taylor Brook");
        (await owner.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = $"@[{robin.UserId}] a look?" }))
            .Expect(HttpStatusCode.Created);
        (await owner.PostAsync($"/v1/applications/{application}/tasks", new JsonObject { ["note"] = "Call the referee.", ["assigneeUserId"] = robin.UserId }))
            .Expect(HttpStatusCode.Created);
        await WaitForDigestItemsAsync(owner.OrganizationId, robin.UserId, 2);

        // Before the morning, Robin wants no email about mentions: the digest reads the choice when it is sent.
        await ChooseAsync(robin, "UTC", new() { ["Mentions"] = "Off" });

        await ReleaseAsync(owner.OrganizationId, Tomorrow(8, 5));
        var digest = await WaitForDigestAsync(robinEmail, 1);
        digest.Text.Should().Contain("gave you a task on Taylor Brook").And.NotContain("mentioned you");
    }

    [Fact]
    public async Task Instantly_emails_a_mention_now_and_off_sends_nothing_while_the_bell_keeps_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var robinEmail = ApiSession.NewEmail("robin");
        var samEmail = ApiSession.NewEmail("sam");
        using var robin = await fixture.InviteAndJoinAsync(owner, "Recruiter", robinEmail);
        using var sam = await fixture.InviteAndJoinAsync(owner, "Recruiter", samEmail);
        await ChooseAsync(sam, "UTC", new() { ["Mentions"] = "Off" });
        var application = await ApplicationAsync(owner, "Taylor Brook");

        (await owner.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = $"@[{robin.UserId}] and @[{sam.UserId}], thoughts?" }))
            .Expect(HttpStatusCode.Created);

        MailpitMessage? toRobin = null;
        await WolverineStore.WaitUntilAsync(
            async () => (toRobin = (await fixture.Mailpit.MessagesAsync(robinEmail, Ct)).FirstOrDefault(m => m.Subject.Contains("mentioned you", StringComparison.Ordinal))) is not null,
            "Robin's mention email", Ct);
        toRobin!.Text.Should().Contain("Change what we email you");
        // Sam's notice was handled alongside Robin's: in the bell, never in the mailbox.
        await sam.WaitForItemsAsync(1);
        (await fixture.Mailpit.MessagesAsync(samEmail, Ct)).Should().NotContain(m => m.Subject.Contains("mentioned you", StringComparison.Ordinal));
    }

    [Fact]
    public async Task New_applications_follow_each_persons_scope()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var everythingEmail = ApiSession.NewEmail("everything");
        var myJobsEmail = ApiSession.NewEmail("myjobs");
        // Both see every job; neither is on its hiring team.
        using var everything = await fixture.InviteAndJoinAsync(owner, "Recruiter", everythingEmail);
        using var myJobs = await fixture.InviteAndJoinAsync(owner, "Recruiter", myJobsEmail);
        await ChooseAsync(everything, "UTC", new() { ["NewApplications"] = "Instantly" }, scope: "Everything");
        await ChooseAsync(myJobs, "UTC", new() { ["NewApplications"] = "Instantly" });

        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await owner.PublishJobAsync(job.Id());
        var application = (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"), "Taylor Brook")).Id();

        MailpitMessage? mail = null;
        await WolverineStore.WaitUntilAsync(
            async () => (mail = (await fixture.Mailpit.MessagesAsync(everythingEmail, Ct)).FirstOrDefault(m => m.Subject == "Taylor Brook applied to Platform Engineer")) is not null,
            "the new application email to the member whose scope is everything", Ct);
        mail!.Text.Should().Contain($"/applications/{application}");
        // One fan-out decided for both; it has run, so the member off the hiring team on My jobs got nothing.
        await fixture.WaitHandledAsync(application.ToString(), ".NotifyNewApplication", 1);
        (await fixture.Mailpit.MessagesAsync(myJobsEmail, Ct)).Should().NotContain(m => m.Subject.Contains("applied to", StringComparison.Ordinal));
    }

    private static async Task ChooseAsync(Tenant member, string timeZone, Dictionary<string, string> rows, string scope = "MyJobs")
    {
        var name = (await member.GetAsync("/v1/account")).Expect(HttpStatusCode.OK).Text("displayName");
        (await member.PutAsync("/v1/account/profile", new JsonObject
        {
            ["name"] = name.Contains('@', StringComparison.Ordinal) ? "Robin Park" : name,
            ["timeZone"] = timeZone,
            ["language"] = null,
            ["dateFormat"] = "Browser",
        })).Expect(HttpStatusCode.OK);
        var body = AccountNotificationsTests.Save((await member.GetAsync(Choices)).Expect(HttpStatusCode.OK), rows);
        body["scope"] = scope;
        (await member.PutAsync(Choices, body)).Expect(HttpStatusCode.OK);
    }

    private static async Task<Guid> ApplicationAsync(Tenant owner, string candidateName)
    {
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await owner.PublishJobAsync(job.Id());
        return (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"), candidateName)).Id();
    }

    private async Task ReleaseAsync(Guid organizationId, DateTimeOffset at)
    {
        await using var publisher = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, Ct);
        await publisher.Services.GetRequiredService<IMessageBus>().PublishAsync(new SendOrganizationDigests(organizationId, at));
    }

    private Task WaitHandledAsync(Guid organizationId, int count) =>
        fixture.WaitHandledAsync(organizationId.ToString(), ".SendOrganizationDigests", count);

    private async Task WaitForDigestItemsAsync(Guid organizationId, Guid userId, int count) =>
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                await using var db = await fixture.ContextAsync(organizationId, Ct);
                return await db.DigestItems.CountAsync(i => i.UserId == userId, Ct) >= count;
            },
            $"{count} items to wait for the digest", Ct);

    private async Task<IReadOnlyList<MailpitMessage>> DigestsAsync(string address) =>
        [.. (await fixture.Mailpit.MessagesAsync(address, Ct)).Where(m => m.Subject.StartsWith("Your ScreenHire digest", StringComparison.Ordinal))];

    private async Task<MailpitMessage> WaitForDigestAsync(string address, int count)
    {
        await WolverineStore.WaitUntilAsync(async () => (await DigestsAsync(address)).Count >= count, $"{count} digests to reach {address}", Ct);
        return (await DigestsAsync(address))[0];
    }
}
