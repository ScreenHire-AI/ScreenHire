using ScreenHire.Application.Distribution;

namespace ScreenHire.UnitTests.Distribution;

public sealed class BoardUrlsTests
{
    private const string Job = "https://careers.example.com/harbour/jobs/platform-engineer";

    [Theory]
    [InlineData(Boards.GenericFeed)]
    [InlineData(Boards.Careerjet)]
    [InlineData(Boards.TalentCom)]
    [InlineData(Boards.Adzuna)]
    [InlineData(Boards.Jooble)]
    [InlineData(Boards.Sandbox)]
    public void A_board_that_passes_candidates_on_gets_its_id_as_the_source(string boardId)
    {
        BoardUrls.Tag(Job, boardId).Should().Be($"{Job}?src={boardId}");
        BoardUrls.TaggedBoards.Should().Contain(boardId);
    }

    [Theory]
    [InlineData(Boards.CareersPage)]
    [InlineData(Boards.GoogleIndexing)]
    [InlineData(Boards.JobBoardApi)]
    public void The_boards_whose_links_must_stay_canonical_are_left_alone(string boardId)
    {
        BoardUrls.Tag(Job, boardId).Should().Be(Job);
        BoardUrls.TaggedBoards.Should().NotContain(boardId);
    }

    [Fact]
    public void Other_parameters_and_the_fragment_stay_and_an_earlier_source_is_replaced()
    {
        BoardUrls.Tag($"{Job}?embed=x&src=old&b=1#top", Boards.TalentCom).Should().Be($"{Job}?embed=x&b=1&src=talent#top");
        BoardUrls.Tag($"{Job}?src", Boards.Jooble).Should().Be($"{Job}?src=jooble");
    }

    [Fact]
    public void Every_tagged_id_is_a_shape_the_careers_app_keeps_and_the_column_holds()
    {
        BoardUrls.TaggedBoards.Should().OnlyContain(id => System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9-]{1,40}$"));
    }
}
