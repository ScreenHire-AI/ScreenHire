using Microsoft.Extensions.Configuration;
using ScreenHire.Application.Messaging;
using ScreenHire.Infrastructure.Persistence;

// The deployment's schema step (docs/operations/deploy.md), run by a person or by CI before the apps are deployed, with
// the table owner's credentials. It is the only process that holds them: the API and the worker connect as
// screenhire_app. Every step is idempotent, so it runs again for each release.
//
//   ScreenHire__Migrator__OwnerConnectionString   the PostgreSQL server's admin login, SSL Mode=Require
//   ScreenHire__Migrator__AppRolePassword         the password screenhire_app is given (the screenhire-app-password parameter)
var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args).Build();
var section = configuration.GetSection("ScreenHire:Migrator");
var owner = section["OwnerConnectionString"];
var appPassword = section["AppRolePassword"];
if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(appPassword))
{
    await Console.Error.WriteLineAsync(
        "Set ScreenHire__Migrator__OwnerConnectionString and ScreenHire__Migrator__AppRolePassword (see docs/operations/deploy.md).");
    return 2;
}

try
{
    Console.WriteLine("Applying the EF Core migrations and giving screenhire_app its login.");
    await DatabaseMigrator.MigrateAsync(owner, appPassword);

    Console.WriteLine("Creating Wolverine's storage (the outbox, inbox, queues and schedules).");
    await ScreenHireMessaging.ProvisionStorageAsync(owner);

    Console.WriteLine("Done.");
    return 0;
}
catch (Exception exception)
{
    // The type and message only: an inner exception's text can carry a connection string.
    await Console.Error.WriteLineAsync($"The schema step failed: {exception.GetType().Name}: {exception.Message}");
    return 1;
}
