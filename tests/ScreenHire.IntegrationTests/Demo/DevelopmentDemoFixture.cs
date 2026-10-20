namespace ScreenHire.IntegrationTests.Demo;

/// <summary>
/// An AppHost of its own with the local demo's data switched on (<c>ScreenHire:Demo=true</c>, which is how <c>pnpm dev</c>
/// runs). The shared fixture keeps Brightwater as the seeder leaves it, because other tests count its jobs and the burst of
/// demo submissions would load its worker, so this one starts a second stack for <see cref="DevelopmentDemoTests"/> only.
/// </summary>
public sealed class DevelopmentDemoFixture : AppHostFixture
{
    protected override IReadOnlyList<string> ExtraArguments => ["--ScreenHire:Demo=true"];
}

/// <summary>
/// The demo's tests, run on their own after the parallel ones: the demo's startup burst (every applicant's integrity
/// analysis, the boards' syncs) is heavy enough to hold up the shared stack's worker past other tests' deadlines when the
/// two stacks share the machine.
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class DevelopmentDemoRun;
