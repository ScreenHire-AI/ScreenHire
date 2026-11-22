namespace ScreenHire.Api.Hiring;

/// <summary>
/// <c>ScreenHire:Forms</c>: the form library's settings (docs/architecture/workflow-engine.md section 7, Forms).
/// </summary>
public sealed class FormsOptions
{
    public const string Section = "ScreenHire:Forms";

    public EqualityMonitoringOptions EqualityMonitoring { get; set; } = new();

    /// <summary>
    /// <c>Enabled</c> (default false) offers equality monitoring forms in the library and their totals in Reports. Their
    /// answers never show on a candidate's page whatever this says. It stays off until the owner's legal review: which
    /// questions such a form may ask, and how its answers are kept.
    /// </summary>
    public sealed class EqualityMonitoringOptions
    {
        public bool Enabled { get; set; }
    }
}
