using System.Text.Json;

namespace ScreenHire.Api.Fonts;

/// <summary>
/// The local and test catalogue (<c>ScreenHire:Fonts:Sandbox:Enabled</c>): twelve real Google Fonts families under the
/// OFL or Apache 2.0, plus one under another licence that hosting must refuse, bundled in the API from Fontsource
/// (<c>HeadingFonts/Sandbox</c>, each with its licence text). Each family has one latin WOFF2 file, served for every
/// weight it lists, so a page draws in the real face with no network. The same interface as Fontsource, so the
/// picker, the download on save and serving run exactly as in production.
/// </summary>
internal sealed class SandboxFontSource : IFontSource
{
    private const string Prefix = "HeadingFonts.Sandbox.";
    private static readonly Uri Base = new("sandbox://fonts/");
    private static readonly Lazy<SandboxFont[]> Fonts = new(Load);

    public Task<IReadOnlyList<SourceFont>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SourceFont>>(
            [.. Fonts.Value.Select(f => new SourceFont(f.Id, f.Family, f.Category, f.License, true, ["latin"], f.Weights, f.Styles))]);

    public Task<SourceFontDetail?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var font = Fonts.Value.SingleOrDefault(f => f.Id == id);
        return Task.FromResult(font is null
            ? null
            : new SourceFontDetail(
                font.Id, font.Family, font.Category, font.License, true, font.Version,
                new Dictionary<string, string> { ["latin"] = font.Latin },
                font.File is null ? [] : [.. font.Weights.Select(weight => new SourceFontFile("latin", weight, new Uri(Base, font.File)))]));
    }

    public Task<byte[]> DownloadAsync(SourceFontDetail font, SourceFontFile file, CancellationToken cancellationToken)
    {
        if (!Base.IsBaseOf(file.Url))
        {
            throw new InvalidOperationException("A sandbox font file is outside the sandbox.");
        }

        return Task.FromResult(Read(file.Url.Segments[^1]) ?? throw new InvalidOperationException($"The sandbox has no file {file.Url}."));
    }

    public Task<string?> LicenseTextAsync(SourceFontDetail font, CancellationToken cancellationToken)
    {
        var bytes = Read($"{font.Id}.LICENSE.txt");
        return Task.FromResult(bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes));
    }

    private static byte[]? Read(string name)
    {
        using var stream = typeof(SandboxFontSource).Assembly.GetManifestResourceStream(Prefix + name);
        if (stream is null)
        {
            return null;
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static SandboxFont[] Load()
    {
        var bytes = Read("sandbox-fonts.json") ?? throw new InvalidOperationException("The embedded sandbox-fonts.json is missing.");
        return JsonSerializer.Deserialize<SandboxFont[]>(bytes, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The embedded sandbox-fonts.json is empty.");
    }

    /// <summary>One entry of <c>sandbox-fonts.json</c>: Fontsource's own facts about the family, and its bundled file.</summary>
    private sealed record SandboxFont(
        string Id, string Family, string Category, string License, string Version, int[] Weights, string[] Styles, string Latin, string? File);
}
