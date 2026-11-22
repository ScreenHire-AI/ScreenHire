using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ScreenHire.Api.Auth;

namespace ScreenHire.Api.Mcp;

/// <summary>
/// Asks the person before a consequential tool acts (docs/architecture/mcp-server.md, "Confirmation"): sending an email,
/// inviting a candidate to an interview, publishing or closing a job, or moving a candidate to a Rejected stage.
/// </summary>
/// <remarks>
/// <para>
/// The server is stateless, so it cannot send an <c>elicitation/create</c> request in the middle of a call. A client on
/// the 2026-07-28 protocol revision that declares form elicitation gets a multi round-trip request (MRTR, SEP-2322)
/// instead: the call returns an input-required result with one yes/no question whose message says exactly what will
/// happen, and the client asks the person and calls again with the answer. Any other client must pass
/// <c>confirm: true</c>, and the tool's description and its error tell the assistant to ask the person first.
/// </para>
/// <para>
/// The client echoes <c>requestState</c> back untouched, so it is untrusted: it is protected with ASP.NET Core data
/// protection and holds the tool, a digest of the arguments (all but <c>confirm</c>), the key and an expiry. A retry with
/// other arguments, from another key or after <see cref="Lifetime"/> is refused, so a yes to one email never sends another.
/// The tool runs every validation again on the retry.
/// </para>
/// </remarks>
internal sealed class McpConfirmation(IDataProtectionProvider protection, TimeProvider time)
{
    /// <summary>The argument every confirmed tool takes, and the field of the question the person answers.</summary>
    public const string Argument = "confirm";

    private const string InputKey = "confirm";

    /// <summary>How long the person has to answer.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IDataProtector protector = protection.CreateProtector("ScreenHire.Mcp.Confirmation");

    /// <summary>
    /// Returns once the person has agreed to <paramref name="action"/> (a phrase such as "email the candidate for the
    /// Platform Engineer application"); otherwise throws: an input-required result that asks them, or a tool error that
    /// tells the assistant to ask and call again with <c>confirm: true</c>.
    /// </summary>
    public void Require(McpServer server, RequestContext<CallToolRequestParams> context, ClaimsPrincipal user, bool? confirm, string action)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(user);
        var parameters = context.Params ?? throw new McpException("The call has no parameters.");
        var tool = parameters.Name;
        var digest = Digest(tool, parameters.Arguments);
        var key = user.ApiKeyId()?.ToString() ?? "";

        if (parameters.InputResponses is { Count: > 0 } responses)
        {
            if (parameters.RequestState is not { } state || !Matches(state, tool, digest, key))
            {
                throw new McpException("That confirmation is for another request or has expired, so nothing changed. Call the tool again to ask.");
            }

            if (!responses.TryGetValue(InputKey, out var response) || !Agreed(response))
            {
                throw new McpException($"The person did not agree to {action}, so nothing changed.");
            }

            return;
        }

        if (server.IsMrtrSupported && server.ClientCapabilities?.Elicitation is { } elicitation && (elicitation.Form is not null || elicitation.Url is null))
        {
            throw new InputRequiredException(
                new Dictionary<string, InputRequest>
                {
                    [InputKey] = InputRequest.ForElicitation(new ElicitRequestParams
                    {
                        Message = $"Your assistant wants to {action}. Go ahead?",
                        RequestedSchema = new ElicitRequestParams.RequestSchema
                        {
                            Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                            {
                                [InputKey] = new ElicitRequestParams.BooleanSchema { Title = "Go ahead", Default = false },
                            },
                            Required = [InputKey],
                        },
                    }),
                },
                Protect(tool, digest, key));
        }

        if (confirm != true)
        {
            throw new McpException(
                $"Not done yet: this would {action}. Ask the person you work for whether to go ahead, and only if they say yes, call {tool} again with the same arguments and confirm: true.");
        }
    }

    // The arguments as the client sent them, minus confirm, in name order: the retry must ask for the same thing.
    private static string Digest(string tool, IDictionary<string, JsonElement>? arguments)
    {
        var builder = new StringBuilder(tool);
        foreach (var (name, value) in (arguments ?? new Dictionary<string, JsonElement>()).Where(a => a.Key != Argument).OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            builder.Append('\n').Append(name).Append('=').Append(JsonSerializer.Serialize(value));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private string Protect(string tool, string digest, string key) =>
        protector.Protect(string.Join('|', tool, digest, key, time.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));

    private bool Matches(string state, string tool, string digest, string key)
    {
        string plain;
        try
        {
            plain = protector.Unprotect(state);
        }
        catch (CryptographicException)
        {
            return false;
        }

        var parts = plain.Split('|');
        return parts.Length == 4
            && parts[0] == tool
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]), Encoding.ASCII.GetBytes(digest))
            && parts[2] == key
            && long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            && time.GetUtcNow().ToUnixTimeSeconds() <= expires;
    }

    private static bool Agreed(InputResponse response)
    {
        var raw = response.RawValue;
        if (raw.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        var result = raw.Deserialize<ElicitResult>(McpJsonUtilities.DefaultOptions);
        return result is { IsAccepted: true, Content: { } content }
            && content.TryGetValue(InputKey, out var value)
            && value.ValueKind == JsonValueKind.True;
    }
}
