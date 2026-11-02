using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests.Realtime;

/// <summary>
/// Saving, sharing, renaming and deleting a view reach the organization's open dashboards as ViewsChanged, carrying the
/// view's id only, so a teammate's sidebar refetches; another organization hears nothing (docs/architecture/saved-views.md).
/// </summary>
public sealed class ViewsRealtimeTests(AppHostFixture fixture)
{
    [Fact]
    public async Task A_view_change_reaches_the_organization_with_the_views_id_and_never_another_organization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await using var ourClient = await EventsConnection.ConnectAsync(ours.Session, cancellationToken);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs.Session, cancellationToken);

        var view = (await ours.PostAsync("/v1/views", new JsonObject
        {
            ["name"] = "Interviewing this week",
            ["scope"] = "Candidates",
            ["jobId"] = null,
            ["shared"] = true,
            ["filters"] = new JsonObject
            {
                ["baseView"] = "interviewing",
                ["jobIds"] = new JsonArray(),
                ["stages"] = new JsonArray(),
                ["authenticity"] = null,
                ["aiAssistance"] = null,
                ["tag"] = null,
                ["sortColumn"] = null,
                ["sortDescending"] = false,
            },
        })).Expect(HttpStatusCode.Created);

        var changed = await ourClient.WaitForAsync("ViewsChanged", "viewId", view.Id(), cancellationToken);
        changed.GetProperty("organizationId").GetGuid().Should().Be(ours.OrganizationId);
        changed.GetProperty("actorUserId").GetGuid().Should().Be(ours.UserId);
        changed.ToString().Should().NotContain("Interviewing this week", "the event carries ids, never names or filters");

        (await ours.DeleteAsync($"/v1/views/{view.Id()}")).Expect(HttpStatusCode.NoContent);

        // Our last event has arrived, so an event of ours sent to them would have too.
        theirClient.ReceivedAnythingAbout(view.Id()).Should().BeFalse("another organization never hears about our views");
    }
}
