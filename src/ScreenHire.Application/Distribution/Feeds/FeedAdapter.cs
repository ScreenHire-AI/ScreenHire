using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ScreenHire.Application.Distribution.Adapters;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.Application.Distribution.Feeds;

/// <summary>What every feed says about itself, once.</summary>
/// <param name="OrganizationName">The employer.</param>
/// <param name="BoardUrl">The employer's careers page.</param>
/// <param name="BuiltAt">When the feed was generated.</param>
public sealed record FeedHeader(string OrganizationName, string BoardUrl, DateTimeOffset BuiltAt);

/// <summary>
/// A pull board that crawls an XML feed we serve: one full snapshot of the organization's postings for that board, in
/// the board's published format (docs/architecture/job-distribution.md §2). The job is in the feed from the moment it
/// is published to the board and out of it the moment it is closed.
/// </summary>
public abstract class FeedAdapter(TimeProvider time) : PullBoardAdapter(time), IJobBoardAdapter<XElement>
{
    /// <summary>One job, in the board's element.</summary>
    public abstract XElement Map(PostingContent content);

    /// <summary>The feed's root element around the jobs.</summary>
    protected abstract XElement Root(FeedHeader header, IEnumerable<XElement> jobs);

    public XDocument Feed(FeedHeader header, IEnumerable<PostingContent> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        return new XDocument(new XDeclaration("1.0", "utf-8", null), Root(header, contents.Select(Map)));
    }

    /// <summary>The feed as UTF-8 bytes without a byte order mark, which is what boards' parsers expect.</summary>
    public byte[] Render(FeedHeader header, IEnumerable<PostingContent> contents)
    {
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            Feed(header, contents).Save(writer);
        }

        return stream.ToArray();
    }
}

/// <summary>Building blocks the feed formats share.</summary>
internal static class FeedXml
{
    /// <summary>
    /// An element whose text is a CDATA section, as every board's spec asks for job content, or null (left out) when
    /// there is no value. Characters XML cannot carry are dropped; a <c>]]&gt;</c> inside is split by the writer.
    /// </summary>
    public static XElement? Data(string name, string? value) =>
        string.IsNullOrEmpty(value) ? null : new XElement(name, new XCData(Clean(value)));

    /// <summary>An element with plain text (numbers, codes, dates), or null when there is no value.</summary>
    public static XElement? Plain(string name, string? value) =>
        string.IsNullOrEmpty(value) ? null : new XElement(name, Clean(value));

    public static string Clean(string value)
    {
        if (value.All(XmlConvert.IsXmlChar))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (XmlConvert.IsXmlChar(value[i]))
            {
                builder.Append(value[i]);
            }
            else if (i + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[i + 1], value[i]))
            {
                builder.Append(value, i, 2);
                i++;
            }
        }

        return builder.ToString();
    }

    /// <summary>ISO 8601 in UTC to the second, such as <c>2026-10-20T09:00:00Z</c>.</summary>
    public static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The English name of an ISO 3166-1 alpha-2 country, such as <c>United Kingdom</c> for <c>GB</c>.</summary>
    public static string CountryName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "";
        }

        try
        {
            return new RegionInfo(code).EnglishName;
        }
        catch (ArgumentException)
        {
            // An unknown code, or a runtime without region data: the code is still meaningful to a board.
            return code;
        }
    }

    /// <summary>
    /// Where the job is, for one-line location fields: the first location, or for a remote job the first country
    /// candidates may work from.
    /// </summary>
    public static (string? City, string? Region, string? Country) Place(JobSnapshot snapshot)
    {
        if (snapshot.WorkplaceType != WorkplaceType.Remote && snapshot.Locations.Count > 0)
        {
            var location = snapshot.Locations[0];
            return (location.City, location.Region, location.Country);
        }

        var countries = snapshot.ApplicantLocationRequirements;
        return (null, null, countries.Count > 0 ? countries[0] : snapshot.Locations.Count > 0 ? snapshot.Locations[0].Country : null);
    }

    /// <summary>"City, Region, Country name", or "Remote (Country name)".</summary>
    public static string PlaceText(JobSnapshot snapshot)
    {
        var (city, region, country) = Place(snapshot);
        var parts = new[] { city, region, CountryName(country) }.Where(p => !string.IsNullOrWhiteSpace(p));
        var text = string.Join(", ", parts);
        return snapshot.WorkplaceType == WorkplaceType.Remote ? (text.Length == 0 ? "Remote" : $"Remote ({text})") : text;
    }

    /// <summary>A pay range as one line, such as <c>50000 - 60000 GBP per year</c>.</summary>
    public static string? SalaryText(JobSalary? salary)
    {
        if (salary is null)
        {
            return null;
        }

        var range = (salary.Min, salary.Max) switch
        {
            ({ } min, { } max) when min != max => $"{Number(min)} - {Number(max)}",
            ({ } min, _) => Number(min),
            (_, { } max) => Number(max),
            _ => null,
        };
        return range is null ? null : $"{range} {salary.Currency} per {salary.Period.ToString().ToLowerInvariant()}";
    }
}
