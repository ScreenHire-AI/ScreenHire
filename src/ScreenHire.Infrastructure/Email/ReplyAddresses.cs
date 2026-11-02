using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ScreenHire.Infrastructure.Email;

/// <summary>Where a reply belongs: the organization and the send it answers.</summary>
public readonly record struct ReplyTarget(Guid OrganizationId, Guid SendId);

/// <summary>
/// The address a candidate's answer goes to: <c>reply+&lt;token&gt;@&lt;reply host&gt;</c>. The token carries the
/// organization and the send (an <c>application_emails</c> id) and a short MAC under the <c>email-reply-key</c>, so a
/// reply finds its tenant before any read and a malformed or edited address is dropped without one. It is lowercase
/// base32 because mail systems may change the case of a local part. The send's id is the real capability: a version 7
/// Guid, so about 74 random bits behind a timestamp. The 4-byte MAC only rejects damage cheaply, so nothing should rely
/// on it as a secret (ADR-0021).
/// </summary>
/// <remarks>
/// The budget is the RFC 5321 local-part limit of 64 characters: 36 bytes (16 + 16 + 4) are 58 base32 characters, and
/// <c>reply+</c> is 6. A test pins the length, so the token cannot grow past the limit unnoticed.
/// </remarks>
public sealed class ReplyAddresses(IOptions<EmailOptions> options)
{
    public const string LocalPrefix = "reply+";

    /// <summary>The longest local part the address may have (RFC 5321).</summary>
    public const int MaxLocalPartLength = 64;

    private const int MacBytes = 4;
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public bool Enabled => options.Value.RepliesEnabled;

    /// <summary>The host replies arrive on for an organization without a sending domain.</summary>
    public string DefaultHost => options.Value.DefaultReplyHost;

    /// <summary>
    /// The reply address for the send, on <paramref name="host"/> (the organization's reply subdomain) or the default
    /// host, or null when replies are off.
    /// </summary>
    public string? For(Guid organizationId, Guid sendId, string? host = null) =>
        Enabled && Key is { } key ? Create(key, organizationId, sendId, host ?? DefaultHost) : null;

    /// <summary>The target of <paramref name="address"/>, or null when it is not one of ours, was edited, or replies are off.</summary>
    public ReplyTarget? TryParse(string? address) =>
        Enabled && Key is { } key ? Parse(key, address) : null;

    private byte[]? Key => options.Value.ReplyKey is { Length: > 0 } key ? Encoding.UTF8.GetBytes(key) : null;

    internal static string Create(byte[] key, Guid organizationId, Guid sendId, string host)
    {
        Span<byte> payload = stackalloc byte[32 + MacBytes];
        organizationId.TryWriteBytes(payload[..16], bigEndian: true, out _);
        sendId.TryWriteBytes(payload[16..32], bigEndian: true, out _);
        HMACSHA256.HashData(key, payload[..32]).AsSpan(0, MacBytes).CopyTo(payload[32..]);
        return $"{LocalPrefix}{Encode(payload)}@{host}";
    }

    internal static ReplyTarget? Parse(byte[] key, string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var trimmed = address.Trim().Trim('<', '>');
        var at = trimmed.LastIndexOf('@');
        if (at < 0)
        {
            return null;
        }

        var local = trimmed[..at].ToLowerInvariant();
        if (!local.StartsWith(LocalPrefix, StringComparison.Ordinal) || local.Length > MaxLocalPartLength)
        {
            return null;
        }

        if (!TryDecode(local[LocalPrefix.Length..], out var payload) || payload.Length != 32 + MacBytes)
        {
            return null;
        }

        var expected = HMACSHA256.HashData(key, payload.AsSpan(0, 32));
        if (!CryptographicOperations.FixedTimeEquals(expected.AsSpan(0, MacBytes), payload.AsSpan(32)))
        {
            return null;
        }

        return new ReplyTarget(
            new Guid(payload.AsSpan(0, 16), bigEndian: true),
            new Guid(payload.AsSpan(16, 16), bigEndian: true));
    }

    private static string Encode(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder((bytes.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }

            buffer &= (1 << bits) - 1;
        }

        if (bits > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return builder.ToString();
    }

    private static bool TryDecode(string text, out byte[] bytes)
    {
        var result = new List<byte>(text.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;
        foreach (var c in text)
        {
            var value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                bytes = [];
                return false;
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                result.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
                buffer &= (1 << bits) - 1;
            }
        }

        bytes = [.. result];
        return true;
    }
}
