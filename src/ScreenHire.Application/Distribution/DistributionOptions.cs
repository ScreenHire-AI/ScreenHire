namespace ScreenHire.Application.Distribution;

/// <summary>
/// Job distribution settings, <c>ScreenHire:Distribution</c>. The AppHost forwards the same values to the API and the
/// worker, so both agree on which boards exist. Every board that needs something from outside is off until configured.
/// </summary>
public sealed class DistributionOptions
{
    public const string Section = "ScreenHire:Distribution";

    public SandboxBoardOptions Sandbox { get; set; } = new();

    public GoogleIndexingOptions GoogleIndexing { get; set; } = new();

    /// <summary>How often reconciliation asks a push board that has not answered yet, before backing off.</summary>
    public TimeSpan StatusPollInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How long a Queued posting may wait for the worker before reconciliation sends it again.</summary>
    public TimeSpan ResendAfter { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Resends before a Queued posting goes to Error.</summary>
    public int MaxResends { get; set; } = 5;
}

/// <summary>
/// The local sandbox board (<c>SandboxBoardAdapter</c>): a push board that accepts and removes postings after a
/// delay, so publish and teardown run end to end with no third party. Development and tests turn it on.
/// </summary>
public sealed class SandboxBoardOptions
{
    public bool Enabled { get; set; }

    public TimeSpan AcceptanceDelay { get; set; } = TimeSpan.FromSeconds(20);

    public TimeSpan RemovalDelay { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Google's Indexing API (job-distribution.md §3).</summary>
public sealed class GoogleIndexingOptions
{
    /// <summary>
    /// <c>Off</c> (the default) hides the board. <c>Google</c> calls the real API with
    /// <see cref="ServiceAccountKeyJson"/>. <c>Local</c> uses an in-process fake that accepts every notification, for
    /// development and tests.
    /// </summary>
    public GoogleIndexingMode Mode { get; set; } = GoogleIndexingMode.Off;

    /// <summary>The service account's JSON key, from user secrets or the deployment's secret store. Never committed.</summary>
    public string? ServiceAccountKeyJson { get; set; }

    /// <summary>Publish calls a day for the whole Google Cloud project: 200 unless Google granted more.</summary>
    public int DailyQuota { get; set; } = 200;
}

public enum GoogleIndexingMode
{
    Off,
    Local,
    Google,
}
