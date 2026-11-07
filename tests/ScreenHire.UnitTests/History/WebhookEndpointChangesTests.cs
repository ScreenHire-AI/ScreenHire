using ScreenHire.Domain.History;
using ScreenHire.Domain.Webhooks;

namespace ScreenHire.UnitTests.History;

public sealed class WebhookEndpointChangesTests
{
    private const string Address = "https://hooks.example.com/path/tokenA1b2?key=tokenC3d4";

    private static readonly Guid Organization = Guid.CreateVersion7();

    private static WebhookEndpoint Endpoint(string url = Address, string? description = "Payroll sync", params string[] events) =>
        WebhookEndpoint.Create(Organization, url, description, events.Length == 0 ? [WebhookEventTypes.JobPublished] : events, DateTimeOffset.UnixEpoch);

    private static string[] Sentences(IReadOnlyList<ChangeLine> lines) => [.. lines.Select(l => l.Sentence())];

    [Fact]
    public void A_new_endpoint_is_named_by_its_host_and_lists_its_events_in_words()
    {
        var lines = WebhookEndpointChanges.Describe(null, Endpoint(events: [WebhookEventTypes.ApplicationMoved, WebhookEventTypes.JobClosed]));

        Sentences(lines).Should().Equal("created the endpoint hooks.example.com");
        lines[0].Parts.Should().Equal(new ChangeText("created the endpoint "), new ChangeText("hooks.example.com", Strong: true));
        lines[0].Details.Should().Equal(new ChangeDetail("Events", null, "Application moved, Job closed"), new ChangeDetail("Description", null, "Payroll sync"));
    }

    [Fact]
    public void An_address_is_never_written_beyond_its_host()
    {
        var moved = Endpoint("https://hooks.example.com/path/tokenE5f6?key=tokenG7h8");
        var elsewhere = Endpoint("https://other.example.org/path/tokenI9j0");
        var all = new[]
        {
            WebhookEndpointChanges.Describe(null, Endpoint()),
            WebhookEndpointChanges.Describe(Endpoint(), moved),
            WebhookEndpointChanges.Describe(Endpoint(), elsewhere),
        };

        Sentences(all[1]).Should().Equal("changed the address of hooks.example.com");
        Sentences(all[2]).Should().Equal("changed the address from hooks.example.com to other.example.org");
        var written = string.Concat(all.SelectMany(l => l).Select(l => l.Sentence() + string.Concat((l.Details ?? []).Select(d => d.Before + d.After))));
        written.Should().NotContain("token").And.NotContain("/path");
    }

    [Fact]
    public void Events_the_description_and_the_switch_each_read_as_one_line_and_bookkeeping_says_nothing()
    {
        var before = Endpoint(events: [WebhookEventTypes.JobPublished, WebhookEventTypes.JobClosed]);
        var after = Endpoint(description: null, events: [WebhookEventTypes.JobPublished, WebhookEventTypes.VerificationAssessed]);
        after.SetEnabled(false, DateTimeOffset.UnixEpoch.AddDays(1));

        var lines = WebhookEndpointChanges.Describe(before, after);

        Sentences(lines).Should().Equal(
            "started sending Verification assessed", "stopped sending Job closed", "removed the description", "turned off the endpoint hooks.example.com");
        lines[2].Details.Should().Equal(new ChangeDetail("Description", "Payroll sync", null));

        var failed = Endpoint();
        failed.ConsecutiveFailures = 4;
        failed.UpdatedAt = DateTimeOffset.UnixEpoch.AddDays(2);
        WebhookEndpointChanges.Describe(Endpoint(), failed).Should().BeEmpty();
        WebhookEndpointChanges.Describe(Endpoint(), null).Should().BeEmpty("a deleted endpoint has no history to show");
    }

    [Fact]
    public void Only_a_later_rotation_time_is_a_rotation()
    {
        var was = new SigningRotation(DateTimeOffset.UnixEpoch);

        Sentences(WebhookEndpointChanges.Rotation(was, new SigningRotation(DateTimeOffset.UnixEpoch.AddHours(1)))).Should().Equal("rotated the signing secret");
        WebhookEndpointChanges.Rotation(was, was).Should().BeEmpty();
        WebhookEndpointChanges.Rotation(null, was).Should().BeEmpty("the secret arrives with the endpoint, whose line says it was created");
    }

    [Theory]
    [InlineData("application.submitted", "Application submitted")]
    [InlineData("verification.assessed", "Verification assessed")]
    [InlineData("job.closed", "Job closed")]
    public void An_event_reads_the_way_the_dashboard_says_it(string type, string words)
    {
        WebhookEndpointChanges.EventLabel(type).Should().Be(words);
    }

    [Fact]
    public void Every_event_a_receiver_can_choose_has_a_label_that_reads_as_words()
    {
        WebhookEventTypes.All.Select(WebhookEndpointChanges.EventLabel).Should().OnlyContain(label => !label.Contains('.') && char.IsUpper(label[0]));
    }
}
