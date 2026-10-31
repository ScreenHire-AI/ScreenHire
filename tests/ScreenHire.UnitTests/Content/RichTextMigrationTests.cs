using System.Text.RegularExpressions;
using ScreenHire.Infrastructure.Persistence.Migrations;

namespace ScreenHire.UnitTests.Content;

public sealed partial class RichTextMigrationTests
{
    // An update of a FORCE row-level-security table with no tenant set reaches no rows and still succeeds, so a missing
    // lift would leave every body as plain text without a sound.
    [Fact]
    public void Each_update_runs_with_its_tables_force_lifted()
    {
        var sql = ConvertPlainTextToRichText.Convert;
        var updates = Update().Matches(sql).Select(match => (match.Groups[1].Value, match.Index)).ToList();

        updates.Select(update => update.Value).Should().Equal("email_templates", "jobs", "job_versions");
        foreach (var (table, index) in updates)
        {
            var lifted = sql.LastIndexOf($"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;", index, StringComparison.Ordinal);
            var restored = sql.IndexOf($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;", index, StringComparison.Ordinal);

            lifted.Should().BeGreaterThan(-1, table);
            restored.Should().BeGreaterThan(index, table);
            sql[lifted..index].Should().NotContain($"ALTER TABLE {table} FORCE", table);
        }
    }

    [GeneratedRegex(@"UPDATE (\w+)")]
    private static partial Regex Update();
}
