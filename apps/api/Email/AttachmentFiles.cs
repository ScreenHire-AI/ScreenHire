using ScreenHire.Api.Applications;

namespace ScreenHire.Api.Email;

/// <summary>
/// Which attachments of a reply ScreenHire keeps: the types an applicant may upload (PDF, Word and PNG or JPEG images),
/// at most <see cref="InboundEmailIngest.MaxAttachmentBytes"/>, whose first bytes are what the extension says. Anything
/// else is dropped and the reply says how many were.
/// </summary>
internal static class AttachmentFiles
{
    public static bool TryAccept(InboundAttachment file, out (string FileName, string ContentType) accepted)
    {
        ArgumentNullException.ThrowIfNull(file);
        accepted = default;
        if (file.Content.Length == 0 || file.Content.Length > InboundEmailIngest.MaxAttachmentBytes)
        {
            return false;
        }

        if (FileTypes.SafeFileName(file.FileName) is not { } safe
            || FileTypes.ContentTypeOf(Path.GetExtension(safe).ToLowerInvariant()) is not { } contentType
            || !FileTypes.Matches(Path.GetExtension(safe).ToLowerInvariant(), file.Content.AsSpan(0, Math.Min(file.Content.Length, FileTypes.SignatureLength))))
        {
            return false;
        }

        accepted = (safe, contentType);
        return true;
    }
}
