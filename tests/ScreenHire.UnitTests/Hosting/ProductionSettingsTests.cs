using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ScreenHire.Api.Configuration;
using ScreenHire.Infrastructure.Hosting;

namespace ScreenHire.UnitTests.Hosting;

/// <summary>
/// A host outside Development stops at start, naming every setting it cannot run without and every local stand-in that is
/// on; a provider with no configuration is not a problem (docs/operations/deploy.md, "Settings").
/// </summary>
public sealed class ProductionSettingsTests
{
    private static readonly Dictionary<string, string?> Api = new()
    {
        ["ConnectionStrings:screenhire-app"] = "Host=db.example.org;Database=screenhire;Username=screenhire_app;Password=p",
        ["ConnectionStrings:blobs"] = "https://storage.example.org/",
        ["ScreenHire:Webhooks:SigningKey"] = new string('k', 40),
        ["ScreenHire:DataProtection:KeyVaultKeyUri"] = "https://vault.example.org/keys/data-protection",
        ["ScreenHire:Verification:DeviceHashKey"] = new string('d', 32),
        ["ScreenHire:Auth:TrustedProxies:0"] = "10.0.0.0/8",
    };

    private static readonly Dictionary<string, string?> Worker = new()
    {
        ["ConnectionStrings:screenhire-app"] = "Host=db.example.org;Database=screenhire;Username=screenhire_app;Password=p",
        ["ConnectionStrings:blobs"] = "https://storage.example.org/",
        ["ScreenHire:Webhooks:SigningKey"] = new string('k', 40),
        ["ScreenHire:DataProtection:KeyVaultKeyUri"] = "https://vault.example.org/keys/data-protection",
        ["ScreenHire:Domains:AppHost"] = "app.example.org",
        ["ScreenHire:Domains:CareersDomain"] = "careers.example.org",
        ["ScreenHire:Domains:CareersHostPattern"] = "{tenant}.{CareersDomain}",
    };

    private static IConfiguration Config(Dictionary<string, string?> values, params (string Key, string? Value)[] changes)
    {
        var copy = new Dictionary<string, string?>(values);
        foreach (var (key, value) in changes)
        {
            copy[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(copy).Build();
    }

    [Fact]
    public void A_complete_api_has_no_problems_and_needs_no_provider()
    {
        // No SendGrid, Slack, calendar, sign-in, Turnstile, Indexing or model setting: every provider stays hidden.
        ProductionSettings.Problems(Config(Api), HostKind.Api).Should().BeEmpty();
    }

    [Fact]
    public void A_complete_worker_has_no_problems()
    {
        ProductionSettings.Problems(Config(Worker), HostKind.Worker).Should().BeEmpty();
    }

    [Theory]
    [InlineData("ConnectionStrings:screenhire-app")]
    [InlineData("ConnectionStrings:blobs")]
    [InlineData("ScreenHire:Webhooks:SigningKey")]
    [InlineData("ScreenHire:DataProtection:KeyVaultKeyUri")]
    public void Each_missing_setting_is_named_for_the_api(string key)
    {
        var problems = ProductionSettings.Problems(Config(Api, (key, null)), HostKind.Api);

        problems.Should().ContainSingle().Which.Should().StartWith(key + " is not set");
    }

    [Theory]
    [InlineData("ScreenHire:Domains:AppHost")]
    [InlineData("ScreenHire:Domains:CareersDomain")]
    [InlineData("ScreenHire:Domains:CareersHostPattern")]
    [InlineData("ScreenHire:Webhooks:SigningKey")]
    public void Each_missing_setting_is_named_for_the_worker(string key)
    {
        var problems = ProductionSettings.Problems(Config(Worker, (key, "  ")), HostKind.Worker);

        problems.Should().ContainSingle().Which.Should().StartWith(key + " is not set");
    }

    [Fact]
    public void Leaving_the_key_ring_unprotected_must_be_said_outright()
    {
        var allowed = Config(Api, ("ScreenHire:DataProtection:KeyVaultKeyUri", null), ("ScreenHire:DataProtection:AllowUnprotectedKeys", "true"));
        var notAllowed = Config(Api, ("ScreenHire:DataProtection:KeyVaultKeyUri", null), ("ScreenHire:DataProtection:AllowUnprotectedKeys", "false"));

        ProductionSettings.Problems(allowed, HostKind.Api).Should().BeEmpty();
        ProductionSettings.Problems(notAllowed, HostKind.Api).Should().ContainSingle().Which.Should().StartWith("ScreenHire:DataProtection:KeyVaultKeyUri is not set");
    }

    [Fact]
    public void The_vault_secret_name_is_given_for_a_setting_that_lives_there()
    {
        var problems = ProductionSettings.Problems(Config(Api, ("ScreenHire:Webhooks:SigningKey", null)), HostKind.Api);

        problems.Should().ContainSingle().Which.Should().Contain("ScreenHire--Webhooks--SigningKey");
    }

    [Fact]
    public void The_device_hash_key_must_be_long_enough()
    {
        var problems = ProductionSettings.Problems(Config(Api, ("ScreenHire:Verification:DeviceHashKey", new string('d', 31))), HostKind.Api);

        problems.Should().ContainSingle().Which.Should().Contain("DeviceHashKey").And.Contain("32");
    }

    [Fact]
    public void The_worker_does_not_need_the_api_only_settings()
    {
        ProductionSettings.Problems(Config(Worker), HostKind.Worker).Should().NotContain(p => p.Contains("DeviceHashKey") || p.Contains("TrustedProxies"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" , ")]
    public void The_api_needs_a_trusted_proxy(string entry)
    {
        var values = new Dictionary<string, string?>(Api) { ["ScreenHire:Auth:TrustedProxies:0"] = entry };

        ProductionSettings.Problems(Config(values), HostKind.Api).Should().ContainSingle().Which.Should().Contain("TrustedProxies");
    }

    [Theory]
    [InlineData("ScreenHire:Webhooks:AllowLoopback", HostKind.Api)]
    [InlineData("ScreenHire:CareersEmbedding:AllowLoopback", HostKind.Api)]
    [InlineData("ScreenHire:Distribution:Sandbox:Enabled", HostKind.Api)]
    [InlineData("ScreenHire:Webhooks:AllowLoopback", HostKind.Worker)]
    [InlineData("ScreenHire:Distribution:Sandbox:Enabled", HostKind.Worker)]
    public void A_local_stand_in_that_is_on_is_a_problem(string key, HostKind kind)
    {
        var problems = ProductionSettings.Problems(Config(kind == HostKind.Api ? Api : Worker, (key, "true")), kind);

        problems.Should().ContainSingle().Which.Should().StartWith(key + " is true");
    }

    [Fact]
    public void Every_problem_is_in_the_one_message_that_stops_a_production_host()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production, DisableDefaults = true });

        var start = () => builder.RequireScreenHireProductionSettings(HostKind.Api);

        var message = start.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("ScreenHire:Webhooks:SigningKey")
            .And.Contain("ScreenHire:DataProtection:KeyVaultKeyUri")
            .And.Contain("ConnectionStrings:screenhire-app")
            .And.Contain("ScreenHire:Verification:DeviceHashKey")
            .And.Contain("ScreenHire:Auth:TrustedProxies");
    }

    [Fact]
    public void Development_starts_with_nothing_set()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development, DisableDefaults = true });

        var start = () => builder.RequireScreenHireProductionSettings(HostKind.Api);

        start.Should().NotThrow();
    }

    [Fact]
    public void Trusted_proxies_are_addresses_or_networks_and_one_entry_may_list_several()
    {
        var options = new AuthOptions { TrustedProxies = ["10.0.0.0/8, 192.168.1.1", "", "  ", "fd00::/8"] };

        options.TrustedProxyEntries().Should().Equal("10.0.0.0/8", "192.168.1.1", "fd00::/8");
    }
}
