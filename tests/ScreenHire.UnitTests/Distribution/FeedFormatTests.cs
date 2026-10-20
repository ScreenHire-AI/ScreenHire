using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using ScreenHire.Application.Distribution;
using ScreenHire.Application.Distribution.Feeds;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Distribution;

/// <summary>
/// Every feed, for a spread of jobs, validates against a schema written from the board's published format
/// (<c>Distribution/Schemas</c>, each with its source and access date), and carries content the way the board asks.
/// </summary>
public sealed class FeedFormatTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Boards => ["xml-feed", "careerjet", "talent", "adzuna", "jooble"];

    [Theory]
    [MemberData(nameof(Boards))]
    public void Feed_validates_against_the_boards_schema(string boardId)
    {
        var feed = Adapter(boardId);

        var xml = Render(feed, [OnsiteJob(), RemoteJob(), HourlyInternship()]);

        Validate(xml, boardId).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Boards))]
    public void An_empty_feed_is_still_valid(string boardId)
    {
        Validate(Render(Adapter(boardId), []), boardId).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Boards))]
    public void Descriptions_go_in_cdata_and_survive_a_closing_cdata_marker_and_control_characters(string boardId)
    {
        var content = OnsiteJob() with { Snapshot = OnsiteJob().Snapshot with { Description = "<p>Bring ]]> this\u0001 & that.</p>" } };

        var xml = Render(Adapter(boardId), [content]);

        Validate(xml, boardId).Should().BeEmpty();
        var description = XDocument.Parse(xml).Descendants("description").Single();
        description.Value.Should().Be("<p>Bring ]]> this & that.</p>", "the XML-illegal character is dropped and the rest survives");
        xml.Should().Contain("<![CDATA[");
    }

    [Fact]
    public void Careerjet_names_the_country_and_the_contract()
    {
        var job = Parse("careerjet", OnsiteJob());

        job.Element("location")!.Element("city")!.Value.Should().Be("London");
        job.Element("location")!.Element("country")!.Value.Should().Be("United Kingdom");
        job.Element("contract_type")!.Value.Should().Be("permanent");
        job.Element("working_hours")!.Value.Should().Be("full-time");
        job.Element("salary")!.Value.Should().Be("50000 - 60000 GBP per year");
    }

    [Fact]
    public void Talent_marks_a_remote_job_and_keeps_its_required_city_empty()
    {
        var job = Parse("talent", RemoteJob());

        job.Element("isremote")!.Value.Should().Be("yes");
        job.Element("city")!.Value.Should().BeEmpty();
        job.Element("country")!.Value.Should().Be("Ireland");
        job.Element("dateposted")!.Value.Should().Be("2026-10-01T09:00:00Z");
        job.Element("expirationdate")!.Value.Should().Be("11-01-2026T00:00:00Z", "Talent.com suggests month first for this field");
    }

    [Fact]
    public void Adzuna_uses_its_own_country_code_and_drops_a_frequency_it_has_no_value_for()
    {
        Parse("adzuna", OnsiteJob()).Element("country")!.Value.Should().Be("UK", "Adzuna's list says UK, not GB");
        var weekly = OnsiteJob() with { Snapshot = OnsiteJob().Snapshot with { Salary = new JobSalary(500, 600, "GBP", SalaryPeriod.Week) } };
        Parse("adzuna", weekly).Element("salary_frequency").Should().BeNull("Adzuna has hour, day, month and year only");
        Parse("adzuna", HourlyInternship()).Element("contract_time").Should().BeNull();
    }

    [Fact]
    public void Jooble_puts_the_id_in_an_attribute_and_dates_day_first()
    {
        var job = Parse("jooble", OnsiteJob());

        job.Attribute("id")!.Value.Should().Be(OnsiteJob().JobId.ToString());
        job.Element("region")!.Value.Should().Be("London, England, United Kingdom");
        job.Element("pubdate")!.Value.Should().Be("01.10.2026");
        Parse("jooble", RemoteJob()).Element("region")!.Value.Should().Be("Remote (Ireland)");
    }

    [Fact]
    public void The_generic_feed_carries_every_location_and_country()
    {
        var job = Parse("xml-feed", RemoteJob());

        job.Element("applicantcountries")!.Elements("country").Select(c => c.Value).Should().Equal("IE", "GB");
        job.Element("workplacetype")!.Value.Should().Be("Remote");
        job.Element("version")!.Value.Should().Be("3");
    }

    [Fact]
    public void Talent_and_Adzuna_say_what_a_short_description_lacks()
    {
        var thin = OnsiteJob() with { Snapshot = OnsiteJob().Snapshot with { Description = "<p>Short.</p>" } };

        Adapter("talent").Validate(thin).Should().ContainSingle().Which.Should().Contain("50 characters");
        Adapter("adzuna").Validate(thin).Should().ContainSingle().Which.Should().Contain("100 characters");
        Adapter("careerjet").Validate(thin).Should().BeEmpty();
    }

    private static FeedAdapter Adapter(string boardId)
    {
        var time = new FixedTime(Now);
        FeedAdapter[] all = [new GenericFeedAdapter(time), new CareerjetFeedAdapter(time), new TalentFeedAdapter(time), new AdzunaFeedAdapter(time), new JoobleFeedAdapter(time)];
        return all.Single(a => a.BoardId == boardId);
    }

    private static string Render(FeedAdapter feed, IEnumerable<PostingContent> jobs) =>
        new System.Text.UTF8Encoding(false).GetString(feed.Render(new FeedHeader("Harbour & Co", "https://careers.example.com/harbour", Now), jobs));

    private static XElement Parse(string boardId, PostingContent content) =>
        XDocument.Parse(Render(Adapter(boardId), [content])).Descendants("job").Single();

    private static List<string> Validate(string xml, string boardId)
    {
        var schemas = new XmlSchemaSet();
        schemas.Add(null, Path.Combine(AppContext.BaseDirectory, "Distribution", "Schemas", $"{boardId}.xsd"));
        var errors = new List<string>();
        XDocument.Parse(xml).Validate(schemas, (_, e) => errors.Add(e.Message));
        return errors;
    }

    private static string LongDescription => "<h2>About the role</h2><p>Build and run the systems our customers rely on every day, with a small team that owns its work.</p>";

    private static PostingContent OnsiteJob() => new(
        Guid.Parse("0199f0c4-0000-7000-8000-000000000001"), 1,
        new JobSnapshot(
            "Platform Engineer", "platform-engineer", LongDescription, "Engineering", EmploymentType.FullTime, WorkplaceType.Onsite,
            [new JobLocation("London", "England", "GB")], [], new JobSalary(50_000, 60_000, "GBP", SalaryPeriod.Year),
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)),
        new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero),
        "https://careers.example.com/harbour/jobs/platform-engineer", "Harbour & Co", "harbour");

    private static PostingContent RemoteJob() => OnsiteJob() with
    {
        JobId = Guid.Parse("0199f0c4-0000-7000-8000-000000000002"),
        Version = 3,
        Snapshot = OnsiteJob().Snapshot with
        {
            Title = "Remote Support Lead",
            WorkplaceType = WorkplaceType.Remote,
            Locations = [],
            ApplicantLocationRequirements = ["IE", "GB"],
            Department = null,
        },
    };

    private static PostingContent HourlyInternship() => OnsiteJob() with
    {
        JobId = Guid.Parse("0199f0c4-0000-7000-8000-000000000003"),
        Snapshot = OnsiteJob().Snapshot with
        {
            Title = "Summer Intern",
            EmploymentType = EmploymentType.Internship,
            WorkplaceType = WorkplaceType.Hybrid,
            Locations = [new JobLocation(null, null, "DE")],
            Salary = new JobSalary(null, 18, "EUR", SalaryPeriod.Hour),
            ValidThrough = null,
        },
    };
}

/// <summary>A clock that always says the same time.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
