using ScreenHire.Domain.Jobs;

namespace ScreenHire.Infrastructure.Persistence;

/// <summary>
/// The system clock cut to Postgres' microsecond precision; the API and the worker register it as their
/// <see cref="TimeProvider"/>. A layer over <see cref="TimeProvider"/> because the framework has no precision setting:
/// without it a time a host returns or freezes just after saving differs from the same time read back later, and a
/// snapshot with "now" in it never equals the row it was taken from.
/// </summary>
public sealed class DatabasePrecisionTimeProvider : TimeProvider
{
    public static DatabasePrecisionTimeProvider Instance { get; } = new();

    private DatabasePrecisionTimeProvider()
    {
    }

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow().ToDatabasePrecision();
}
