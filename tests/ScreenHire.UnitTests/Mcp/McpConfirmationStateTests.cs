using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using ScreenHire.Api.Mcp;

namespace ScreenHire.UnitTests.Mcp;

/// <summary>
/// The state a confirmation round trip echoes back (docs/architecture/mcp-server.md, "Confirmation"): it holds only for
/// the tool, the arguments, what the tool resolved from them, the key and the ten minutes it was issued for, so a yes
/// to one email never sends another. The
/// integration tests (<c>McpConfirmationTests</c>) drive the round trip itself, where the SDK hides the real state from
/// the client; these pin what the state binds. Also here: the server registers every tool the catalogue lists.
/// </summary>
public sealed class McpConfirmationStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_state_holds_for_the_same_tool_arguments_and_key_within_ten_minutes()
    {
        var (confirmation, clock) = Create();
        var digest = McpConfirmation.Digest("send_email", Arguments("""{"applicationId":"a","templateId":"t","confirm":false}"""));
        var state = confirmation.Protect("send_email", digest, "key-1");

        clock.Now = Now.AddMinutes(9);

        confirmation.Matches(state, "send_email", digest, "key-1").Should().BeTrue();
    }

    [Fact]
    public void Confirm_itself_and_the_order_of_the_arguments_do_not_change_what_was_agreed()
    {
        var first = McpConfirmation.Digest("send_email", Arguments("""{"applicationId":"a","templateId":"t","confirm":false}"""));
        var retry = McpConfirmation.Digest("send_email", Arguments("""{"confirm":true,"templateId":"t","applicationId":"a"}"""));

        retry.Should().Be(first);
    }

    [Fact]
    public void Other_arguments_another_tool_another_key_an_expired_or_a_forged_state_are_refused()
    {
        var (confirmation, clock) = Create();
        var digest = McpConfirmation.Digest("send_email", Arguments("""{"applicationId":"a","templateId":"t"}"""));
        var other = McpConfirmation.Digest("send_email", Arguments("""{"applicationId":"b","templateId":"t"}"""));
        var state = confirmation.Protect("send_email", digest, "key-1");

        other.Should().NotBe(digest);
        confirmation.Matches(state, "send_email", other, "key-1").Should().BeFalse("a yes to one email never sends another");
        confirmation.Matches(state, "close_job", digest, "key-1").Should().BeFalse();
        confirmation.Matches(state, "send_email", digest, "key-2").Should().BeFalse();
        confirmation.Matches("not a state the server wrote", "send_email", digest, "key-1").Should().BeFalse();
        clock.Now = Now.Add(McpConfirmation.Lifetime).AddSeconds(1);
        confirmation.Matches(state, "send_email", digest, "key-1").Should().BeFalse("the person has ten minutes to answer");
    }

    [Fact]
    public void A_change_to_what_the_tool_resolved_refuses_the_yes()
    {
        var arguments = Arguments("""{"applicationId":"a","timeZone":"Europe/London"}""");
        var asked = McpConfirmation.Digest("invite_to_schedule", arguments, "TheirTimes|30|14|p1");

        McpConfirmation.Digest("invite_to_schedule", arguments, "TheirTimes|30|14|p1").Should().Be(asked);
        McpConfirmation.Digest("invite_to_schedule", arguments, "TheirTimes|60|14|p1").Should()
            .NotBe(asked, "the stage's length changed after the person said yes to 30 minutes");
        McpConfirmation.Digest("invite_to_schedule", arguments).Should().NotBe(asked);
    }

    [Fact]
    public void The_server_registers_every_tool_the_catalogue_lists()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddScreenHireMcp();
        using var services = builder.Services.BuildServiceProvider();

        // A list passed where the SDK takes IEnumerable<Type> once bound to WithTools<T> and registered nothing.
        services.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).Should()
            .BeEquivalentTo(McpToolCatalog.Describe().Tools.Select(t => t.Name));
    }

    private static (McpConfirmation Confirmation, Clock Clock) Create()
    {
        var clock = new Clock { Now = Now };
        return (new McpConfirmation(new EphemeralDataProtectionProvider(), clock), clock);
    }

    private static Dictionary<string, JsonElement> Arguments(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
