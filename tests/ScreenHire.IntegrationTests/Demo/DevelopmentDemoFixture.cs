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
