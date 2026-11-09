using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScreenHire.Application.Billing;
using ScreenHire.Domain.Billing;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Billing;

/// <summary>
/// <c>BillingService.CheckPublishAsync</c> (ADR-0029) run against the stack's database as the application role. The job being
/// published is not counted, so a double submit that already opened it is not refused for taking its own place; a different
/// draft still is. The route's wiring is covered by <see cref="BillingPlanLimitTests"/>.
/// </summary>
public sealed class BillingPublishCheckTests(AppHostFixture fixture)
{
    private static readonly DateTimeOffset At = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_job_being_published_is_not_counted_when_it_is_already_the_one_open_job_at_the_limit()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await fixture.SubscribeAsync(owner, "starter");
        var alreadyOpen = (await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"))).Id();
        var otherDraft = (await owner.CreateJobAsync(Samples.RemoteJob("Payroll Specialist"))).Id();
        await owner.PublishJobAsync(alreadyOpen);

        var forItself = await CheckPublishAsync(owner.OrganizationId, alreadyOpen);
        var forAnother = await CheckPublishAsync(owner.OrganizationId, otherDraft);

        forItself.Kind.Should().Be(PublishDecisionKind.Allowed, "the only open job is the one being published");
        forItself.OpenJobs.Should().Be(0);
        forAnother.Kind.Should().Be(PublishDecisionKind.LimitReached);
        forAnother.Limit.Should().Be(1);
        forAnother.OpenJobs.Should().Be(1);
    }

    private async Task<PublishDecision> CheckPublishAsync(Guid organization, Guid jobId)
    {
        var tenant = new TenantContext();
        tenant.SetPublicOrganization(organization);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);

        // The open-job counter opens a scope of its own for the organization, as a worker handler does: that scope gets its
        // own application-role context. A service factory cannot await, and the fixture only awaits the connection string.
        var services = new ServiceCollection();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());
        services.AddScoped(provider => fixture.CreateDbContextAsync(provider.GetRequiredService<ITenantContext>(), Ct).GetAwaiter().GetResult());
        await using var root = services.BuildServiceProvider();

        var options = Options.Create(new BillingOptions { Sandbox = new BillingSandboxOptions { Enabled = true } });
        options.Value.Plans["starter"] = new PlanOptions { ActiveJobs = 1 };
        var time = new FixedTime(At);
        var access = new BillingAccess(options, new StripeBillingProvider(options, new PlanCatalogue(options)), new SandboxBillingProvider(new EphemeralDataProtectionProvider(), time, options));
        var service = new BillingService(db, access, new PlanCatalogue(options), new OpenJobCounter(root.GetRequiredService<IServiceScopeFactory>()), options, time);

        return await service.CheckPublishAsync(organization, jobId, Ct);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
