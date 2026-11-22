using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.Api.Fonts;

/// <summary>
/// The Google Fonts catalogue through Fontsource's read-only API, which needs no key (fontsource.org/docs/api: "can be
/// accessed by any HTTP client"; a hard limit of 2,500 requests in 10 seconds), and its files on jsDelivr, whose terms
/// allow commercial use and forbid crawling many packages' files (ADR-0031). The catalogue is one request a day
/// (<see cref="HeadingFontCatalogue"/>); a family is downloaded once, when an organization first saves it, and a
/// preview once, when someone first opens it, at most <see cref="FontsOptions.PreviewsPerMinute"/> a person. Retries and timeouts are the service defaults' standard resilience
/// handler, which every <see cref="HttpClient"/> gets.
/// </summary>
internal sealed class FontsourceSource(HttpClient http, IOptions<FontsOptions> options) : IFontSource
{
    private FontsourceOptions Settings => options.Value.Fontsource;

    public async Task<IReadOnlyList<SourceFont>> ListAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(new Uri(Settings.ApiUrl, "fonts"), cancellationToken)
            ?? throw new HttpRequestException("Fontsource listed no fonts.");
        return [.. document.RootElement.EnumerateArray().Select(ParseFont).OfType<SourceFont>()];
    }

    public async Task<SourceFontDetail?> GetAsync(string id, CancellationToken cancellationToken)
    {
        // The id is checked before it goes into a path; anything else is no family.
        if (!HeadingFonts.IsValidId(id))
        {
            return null;
        }

        using var document = await GetJsonAsync(new Uri(Settings.ApiUrl, $"fonts/{id}"), cancellationToken);
        return document is null ? null : ParseDetail(document.RootElement, Settings.FilesUrl);
    }

    public async Task<byte[]> DownloadAsync(SourceFontDetail font, SourceFontFile file, CancellationToken cancellationToken)
    {
        if (!Settings.FilesUrl.IsBaseOf(file.Url))
        {
            throw new InvalidOperationException("A font file is outside Fontsource's file address.");
        }

        return await http.GetByteArrayAsync(file.Url, cancellationToken);
    }

    public async Task<string?> LicenseTextAsync(SourceFontDetail font, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(Settings.PackageUrl, $"@fontsource/{font.Id}@{font.Version}/LICENSE"), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private async Task<JsonDocument?> GetJsonAsync(Uri url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    /// <summary>One entry of <c>/v1/fonts</c>, or null when it lacks a field the picker needs.</summary>
    internal static SourceFont? ParseFont(JsonElement font)
    {
        if (Text(font, "id") is not { } id || Text(font, "family") is not { } family)
        {
            return null;
        }

        return new SourceFont(
            id, family, Text(font, "category") ?? "other", Text(font, "license") ?? "", Text(font, "type") == "google",
            Strings(font, "subsets"), Weights(font), []);
    }

    /// <summary>
    /// <c>/v1/fonts/{id}</c>: <c>variants</c> is weight, then style, then subset, then <c>url.woff2</c>. Only upright
    /// WOFF2 files under <paramref name="filesUrl"/> are kept.
    /// </summary>
    internal static SourceFontDetail? ParseDetail(JsonElement font, Uri filesUrl)
    {
        if (ParseFont(font) is not { } summary || Text(font, "npmVersion") is not { } version)
        {
            return null;
        }

        var ranges = new Dictionary<string, string>(StringComparer.Ordinal);
        if (font.TryGetProperty("unicodeRange", out var unicodeRange) && unicodeRange.ValueKind == JsonValueKind.Object)
        {
            foreach (var subset in unicodeRange.EnumerateObject())
            {
                if (subset.Value.ValueKind == JsonValueKind.String)
                {
                    ranges[subset.Name] = subset.Value.GetString()!;
                }
            }
        }

        var files = new List<SourceFontFile>();
        if (font.TryGetProperty("variants", out var variants) && variants.ValueKind == JsonValueKind.Object)
        {
            foreach (var weight in variants.EnumerateObject())
            {
                if (!int.TryParse(weight.Name, out var value) || !weight.Value.TryGetProperty("normal", out var normal)
                    || normal.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var subset in normal.EnumerateObject())
                {
                    if (subset.Value.TryGetProperty("url", out var url) && Text(url, "woff2") is { } woff2
                        && Uri.TryCreate(woff2, UriKind.Absolute, out var address) && filesUrl.IsBaseOf(address))
                    {
                        files.Add(new SourceFontFile(subset.Name, value, address));
                    }
                }
            }
        }

        return new SourceFontDetail(summary.Id, summary.Family, summary.Category, summary.License, summary.Google, version, ranges, files);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string[] Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)]
            : [];

    private static int[] Weights(JsonElement element) =>
        element.TryGetProperty("weights", out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out _)).Select(v => v.GetInt32())]
            : [];
}
