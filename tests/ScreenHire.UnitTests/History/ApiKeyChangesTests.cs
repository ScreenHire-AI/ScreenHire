using ScreenHire.Domain.History;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.History;

/// <summary>
/// The API keys page's history lines (change-history.md): a key created, with its name and access, and a key revoked.
/// What the history sees of a key never holds its hash or its prefix.
/// </summary>
public sealed class ApiKeyChangesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_key_says_its_name_and_access()
    {
        var line = ApiKeyChanges.Describe(null, new ApiKeyFacts("Payroll sync", [ApiKeyScope.Read, ApiKeyScope.Write], null, null)).Single();

        line.Sentence().Should().Be("created the key Payroll sync");
        line.Parts.Should().Contain(new ChangeText("Payroll sync", true));
        line.Details.Should().Equal(new ChangeDetail("Access", null, "Read and write"));
    }

    [Fact]
    public void A_rate_limit_shows_with_the_access()
    {
        var line = ApiKeyChanges.Describe(null, new ApiKeyFacts("Reports", [ApiKeyScope.Read], 1200, null)).Single();

        line.Details.Should().Equal(new ChangeDetail("Access", null, "Read only"), new ChangeDetail("Requests per minute", null, "1,200"));
    }

    [Fact]
    public void Revoking_says_so_once_and_nothing_else_about_a_key_makes_a_line()
    {
        var active = new ApiKeyFacts("Payroll sync", [ApiKeyScope.Read], null, null);
        var revoked = active with { RevokedAt = Now };

        ApiKeyChanges.Describe(active, revoked).Single().Sentence().Should().Be("revoked the key Payroll sync");
        ApiKeyChanges.Describe(revoked, revoked).Should().BeEmpty("a key stays revoked from the first time");
        ApiKeyChanges.Describe(active, active).Should().BeEmpty("using a key changes nothing the history records");
    }

    [Fact]
    public void A_keys_view_has_neither_its_hash_nor_its_prefix()
    {
        var key = new ApiKey
        {
            Name = "Payroll sync", Prefix = "shk_1a2b3c4d", SecretHash = "9f86d081884c7d65", Scopes = [ApiKeyScope.Read], CreatedAt = Now,
            LastUsedAt = Now,
        };

        var view = ApiKeyFacts.Of(key).ToString();

        view.Should().NotContain("shk_1a2b3c4d").And.NotContain("9f86d081884c7d65");
    }
}
