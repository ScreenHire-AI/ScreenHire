namespace ScreenHire.Api.Applications;

/// <summary>
/// The file types candidates may upload, by extension, with the bytes each format starts with. A question's
/// <c>acceptedFileTypes</c> narrows this list; it never widens it. The extension and the content type the browser
/// sends are the candidate's claims, so a file is accepted only when its first bytes match its extension.
/// </summary>
internal static class FileTypes
{
    private static readonly Dictionary<string, FileType> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = new("application/pdf", [[0x25, 0x50, 0x44, 0x46, 0x2D]]), // %PDF-
        // Word 2007+ is a zip package.
        [".docx"] = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [[0x50, 0x4B, 0x03, 0x04]]),
        // Word 97-2003 is an OLE compound file.
        [".doc"] = new("application/msword", [[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]]),
        [".jpg"] = new("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
        [".jpeg"] = new("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
        [".png"] = new("image/png", [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]]),
    };

    /// <summary>The most bytes <see cref="Matches"/> reads.</summary>
    public const int SignatureLength = 8;

    /// <summary>The extensions a question accepts: its own list within the allowed types, or every allowed type.</summary>
    public static IReadOnlyList<string> AcceptedBy(IReadOnlyList<string>? questionTypes) =>
        questionTypes is { Count: > 0 }
            ? [.. questionTypes.Select(t => t.ToLowerInvariant()).Where(ByExtension.ContainsKey).Distinct()]
            : [.. ByExtension.Keys];

    /// <summary>The media type for <paramref name="extension"/>, or null if it is not an allowed type.</summary>
    public static string? ContentTypeOf(string extension) => ByExtension.GetValueOrDefault(extension)?.ContentType;

    /// <summary>Whether <paramref name="head"/>, a file's first bytes, is what a file with <paramref name="extension"/> starts with.</summary>
    public static bool Matches(string extension, ReadOnlySpan<byte> head)
    {
        if (!ByExtension.TryGetValue(extension, out var type))
        {
            return false;
        }

        foreach (var signature in type.Signatures)
        {
            if (head.StartsWith(signature))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A file name that is safe to store and show: the last path segment, only letters, digits, spaces, dots, dashes and
    /// underscores, at most 100 characters, keeping the extension. Null when nothing usable is left.
    /// </summary>
    public static string? SafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = fileName.Replace('\\', '/').Split('/')[^1];
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var safeStem = new string([.. stem.Select(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' ? c : '_')]).Trim(' ', '.');
        if (extension.Length is 0 or > 6 || !extension[1..].All(char.IsAsciiLetterOrDigit))
        {
            return null;
        }

        safeStem = safeStem.Length == 0 ? "file" : safeStem[..Math.Min(safeStem.Length, 100 - extension.Length)];
        return safeStem + extension.ToLowerInvariant();
    }

    private sealed record FileType(string ContentType, byte[][] Signatures);
}
