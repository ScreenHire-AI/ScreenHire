using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Applications;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Endpoints;
using ScreenHire.Domain.Applications;

namespace ScreenHire.UnitTests.Applications;

public sealed class TurnstileVerifierTests
{
    private static readonly TurnstileOptions Configured = new() { SiteKey = "site-key", SecretKey = "secret-key" };

    [Fact]
    public async Task Unconfigured_it_checks_nothing_and_calls_nobody()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"success":true}""");
        var verifier = Verifier(new TurnstileOptions(), handler);

        var result = await verifier.VerifyAsync("token", "203.0.113.7", TestContext.Current.CancellationToken);

        result.Should().Be(BotCheckResult.NotChecked);
        verifier.SiteKey.Should().BeNull();
        handler.Requests.Should().Be(0);
    }

    [Theory]
    [InlineData("""{"success":true}""", BotCheckResult.Passed)]
    [InlineData("""{"success":false,"error-codes":["invalid-input-response"]}""", BotCheckResult.Failed)]
    public async Task Configured_it_records_cloudflares_answer(string body, BotCheckResult expected)
    {
        var handler = new StubHandler(HttpStatusCode.OK, body);

        var result = await Verifier(Configured, handler).VerifyAsync("token", "203.0.113.7", TestContext.Current.CancellationToken);

        result.Should().Be(expected);
        handler.LastForm.Should().Contain("secret=secret-key").And.Contain("response=token").And.Contain("remoteip=203.0.113.7");
    }

    [Fact]
    public async Task A_missing_token_fails_without_a_call()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"success":true}""");

        var result = await Verifier(Configured, handler).VerifyAsync(" ", null, TestContext.Current.CancellationToken);

        result.Should().Be(BotCheckResult.Failed);
        handler.Requests.Should().Be(0);
    }

    [Fact]
    public async Task Cloudflare_being_unavailable_is_not_held_against_the_candidate()
    {
        var result = await Verifier(Configured, new StubHandler(HttpStatusCode.ServiceUnavailable, ""))
            .VerifyAsync("token", null, TestContext.Current.CancellationToken);

        result.Should().Be(BotCheckResult.NotChecked);
    }

    [Fact]
    public async Task A_resilience_timeout_or_open_circuit_is_not_checked_rather_than_an_error()
    {
        // The standard resilience handler throws its own exception types, not HttpRequestException.
        var result = await Verifier(Configured, new ThrowingHandler(new TimeoutException("attempt timed out")))
            .VerifyAsync("token", null, TestContext.Current.CancellationToken);

        result.Should().Be(BotCheckResult.NotChecked);
    }

    // The first link of a saved draft is emailed to an address the visitor typed, so only a failed check refuses it
    // (PublicApplicationDraftEndpoints.BotCheckProblemAsync). A check that did not run lets it through.
    [Fact]
    public async Task A_failed_check_refuses_the_draft_link_with_a_way_to_send_the_application_instead()
    {
        var verifier = Verifier(Configured, new StubHandler(HttpStatusCode.OK, """{"success":false}"""));

        var refused = await PublicApplicationDraftEndpoints.BotCheckProblemAsync(verifier, "token", "203.0.113.7", TestContext.Current.CancellationToken);

        refused.Should().NotBeNull();
        refused!.StatusCode.Should().Be((int)HttpStatusCode.Forbidden);
        refused.ProblemDetails.Detail.Should().Contain("send your application now");
    }

    [Fact]
    public async Task A_missing_token_refuses_the_draft_link_when_the_check_is_configured_and_calls_nobody()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"success":true}""");

        var refused = await PublicApplicationDraftEndpoints.BotCheckProblemAsync(Verifier(Configured, handler), null, null, TestContext.Current.CancellationToken);

        refused.Should().NotBeNull();
        handler.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_passed_check_unconfigured_or_unreachable_lets_the_draft_link_go()
    {
        var ct = TestContext.Current.CancellationToken;

        (await PublicApplicationDraftEndpoints.BotCheckProblemAsync(
            Verifier(Configured, new StubHandler(HttpStatusCode.OK, """{"success":true}""")), "token", null, ct)).Should().BeNull("Cloudflare said yes");
        (await PublicApplicationDraftEndpoints.BotCheckProblemAsync(
            Verifier(new TurnstileOptions(), new StubHandler(HttpStatusCode.OK, "")), null, null, ct)).Should().BeNull("local and test stacks have no keys");
        (await PublicApplicationDraftEndpoints.BotCheckProblemAsync(
            Verifier(Configured, new StubHandler(HttpStatusCode.ServiceUnavailable, "")), "token", null, ct)).Should().BeNull("an outage says nothing about the visitor");
    }

    private static TurnstileVerifier Verifier(TurnstileOptions options, HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(options), NullLogger<TurnstileVerifier>.Instance);

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        public string LastForm { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastForm = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
