using System.Globalization;

namespace ScreenHire.Domain.Jobs;

/// <summary>
/// The English name of a country for text people read. Structured data and storage keep the ISO 3166-1 alpha-2 code;
/// only prose, labels and evidence lines use the name. The names come from .NET's <see cref="RegionInfo"/>, so there is no
/// list to keep (<c>System.Globalization</c> is the base class library, not a framework).
/// </summary>
public static class CountryNames
{
    /// <summary>
    /// <c>United Kingdom</c> for <c>GB</c>. A blank code gives an empty string. An unknown code, or a runtime without
    /// region data, gives the code back, which still tells a reader something.
    /// </summary>
    public static string Name(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "";
        }

        var trimmed = code.Trim();
        try
        {
            return new RegionInfo(trimmed).EnglishName;
        }
        catch (ArgumentException)
        {
            return trimmed;
        }
    }
}
