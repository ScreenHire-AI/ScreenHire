namespace ScreenHire.IntegrationTests;

/// <summary>
/// The classes that boot an AppHost of their own for a setting the shared stack does not have (tax at checkout, Stripe
/// mode, the Production environment). They run here, one after another, once the parallel tests have ended: a second stack
/// starting beside them takes their processor time, and <c>ApiReferenceTests</c> missed its one-minute deadline that way
/// three times (docs/process/testing.md, "Flake history"). Running alone also makes their cost a span on the shard's
/// clock, which <c>scripts/test-shard.mjs</c> weighs (<c>alone</c> in <c>tests/shard-durations.json</c>);
/// <c>TestShardPlanTests</c> fails when a class with a stack of its own is left out. The demo's stack has a collection of
/// its own, <c>DevelopmentDemoRun</c>, for the same reason.
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class OwnStackRun;
