using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Realtime;
using ScreenHire.Domain.Jobs;
using ScreenHire.IntegrationTests.Messaging;
using Wolverine;

namespace ScreenHire.IntegrationTests.Realtime;

/// <summary>
/// The real-time hub against the running API and worker, with the .NET SignalR client signed in by a session cookie the
/// way the dashboard is: tenant events reach their own organization's clients and nobody else's, whichever host raised
/// them, and the hub refuses anyone not signed in to an organization.
/// </summary>
public sealed class RealtimeHubTests(AppHostFixture fixture)
{
    [Fact]
    public async Task A_signed_out_connection_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);

        var connect = () => EventsConnection.ConnectAsync(session, cancellationToken);

        (await connect.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_WebSocket_from_another_origin_is_refused_and_one_from_the_app_origin_connects()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, _, _) = await SignedInWithDraftsAsync(cancellationToken);
        using var _ = session;

        // A same-site page, such as a careers page under the dashboard's apex, would send the Lax session cookie too.
        var fromElsewhere = () => EventsConnection.ConnectAsync(session, "http://careers.example.com", cancellationToken);
        (await fromElsewhere.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("403");

        // Without the dashboard, the API is the app origin (AppHost, "ScreenHire:Frontends" None).
        await using var fromTheApp = await EventsConnection.ConnectAsync(
            session, session.BaseAddress.GetLeftPart(UriPartial.Authority), cancellationToken);
        fromTheApp.Hub.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task A_user_without_an_organization_is_disconnected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail("no-org"), cancellationToken);

        // The handshake succeeds, then the hub refuses the connection before it joins any group.
        await using var connection = await TryConnectAsync(session, cancellationToken);
        if (connection is not null)
        {
            await connection.Closed.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            connection.Hub.State.Should().Be(HubConnectionState.Disconnected);
        }
    }

    [Fact]
    public async Task Each_organization_receives_its_own_job_events_and_never_another_organizations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (ours, ourJob, ourSecondJob) = await SignedInWithDraftsAsync(cancellationToken);
        var (theirs, theirJob, _) = await SignedInWithDraftsAsync(cancellationToken);
        using var _ = ours;
        using var __ = theirs;
        await using var ourClient = await EventsConnection.ConnectAsync(ours, cancellationToken);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs, cancellationToken);

        await PostAsync(ours, $"/v1/jobs/{ourJob}/publish", cancellationToken);
        var published = await ourClient.WaitForAsync("JobPublished", "jobId", ourJob, cancellationToken);
        published.GetProperty("organizationId").GetGuid().Should().NotBeEmpty();

        await PostAsync(theirs, $"/v1/jobs/{theirJob}/publish", cancellationToken);
        await theirClient.WaitForAsync("JobPublished", "jobId", theirJob, cancellationToken);

        // A later event of our own: once it arrives, theirs would have too, had it been sent to us.
        await PostAsync(ours, $"/v1/jobs/{ourSecondJob}/close", cancellationToken);
        await ourClient.WaitForAsync("JobClosed", "jobId", ourSecondJob, cancellationToken);

        ourClient.ReceivedAnythingAbout(theirJob).Should().BeFalse("another organization's events never reach us");
        theirClient.ReceivedAnythingAbout(ourJob).Should().BeFalse();
        theirClient.ReceivedAnythingAbout(ourSecondJob).Should().BeFalse();
    }

    [Fact]
    public async Task An_event_raised_by_the_worker_reaches_its_organizations_clients_only()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (ours, _, _) = await SignedInWithDraftsAsync(cancellationToken);
        var (theirs, _, _) = await SignedInWithDraftsAsync(cancellationToken);
        using var _ = ours;
        using var __ = theirs;
        var ourOrganization = await OrganizationIdAsync(ours, cancellationToken);
        await using var ourClient = await EventsConnection.ConnectAsync(ours, cancellationToken);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs, cancellationToken);
        await using var sender = await MessagingHost.StartPublisherToAppHostQueuesAsync(fixture, cancellationToken);
        var checkId = Guid.CreateVersion7();

        // The worker handles the check and raises LiveUpdatesChecked itself; the API only pushes it.
        await sender.Services.GetRequiredService<IMessageBus>().PublishAsync(new CheckLiveUpdates(ourOrganization, checkId));

        var checkedEvent = await ourClient.WaitForAsync("LiveUpdatesChecked", "checkId", checkId, cancellationToken);
        checkedEvent.GetProperty("organizationId").GetGuid().Should().Be(ourOrganization);
        theirClient.ReceivedAnythingAbout(checkId).Should().BeFalse();
    }

    [Fact]
    public async Task A_client_may_subscribe_to_its_own_job_but_not_to_another_organizations_or_an_unknown_topic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (ours, ourJob, _) = await SignedInWithDraftsAsync(cancellationToken);
        var (theirs, theirJob, _) = await SignedInWithDraftsAsync(cancellationToken);
        using var _ = ours;
        using var __ = theirs;
        await using var client = await EventsConnection.ConnectAsync(ours, cancellationToken);

        await client.Hub.InvokeAsync("Subscribe", "job", ourJob, cancellationToken);
        await client.Hub.InvokeAsync("Unsubscribe", "job", ourJob, cancellationToken);

        var theirsSubscription = () => client.Hub.InvokeAsync("Subscribe", "job", theirJob, cancellationToken);
        (await theirsSubscription.Should().ThrowAsync<HubException>()).WithMessage($"*no job {theirJob}*", "another tenant's job looks exactly like a missing one");
        var missing = Guid.CreateVersion7();
        var missingSubscription = () => client.Hub.InvokeAsync("Subscribe", "job", missing, cancellationToken);
        (await missingSubscription.Should().ThrowAsync<HubException>()).WithMessage($"*no job {missing}*");
        var unknownTopic = () => client.Hub.InvokeAsync("Subscribe", "payroll", ourJob, cancellationToken);
        await unknownTopic.Should().ThrowAsync<HubException>();
        client.Hub.State.Should().Be(HubConnectionState.Connected, "a refused subscription leaves the connection up");
    }

    // A new member of a new organization, and two draft jobs in it.
    private async Task<(ApiSession Session, Guid FirstJob, Guid SecondJob)> SignedInWithDraftsAsync(CancellationToken cancellationToken)
    {
        var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail("realtime"), cancellationToken);
        var organizationId = await session.OnboardAsync(cancellationToken);
        Job[] drafts = [TestData.NewJob(organizationId, "first"), TestData.NewJob(organizationId, "second")];
        await fixture.AddJobsAsync(organizationId, drafts, cancellationToken);
        return (session, drafts[0].Id, drafts[1].Id);
    }

    private static async Task<Guid> OrganizationIdAsync(ApiSession session, CancellationToken cancellationToken) =>
        (await session.CurrentUserAsync(cancellationToken))!.Value.GetProperty("organization").GetProperty("id").GetGuid();

    private static async Task PostAsync(ApiSession session, string path, CancellationToken cancellationToken)
    {
        using var response = await session.PostAsync(path, new { }, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<EventsConnection?> TryConnectAsync(ApiSession session, CancellationToken cancellationToken)
    {
        try
        {
            return await EventsConnection.ConnectAsync(session, cancellationToken);
        }
        catch (Exception exception) when (exception is HubException or InvalidOperationException or IOException)
        {
            return null; // refused during the handshake: also a refusal
        }
    }
}
