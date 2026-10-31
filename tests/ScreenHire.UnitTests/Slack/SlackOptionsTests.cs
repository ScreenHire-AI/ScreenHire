using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ScreenHire.Application.Slack;

namespace ScreenHire.UnitTests.Slack;

/// <summary>
/// Which Slack a host talks to: the real app once its client id and secret are both set, the sandbox when it is switched
/// on, otherwise none (the page hides Slack). The sandbox's workspace is made up, so a host outside Development refuses
/// to start with it.
/// </summary>
public sealed class SlackOptionsTests
{
    [Theory]
    [InlineData("client", "secret", false, SlackMode.Slack)]
    [InlineData("client", "secret", true, SlackMode.Slack)]
    [InlineData("client", null, true, SlackMode.Sandbox)]
    [InlineData("client", "  ", true, SlackMode.Sandbox)]
    [InlineData(null, null, true, SlackMode.Sandbox)]
    [InlineData("client", null, false, SlackMode.Off)]
    [InlineData(null, "secret", false, SlackMode.Off)]
    [InlineData(null, null, false, SlackMode.Off)]
    public void The_mode_follows_the_app_and_the_sandbox_switch(string? clientId, string? clientSecret, bool sandbox, SlackMode expected)
    {
        var options = new SlackOptions { ClientId = clientId, ClientSecret = clientSecret, Sandbox = new SlackSandboxOptions { Enabled = sandbox } };

        options.Mode.Should().Be(expected);
    }

    [Fact]
    public void The_sandbox_is_refused_outside_development()
    {
        var result = new SlackSandboxGate(new Host(Environments.Production))
            .Validate(null, new SlackOptions { Sandbox = new SlackSandboxOptions { Enabled = true } });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Sandbox:Enabled").And.Contain("Development");
    }

    [Fact]
    public void The_sandbox_is_allowed_in_development()
    {
        new SlackSandboxGate(new Host(Environments.Development))
            .Validate(null, new SlackOptions { Sandbox = new SlackSandboxOptions { Enabled = true } })
            .Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void Real_slack_and_no_slack_start_in_any_environment(string environment)
    {
        var gate = new SlackSandboxGate(new Host(environment));

        gate.Validate(null, new SlackOptions { ClientId = "client", ClientSecret = "secret" }).Succeeded.Should().BeTrue();
        gate.Validate(null, new SlackOptions()).Succeeded.Should().BeTrue();
    }

    private sealed class Host(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = "";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
