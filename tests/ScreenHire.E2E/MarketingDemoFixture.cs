namespace ScreenHire.E2E;

/// <summary>
/// An AppHost of its own with the local demo's data switched on (<c>ScreenHire:Demo=true</c>, which is how <c>pnpm dev</c>
/// runs), serving the production builds like the shared stack. The marketing site's product screenshots are taken from
/// Brightwater Studio and its people, so they need the demo's jobs, candidates, history and reports; the shared fixture
/// keeps Brightwater as the seeder leaves it, because other tests count its jobs. Slack and the calendar keep their
/// sandboxes on, so the Settings home's Integrations group and the interview booking page show a working setup.
/// </summary>
public sealed class MarketingDemoFixture : E2EFixture
{
    protected override IReadOnlyList<string> ExtraArguments => ["--ScreenHire:Demo=true"];
}

/// <summary>
/// The screenshot test's own collection, run on its own after the parallel ones: the demo's startup burst (every
/// applicant's verification analysis, the boards' syncs) is heavy enough to hold up the shared stack's worker past other
/// tests' deadlines when two stacks share the machine, as it is for <c>DevelopmentDemoRun</c> in the integration tests.
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class MarketingScreenshotsRun;
