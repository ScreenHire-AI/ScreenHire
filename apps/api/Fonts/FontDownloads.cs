using System.Text.Json;
using Polly;

namespace ScreenHire.Api.Fonts;

/// <summary>What hosting a family and drawing a preview both check of a font source's answer.</summary>
internal static class FontDownloads
{
    /// <summary>A latin subset of one weight is well under this; anything larger is not what we asked for.</summary>
    public const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>A WOFF2 file of a size we would store: not empty, not over <see cref="MaxFileBytes"/>.</summary>
    public static bool IsWoff2(byte[] bytes) => bytes.Length is > 0 and <= MaxFileBytes && bytes.AsSpan().StartsWith("wOF2"u8);

    /// <summary>
    /// The font source could not be reached or answered nonsense: a network or HTTP error, a timeout, a body that is
    /// not JSON, or the standard resilience handler refusing the call (its total timeout or an open circuit throw
    /// <see cref="ExecutionRejectedException"/>, which is not an <see cref="HttpRequestException"/>). Polly comes with
    /// Microsoft.Extensions.Http.Resilience; this reads its exception type and adds no package (ADR-0008).
    /// </summary>
    public static bool IsUnreachable(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException or ExecutionRejectedException;
}
