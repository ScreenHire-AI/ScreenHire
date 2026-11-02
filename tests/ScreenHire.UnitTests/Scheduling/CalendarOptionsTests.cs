using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ScreenHire.Application.Scheduling;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.UnitTests.Scheduling;

/// <summary>
/// Which calendars a member can connect: a provider only once both its client id and its secret are set (a bare id
/// cannot work), the sandbox when it is switched on. The sandbox's accounts are made up, so a host outside Development
/// refuses to start with it.
/// </summary>
public sealed class CalendarOptionsTests
{
    [Theory]
    [InlineData("client", "secret", true)]
    [InlineData("client", null, false)]
    [InlineData("client", "  ", false)]
    [InlineData(null, "secret", false)]
    [InlineData(null, null, false)]
    public void A_provider_is_available_only_with_both_its_client_id_and_its_secret(string? clientId, string? clientSecret, bool expected)
    {
        var options = new CalendarOptions
        {
            Google = new CalendarOAuthApp { ClientId = clientId, ClientSecret = clientSecret },
            Microsoft = new CalendarOAuthApp { ClientId = clientId, ClientSecret = clientSecret },
        };

        options.IsAvailable(CalendarProvider.Google).Should().Be(expected);
        options.IsAvailable(CalendarProvider.Microsoft).Should().Be(expected);
        options.AvailableProviders.Should().HaveCount(expected ? 2 : 0);
    }

    [Fact]
    public void With_nothing_configured_no_calendar_is_offered()
    {
        new CalendarOptions().AvailableProviders.Should().BeEmpty();
    }

    [Fact]
    public void The_sandbox_adds_itself_after_the_real_providers()
    {
        var options = new CalendarOptions
        {
            Google = new CalendarOAuthApp { ClientId = "g", ClientSecret = "s" },
            Sandbox = new CalendarSandboxOptions { Enabled = true },
        };

        options.AvailableProviders.Should().Equal(CalendarProvider.Google, CalendarProvider.Sandbox);
        options.IsAvailable(CalendarProvider.Microsoft).Should().BeFalse("a provider that is not configured is hidden");
    }

    [Fact]
    public void The_interview_defaults_are_a_day_of_notice_and_half_hour_steps()
    {
        var interviews = new CalendarOptions().Interviews;

        interviews.MinNoticeHours.Should().Be(24);
        interviews.StepMinutes.Should().Be(30);
    }

    [Fact]
    public void The_sandbox_is_refused_outside_development()
    {
        var result = new CalendarSandboxGate(new Host(Environments.Production))
            .Validate(null, new CalendarOptions { Sandbox = new CalendarSandboxOptions { Enabled = true } });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Sandbox:Enabled").And.Contain("Development");
    }

    [Fact]
    public void The_sandbox_is_allowed_in_development()
    {
        new CalendarSandboxGate(new Host(Environments.Development))
            .Validate(null, new CalendarOptions { Sandbox = new CalendarSandboxOptions { Enabled = true } })
            .Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void Real_providers_and_none_start_in_any_environment(string environment)
    {
        var gate = new CalendarSandboxGate(new Host(environment));

        gate.Validate(null, new CalendarOptions { Google = new CalendarOAuthApp { ClientId = "client", ClientSecret = "secret" } }).Succeeded.Should().BeTrue();
        gate.Validate(null, new CalendarOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("google", CalendarProvider.Google)]
    [InlineData("Microsoft", CalendarProvider.Microsoft)]
    [InlineData("SANDBOX", CalendarProvider.Sandbox)]
    public void A_provider_is_found_by_its_route_name(string slug, CalendarProvider expected)
    {
        CalendarProviderNames.TryParse(slug, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
        CalendarProviderNames.Slug(expected).Should().Be(expected.ToString().ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("zoom")]
    [InlineData("7")]
    public void An_unknown_route_name_is_not_a_provider(string slug)
    {
        CalendarProviderNames.TryParse(slug, out _).Should().BeFalse();
    }

    private sealed class Host(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = "";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
