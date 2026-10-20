namespace ScreenHire.Domain.Jobs;

/// <summary>
/// Postgres keeps <c>timestamp with time zone</c> to the microsecond, .NET to 100 ns. A time compared after a round trip
/// (a frozen job snapshot against the job read back) must be cut to the database's precision first, or it never
/// matches.
/// </summary>
public static class Instants
{
    public static DateTimeOffset ToDatabasePrecision(this DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
}
