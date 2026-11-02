using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Scheduling;

namespace ScreenHire.Application.Scheduling;

/// <summary>
/// Calendar settings, <c>ScreenHire:Calendar</c>. The AppHost gives the API and the worker the same values. A provider is
/// available only when both its client id and its secret are set (a bare id cannot work), so with nothing configured a
/// member sees no calendar to connect. <see cref="Sandbox"/> adds the local stand-in, for Development and tests.
/// </summary>
public sealed class CalendarOptions
{
    public const string Section = "ScreenHire:Calendar";

    public CalendarOAuthApp Google { get; set; } = new();

    public CalendarOAuthApp Microsoft { get; set; } = new();

    /// <summary>The local sandbox calendar (<see cref="SandboxCalendarProvider"/>): Development and tests only.</summary>
    public CalendarSandboxOptions Sandbox { get; set; } = new();

    public CalendarInterviewOptions Interviews { get; set; } = new();

    /// <summary>How long one call to a calendar provider may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The providers a member can connect here, in the order the sheet lists them.</summary>
    public IReadOnlyList<CalendarProvider> AvailableProviders
    {
        get
        {
            var available = new List<CalendarProvider>(3);
            if (Google.IsConfigured)
            {
                available.Add(CalendarProvider.Google);
            }

            if (Microsoft.IsConfigured)
            {
                available.Add(CalendarProvider.Microsoft);
            }

            if (Sandbox.Enabled)
            {
                available.Add(CalendarProvider.Sandbox);
            }

            return available;
        }
    }

    public bool IsAvailable(CalendarProvider provider) => AvailableProviders.Contains(provider);
}

/// <summary>An OAuth app registered with a calendar provider. The secret comes from a parameter, never from committed files.</summary>
public sealed class CalendarOAuthApp
{
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public sealed class CalendarSandboxOptions
{
    public bool Enabled { get; set; }
}

/// <summary>How interviews are offered: the notice a slot needs and the step between offered start times.</summary>
public sealed class CalendarInterviewOptions
{
    public int MinNoticeHours { get; set; } = 24;

    public int StepMinutes { get; set; } = 30;
}

/// <summary>
/// Refuses to start a host with the sandbox calendar outside Development: its accounts are made up, and a real
/// organization must never think an interview reached a calendar.
/// </summary>
public sealed class CalendarSandboxGate(IHostEnvironment environment) : IValidateOptions<CalendarOptions>
{
    public ValidateOptionsResult Validate(string? name, CalendarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Sandbox.Enabled && !environment.IsDevelopment()
            ? ValidateOptionsResult.Fail("ScreenHire:Calendar:Sandbox:Enabled is true outside Development. The sandbox calendar is for local runs and tests only.")
            : ValidateOptionsResult.Success;
    }
}
