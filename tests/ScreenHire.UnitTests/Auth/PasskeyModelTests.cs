using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Infrastructure.Identity;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.UnitTests.Auth;

/// <summary>
/// The passkey table is in the model however the context is built. <c>IdentityUserContext</c> reads the Identity schema
/// version from the options of the application service provider it is given, and several contexts get none (the migrator,
/// <c>dotnet ef</c>, the seeder, tests): if the table depended on that, a migration would leave it out or the API's model
/// would differ from the migrations' snapshot.
/// </summary>
public sealed class PasskeyModelTests
{
    private const string Connection = "Host=localhost;Database=screenhire";

    [Fact]
    public void A_context_with_no_service_provider_has_the_passkey_table_and_no_pending_changes()
    {
        var options = new DbContextOptionsBuilder<ScreenHireDbContext>().UseNpgsql(Connection).UseScreenHireConventions().Options;
        using var context = new ScreenHireDbContext(options, new TenantContext());

        context.Model.FindEntityType(typeof(ApplicationUserPasskey))!.GetTableName().Should().Be("user_passkeys");
        context.Database.HasPendingModelChanges().Should().BeFalse("the migrations hold the model the migrator builds");
    }

    [Fact]
    public void A_context_from_a_service_provider_with_Identity_has_the_same_model_as_the_migrations()
    {
        var services = new ServiceCollection();
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<ScreenHireDbContext>();
        services.AddSingleton<ITenantContext, TenantContext>();
        services.AddDbContext<ScreenHireDbContext>(options => options.UseNpgsql(Connection).UseScreenHireConventions());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ScreenHireDbContext>();

        context.Database.HasPendingModelChanges().Should().BeFalse(
            "the API runs the migrator's model; a difference would make a migration check fail at start");
    }
}
