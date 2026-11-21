using System.Text.RegularExpressions;
using ScreenHire.Domain.Hiring;

namespace ScreenHire.UnitTests.Hiring;

/// <summary>The dashboard's list of starting points (the new template screen's cards) names the system templates the migration ships.</summary>
public sealed class StartingPointsTests
{
    [Fact]
    public void The_dashboards_starting_points_are_the_four_system_templates_and_their_newest_versions_in_order()
    {
        var source = File.ReadAllText(RepositoryFiles.PathTo("apps", "dashboard", "src", "lib", "starting-points.ts"));

        var pairs = Regex.Matches(source, "templateId: '([0-9a-f-]+)', versionId: '([0-9a-f-]+)'")
            .Select(m => (Template: Guid.Parse(m.Groups[1].Value), Version: Guid.Parse(m.Groups[2].Value)))
            .ToList();

        pairs.Should().Equal(
            (SystemTemplates.OfficeRole, SystemTemplates.OfficeRoleVersion1),
            (SystemTemplates.TradeRole, SystemTemplates.TradeRoleVersion2),
            (SystemTemplates.Hospitality, SystemTemplates.HospitalityVersion1),
            (SystemTemplates.HighVolume, SystemTemplates.HighVolumeVersion1));
    }
}
