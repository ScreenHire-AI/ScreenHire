using System.Text.Json.Nodes;
using ScreenHire.IntegrationTests.Automations;

namespace ScreenHire.IntegrationTests.Realtime;

/// <summary>
/// The workflow engine's tenant events through the hub: a NotifyTeam action and the finished run reach their own
/// organization's dashboards, with ids and the status only, and never another organization's.
/// </summary>
public sealed class AutomationRealtimeTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Automation_outcomes_reach_their_own_organization_and_never_another()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await using var ourClient = await EventsConnection.ConnectAsync(ours.Session, cancellationToken);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs.Session, cancellationToken);
        var job = await AutomationSamples.JobWithAutomationsAsync(ours, "Notify on arrival", new JsonObject
        {
            ["id"] = "notify",
            ["name"] = "Tell the team",
            ["trigger"] = new JsonObject { ["type"] = "ApplicationSubmitted" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "NotifyTeam", ["note"] = "A new application arrived." }),
        });

        var application = await ours.CreateApplicationAsync(job, ApiSession.NewEmail("candidate"));

        var notified = await ourClient.WaitForAsync("TeamNotified", "applicationId", application.Id(), cancellationToken);
        notified.GetProperty("jobId").GetGuid().Should().Be(job);
        var finished = await ourClient.WaitForAsync("AutomationRunFinished", "applicationId", application.Id(), cancellationToken);
        finished.GetProperty("automationId").GetString().Should().Be("notify");
        finished.GetProperty("status").GetString().Should().Be("Completed");
        finished.GetProperty("runId").GetGuid().Should().Be(notified.GetProperty("runId").GetGuid());
        notified.EnumerateObject().Select(p => p.Name).Should().NotContain("note", "the note stays behind the API");

        theirClient.ReceivedAnythingAbout(application.Id()).Should().BeFalse("another organization's automation events never reach us");
    }
}
