using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Infrastructure.Tenancy;

/// <summary>
/// Sets the row-level security settings <c>app.tenant_id</c> and <c>app.user_id</c> transaction-locally for every
/// command a <see cref="ScreenHireDbContext"/> runs, from its <see cref="ITenantContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Inside a transaction (every SaveChanges, because the context sets <c>AutoTransactionBehavior.Always</c>, and any
/// explicit transaction), the settings go first as their own command on that transaction. Prefixing a SaveChanges
/// batch would not work: Npgsql reports rows affected per statement, and an extra statement shifts EF's concurrency
/// checks onto the wrong rows.
/// </para>
/// <para>
/// Outside a transaction (plain queries, <c>ExecuteUpdate</c>), the settings are prepended to the command as a
/// <c>DO</c> block, which returns no result set, so EF reads its results unchanged. Npgsql sends a multi-statement
/// command as one batch that Postgres runs in one implicit transaction, so the settings hold for exactly that command.
/// </para>
/// <para>Either way <c>set_config(..., true)</c> ends with the transaction, so nothing leaks to the next user of a pooled
/// connection.</para>
/// <para>
/// A context with neither an organization nor a user sends nothing: an unset setting, or one a finished transaction
/// reverted, already means "no tenant rows" to every policy. That also keeps migrations free of the extra statement,
/// some of which must not run inside a transaction block.
/// </para>
/// </remarks>
public sealed class TenantSessionInterceptor : DbCommandInterceptor
{
    public static readonly TenantSessionInterceptor Instance = new();

    private TenantSessionInterceptor()
    {
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Apply(command, eventData);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await ApplyAsync(command, eventData, cancellationToken);
        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Apply(command, eventData);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await ApplyAsync(command, eventData, cancellationToken);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Apply(command, eventData);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        await ApplyAsync(command, eventData, cancellationToken);
        return result;
    }

    private static void Apply(DbCommand command, CommandEventData eventData)
    {
        if (eventData.Context is not ScreenHireDbContext { Tenant: { OrganizationId: not null } or { UserId: not null } } context)
        {
            return;
        }

        if (command.Transaction is null)
        {
            command.CommandText = SessionSql(context.Tenant) + command.CommandText;
            return;
        }

        using var session = SessionCommand(command, context.Tenant);
        session.ExecuteNonQuery();
    }

    private static async ValueTask ApplyAsync(DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
    {
        if (eventData.Context is not ScreenHireDbContext { Tenant: { OrganizationId: not null } or { UserId: not null } } context)
        {
            return;
        }

        if (command.Transaction is null)
        {
            command.CommandText = SessionSql(context.Tenant) + command.CommandText;
            return;
        }

        await using var session = SessionCommand(command, context.Tenant);
        await session.ExecuteNonQueryAsync(cancellationToken);
    }

    // Created on the connection, not through EF, so it does not pass through this interceptor again.
    private static DbCommand SessionCommand(DbCommand command, ITenantContext tenant)
    {
        var session = command.Connection!.CreateCommand();
        session.Transaction = command.Transaction;
        session.CommandText = SessionSql(tenant);
        return session;
    }

    /// <summary>
    /// The statement that sets both values. Ids are formatted from <see cref="Guid"/> (hex digits and dashes only), so
    /// inlining them is safe; utility statements such as <c>DO</c> cannot take parameters.
    /// </summary>
    internal static string SessionSql(ITenantContext tenant) =>
        string.Create(CultureInfo.InvariantCulture,
            $"DO $$BEGIN PERFORM set_config('app.tenant_id', '{tenant.OrganizationId}', true); PERFORM set_config('app.user_id', '{tenant.UserId}', true); END$$;\n");
}
