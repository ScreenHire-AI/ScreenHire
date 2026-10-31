using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ScreenHire.Application.Slack;

/// <summary>
/// Slack settings, <c>ScreenHire:Slack</c> (docs/architecture/slack.md). The AppHost gives the API and the worker the
/// same values, so both agree on <see cref="Mode"/>. Nothing here is on until configured: with no Slack app (client id
/// and secret) and no sandbox, Slack is hidden.
/// </summary>
public sealed class SlackOptions
{
    public const string Section = "ScreenHire:Slack";

    /// <summary>The Slack app's client id. Not a secret.</summary>
    public string? ClientId { get; set; }

    /// <summary>The Slack app's client secret, from the <c>slack-client-secret</c> parameter. Never committed.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>The local sandbox workspace (<see cref="SandboxSlackClient"/>): Development and tests only.</summary>
    public SlackSandboxOptions Sandbox { get; set; } = new();

    /// <summary>Slack's Web API. Configurable only so a test can point the real client at a stand-in.</summary>
    public Uri ApiBaseUrl { get; set; } = new("https://slack.com/api/");

    /// <summary>Where Slack's authorize pages live.</summary>
    public Uri AuthorizeBaseUrl { get; set; } = new("https://slack.com/");

    /// <summary>How long one call to Slack may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long the record that a message went is kept.</summary>
    public TimeSpan MessageRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Real Slack when the app's client id and secret are both set; the sandbox when it is enabled; otherwise off. The
    /// one rule both hosts apply.
    /// </summary>
    public SlackMode Mode =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) ? SlackMode.Slack
        : Sandbox.Enabled ? SlackMode.Sandbox
        : SlackMode.Off;
}

public sealed class SlackSandboxOptions
{
    public bool Enabled { get; set; }
}

public enum SlackMode
{
    /// <summary>No Slack app here: the page says so and nothing is posted.</summary>
    Off,

    /// <summary>The local sandbox workspace, with no third party.</summary>
    Sandbox,

    /// <summary>Slack's Web API with the configured app.</summary>
    Slack,
}

/// <summary>
/// Refuses to start a host with the sandbox outside Development: its workspace is made up, and a real organization
/// must never think its messages went to Slack.
/// </summary>
public sealed class SlackSandboxGate(IHostEnvironment environment) : IValidateOptions<SlackOptions>
{
    public ValidateOptionsResult Validate(string? name, SlackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Mode == SlackMode.Sandbox && !environment.IsDevelopment()
            ? ValidateOptionsResult.Fail("ScreenHire:Slack:Sandbox:Enabled is true outside Development. The sandbox workspace is for local runs and tests only.")
            : ValidateOptionsResult.Success;
    }
}
