using ScreenHire.Domain.Jobs;

namespace ScreenHire.Api.Configuration;

/// <summary>
/// The system clock cut to Postgres' microsecond precision. A layer over <see cref="TimeProvider"/> because the
/// framework has no precision setting: without it a time the API returns just after saving differs from the same time
/// read back later, and a snapshot frozen with "now" in it never equals the row it was taken from.
/// </summary>
internal sealed class DatabasePrecisionTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => System.GetUtcNow().ToDatabasePrecision();
}
