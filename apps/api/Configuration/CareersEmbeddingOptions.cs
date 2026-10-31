namespace ScreenHire.Api.Configuration;

/// <summary>
/// The sites allowed to embed an organization's careers pages (<c>ScreenHire:CareersEmbedding</c>,
/// docs/architecture/careers-widget.md).
/// </summary>
public sealed class CareersEmbeddingOptions
{
    public const string Section = "ScreenHire:CareersEmbedding";

    /// <summary>
    /// Also accept plain http sites on this machine (<c>localhost</c>, <c>*.localhost</c>, <c>127.0.0.1</c>), for a local
    /// copy of a site and the test stacks. Development turns it on; never in production, where sites use https.
    /// </summary>
    public bool AllowLoopback { get; set; }
}
