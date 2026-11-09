using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScreenHire.Testing;

/// <summary>
/// A software WebAuthn authenticator for the integration tests, standing in for the browser and the device. It holds one
/// ES256 key and answers the API's passkey options the way a platform authenticator does: a registration with the
/// <c>none</c> attestation format, and an assertion signed over the authenticator data and the client data hash. The relying
/// party id and the origin are the ones the API is configured with, so a test can also lie about either to check the
/// refusal. Nothing in it is secret: a test creates a new one per passkey.
/// </summary>
public sealed class PasskeyAuthenticator(string relyingPartyId, string origin, byte[]? credentialId = null) : IDisposable
{
    private const byte UserPresent = 0x01;
    private const byte UserVerified = 0x04;
    private const byte AttestedCredentialData = 0x40;

    private static readonly string[] Transports = ["internal"];

    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private uint signCount;

    /// <summary>The credential id this authenticator registered or will register: random, or the one it was given.</summary>
    public byte[] CredentialId { get; } = credentialId ?? RandomNumberGenerator.GetBytes(32);

    /// <summary>The credential id as the API lists it: base64url.</summary>
    public string Id => Base64Url.EncodeToString(CredentialId);

    private byte[] userHandle = [];

    /// <summary>
    /// The registration ceremony: the browser's <c>credential.toJSON()</c> for the API's creation options. Pass
    /// <paramref name="originOverride"/> to answer from a different origin.
    /// </summary>
    public string Register(string creationOptionsJson, string? originOverride = null)
    {
        using var options = JsonDocument.Parse(creationOptionsJson);
        var root = options.RootElement;
        userHandle = Base64Url.DecodeFromChars(root.GetProperty("user").GetProperty("id").GetString()!);

        var clientData = ClientData("webauthn.create", root.GetProperty("challenge").GetString()!, originOverride ?? origin);
        var authenticatorData = AuthenticatorData(UserPresent | UserVerified | AttestedCredentialData, attested: true);
        var attestationObject = Cbor.Map(
            ("fmt", Cbor.Text("none")),
            ("attStmt", Cbor.EmptyMap),
            ("authData", Cbor.Bytes(authenticatorData)));

        return JsonSerializer.Serialize(new
        {
            id = Id,
            rawId = Id,
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64Url.EncodeToString(clientData),
                attestationObject = Base64Url.EncodeToString(attestationObject),
                transports = Transports,
            },
        });
    }

    /// <summary>
    /// The sign-in ceremony: the browser's <c>credential.toJSON()</c> for the API's request options. The signature covers
    /// the authenticator data and the client data hash; <paramref name="corruptSignature"/> flips a byte of it.
    /// </summary>
    public string Assert(string requestOptionsJson, string? originOverride = null, bool corruptSignature = false)
    {
        using var options = JsonDocument.Parse(requestOptionsJson);
        var clientData = ClientData("webauthn.get", options.RootElement.GetProperty("challenge").GetString()!, originOverride ?? origin);
        signCount++;
        var authenticatorData = AuthenticatorData(UserPresent | UserVerified, attested: false);
        var signature = key.SignData(
            [.. authenticatorData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        if (corruptSignature)
        {
            signature[^1] ^= 0xFF;
        }

        return JsonSerializer.Serialize(new
        {
            id = Id,
            rawId = Id,
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64Url.EncodeToString(clientData),
                authenticatorData = Base64Url.EncodeToString(authenticatorData),
                signature = Base64Url.EncodeToString(signature),
                userHandle = Base64Url.EncodeToString(userHandle),
            },
        });
    }

    public void Dispose() => key.Dispose();

    private static byte[] ClientData(string type, string challenge, string origin) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge, origin, crossOrigin = false }));

    // rpIdHash (32) | flags (1) | signCount (4, big endian) | the attested credential data of a registration.
    private byte[] AuthenticatorData(byte flags, bool attested)
    {
        var data = new List<byte>(SHA256.HashData(Encoding.UTF8.GetBytes(relyingPartyId))) { flags };
        var counter = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(counter, signCount);
        data.AddRange(counter);
        if (attested)
        {
            data.AddRange(new byte[16]); // the AAGUID of an authenticator that does not say which model it is
            var length = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)CredentialId.Length);
            data.AddRange(length);
            data.AddRange(CredentialId);
            data.AddRange(CoseKey());
        }

        return [.. data];
    }

    // The public key as a COSE_Key: kty EC2, alg ES256, crv P-256, x, y.
    private byte[] CoseKey()
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        return Cbor.IntMap(
            (1, Cbor.Int(2)),
            (3, Cbor.Int(-7)),
            (-1, Cbor.Int(1)),
            (-2, Cbor.Bytes(parameters.Q.X!)),
            (-3, Cbor.Bytes(parameters.Q.Y!)));
    }

    // Just enough CBOR (RFC 8949) for an attestation object and a COSE key: small maps, byte and text strings, integers.
    private static class Cbor
    {
        public static readonly byte[] EmptyMap = [0xA0];

        public static byte[] Int(int value) => value >= 0 ? Head(0, (ulong)value) : Head(1, (ulong)(-1 - value));

        public static byte[] Bytes(byte[] value) => [.. Head(2, (ulong)value.Length), .. value];

        public static byte[] Text(string value) => [.. Head(3, (ulong)Encoding.UTF8.GetByteCount(value)), .. Encoding.UTF8.GetBytes(value)];

        public static byte[] Map(params (string Key, byte[] Value)[] entries) =>
            [.. Head(5, (ulong)entries.Length), .. entries.SelectMany(e => Text(e.Key).Concat(e.Value))];

        public static byte[] IntMap(params (int Key, byte[] Value)[] entries) =>
            [.. Head(5, (ulong)entries.Length), .. entries.SelectMany(e => Int(e.Key).Concat(e.Value))];

        private static byte[] Head(int majorType, ulong argument)
        {
            var major = (byte)(majorType << 5);
            return argument switch
            {
                < 24 => [(byte)(major | (byte)argument)],
                <= byte.MaxValue => [(byte)(major | 24), (byte)argument],
                _ => [(byte)(major | 25), (byte)(argument >> 8), (byte)argument],
            };
        }
    }
}
