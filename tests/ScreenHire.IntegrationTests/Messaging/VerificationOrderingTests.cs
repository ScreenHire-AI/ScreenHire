using Microsoft.Extensions.DependencyInjection;
using ScreenHire.Application.Messaging;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Applications;
using ScreenHire.Testing;
using Wolverine;

namespace ScreenHire.IntegrationTests.Messaging;

/// <summary>
/// The submission and each content stage as it lands all ask for an application's analysis, often within milliseconds.
/// The worker handles analyses of one application one at a time (Wolverine's partitioned processing in
/// <see cref="ScreenHireMessaging.ConfigureHandler"/>), so none of them fails on a row another one is inserting.
/// docs/architecture/messaging.md, "Ordering".
/// </summary>
public sealed class VerificationOrderingTests(AppHostFixture fixture)
{
    [Fact]
    public async Task Analyses_of_one_application_run_in_turn_so_none_fails_on_a_row_another_inserts()
    {
        var ct = TestContext.Current.CancellationToken;
        // An application written straight to the database: no message reaches the AppHost's worker, so only this test's
        // handler analyzes it, and every analyzer has still to run.
        var organization = await fixture.CreateOrganizationAsync(_ => [], ct);
        var job = TestData.NewJob(organization.Id, $"engineer-{Guid.NewGuid():N}");
        await fixture.AddJobsAsync(organization.Id, [job], ct);
        var applicationId = await fixture.AddApplicationHistoryAsync(
            organization.Id, job.Id, DateTimeOffset.UtcNow, ApplicationSource.CareersSite, cancellationToken: ct);

        var settings = new MessagingSettings { Schema = MessagingHost.NewSchema(), AutoProvision = true };
        await using var worker = await MessagingHost.StartHandlerAsync(fixture, settings, [], ct);
        var bus = worker.Services.GetRequiredService<IMessageBus>();
        const int requests = 6;
        await Task.WhenAll(Enumerable.Range(0, requests)
            .Select(_ => bus.PublishAsync(new AnalyzeApplicationVerification(organization.Id, applicationId)).AsTask()));

        await WolverineStore.WaitUntilAsync(
            async () => await worker.Store.CountHandledAsync(applicationId.ToString(), ct, ".AnalyzeApplicationVerification") == requests,
            "every analysis to be handled", ct);
        worker.Logs.Entries
            .Where(entry => entry.Message.Contains($"Failed to process message {typeof(AnalyzeApplicationVerification).FullName}", StringComparison.Ordinal))
            .Select(entry => entry.Exception?.Message)
            .Should().BeEmpty("analyses of one application never run at the same time, so none hits the unique index on analyzer runs");
    }
}
