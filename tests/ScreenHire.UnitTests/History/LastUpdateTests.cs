using ScreenHire.Api.Contracts;
using ScreenHire.Domain.History;
using ScreenHire.Infrastructure.Persistence.History;

namespace ScreenHire.UnitTests.History;

/// <summary>
/// Last updated on a read contract (change-history.md, API): Created while nothing has changed the object since it was
/// made, by whoever made it when that was recorded, and Updated after; and the members a line names, named when read.
/// </summary>
public sealed class LastUpdateTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = CreatedAt.AddHours(5);
    private static readonly Guid Avery = Guid.CreateVersion7();

    [Fact]
    public void A_recorded_creation_says_created_by_its_maker_and_a_later_change_says_updated()
    {
        LastUpdate.From(new ChangeStamp(CreatedAt, Avery, null, Created: true), CreatedAt)
            .Should().Be(new LastUpdate(CreatedAt, ChangeActorKind.Member, Avery, Created: true));
        LastUpdate.From(new ChangeStamp(Later, Avery, null), CreatedAt, Later)
            .Should().Be(new LastUpdate(Later, ChangeActorKind.Member, Avery));
    }

    [Fact]
    public void With_nothing_recorded_an_object_never_updated_says_created_with_nobody()
    {
        LastUpdate.From(null, CreatedAt).Should().Be(new LastUpdate(CreatedAt, null, Created: true));
        LastUpdate.From(null, CreatedAt, CreatedAt).Should().Be(new LastUpdate(CreatedAt, null, Created: true), "a row stamps both times when it is made");
        LastUpdate.From(null, CreatedAt, Later).Should().Be(new LastUpdate(Later, null), "an edit before history began names nobody");
    }

    [Fact]
    public void Something_people_only_change_never_says_created()
    {
        LastUpdate.FromChange(null, Later).Should().Be(new LastUpdate(Later, null));
        LastUpdate.FromChange(new ChangeStamp(Later, null, null), Later).Should().Be(new LastUpdate(Later, ChangeActorKind.ScreenHire));
    }

    [Fact]
    public void A_line_names_a_member_by_their_current_name_and_a_deleted_account_as_a_former_teammate()
    {
        var sam = Guid.CreateVersion7();
        var gone = Guid.CreateVersion7();
        var record = new ChangeRecord
        {
            SubjectId = "team",
            Lines =
            [
                ChangeLine.Of([new("changed "), ChangeText.Person(sam), new("'s role from Viewer to Admin")]),
                ChangeLine.Of([new("removed "), ChangeText.Person(gone), new(" from the team")]),
            ],
        };

        HistoryMappings.People([record]).Should().BeEquivalentTo([sam, gone]);
        var entry = record.ToEntry(new Dictionary<Guid, string> { [sam] = "Sam Lee" });

        entry.Lines.Select(l => l.Text).Should().Equal("changed Sam Lee's role from Viewer to Admin", "removed a former teammate from the team");
        entry.Lines[0].Parts[1].Should().Be(new HistoryTextPart("Sam Lee", true));
    }
}
