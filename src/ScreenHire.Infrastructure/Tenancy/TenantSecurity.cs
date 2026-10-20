namespace ScreenHire.Infrastructure.Tenancy;

/// <summary>Names shared by the row-level security migration, the Development seed and the tests.</summary>
public static class TenantSecurity
{
    /// <summary>
    /// The Postgres role the API and worker connect as. Created by the <c>AddSignInAndTenantIsolation</c> migration:
    /// it neither owns the tables nor has <c>BYPASSRLS</c>, so every policy applies to it.
    /// </summary>
    public const string AppRole = "screenhire_app";
}
