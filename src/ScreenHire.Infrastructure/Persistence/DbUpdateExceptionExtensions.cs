using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ScreenHire.Infrastructure.Persistence;

public static class DbUpdateExceptionExtensions
{
    /// <summary>A unique index refused the write: a concurrent request or delivery got there first.</summary>
    public static bool IsUniqueViolation(this DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}
