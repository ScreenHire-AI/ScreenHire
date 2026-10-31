using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace ScreenHire.Infrastructure.Verification.Llm;

/// <summary>
/// A deterministic stand-in for a language model, for Development and tests (<see cref="LlmProvider.Fake"/>). It
/// answers the consistency check's two prompts with simple rules, in the same JSON a real model returns, so the whole
/// path runs with no key and no network: claims are sentences or lines that state a number of years ("eight years of
/// .NET", "How many years have you worked as a software engineer? 6 to 9"), and two claims from different sources
/// contradict when their ranges are at least two years apart.
/// </summary>
/// <remarks>Never used outside Development (<see cref="FakeModelGate"/>). It knows nothing a real model knows.</remarks>
public sealed partial class FakeConsistencyChatClient : IChatClient
{
    public const string ModelId = "screenhire-fake-consistency-1";

    private static readonly string[] NumberWords =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
    ];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var system = list.FirstOrDefault(m => m.Role == ChatRole.System)?.Text ?? "";
        var user = list.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        var json = system.StartsWith(ConsistencyPrompts.ComparisonSystem[..40], StringComparison.Ordinal)
            ? Compare(ConsistencyPrompts.Fenced(user, "CLAIMS") ?? "[]")
            : Extract(ConsistencyPrompts.Fenced(user, "DOCUMENT") ?? "");
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
        {
            ModelId = ModelId,
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = (system.Length + user.Length) / 4, OutputTokenCount = json.Length / 4 },
        };
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(FakeConsistencyChatClient) ? this : null;

    public void Dispose()
    {
    }

    // Each sentence or line that states a number of years becomes a years_of_experience claim, quoted whole.
    private static string Extract(string document)
    {
        var claims = new List<object>();
        foreach (var sentence in Sentences().Split(document).Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            if (Years(sentence) is not null)
            {
                claims.Add(new { topic = "years_of_experience", quote = sentence.Length > 200 ? sentence[..200] : sentence });
            }
        }

        return JsonSerializer.Serialize(new { claims });
    }

    private static string Compare(string claimsJson)
    {
        var claims = JsonSerializer.Deserialize<List<PromptClaim>>(claimsJson, PromptJson.Options) ?? [];
        var years = claims
            .Where(c => c.Topic == "years_of_experience")
            .Select(c => (Claim: c, Range: Years(c.Quote)))
            .Where(c => c.Range is not null)
            .ToList();
        var contradictions = new List<object>();
        for (var i = 0; i < years.Count; i++)
        {
            for (var j = i + 1; j < years.Count; j++)
            {
                var (left, right) = (years[i], years[j]);
                if (left.Claim.Source == right.Claim.Source)
                {
                    continue;
                }

                var (low1, high1) = left.Range!.Value;
                var (low2, high2) = right.Range!.Value;
                if (low2 - high1 >= 2 || low1 - high2 >= 2)
                {
                    contradictions.Add(new { topic = "years_of_experience", leftClaimId = left.Claim.Id, rightClaimId = right.Claim.Id });
                }
            }
        }

        return JsonSerializer.Serialize(new { contradictions });
    }

    // "eight years", "10+ years", "6 to 9 years", or a line about years with a number in it ("... years ...? 6 to 9",
    // "10 or more", "Less than 3"): the range of years stated, or null.
    private static (int Low, int High)? Years(string text)
    {
        var match = YearsPattern().Match(text);
        if (!match.Success)
        {
            if (!text.Contains("year", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            match = RangePattern().Match(text);
        }

        if (!match.Success || Number(match.Groups["low"].Value) is not { } low)
        {
            return null;
        }

        var high = match.Groups["high"].Success && Number(match.Groups["high"].Value) is { } upper ? upper : low;
        if (match.Groups["less"].Success)
        {
            return (0, Math.Max(0, low - 1));
        }

        if (match.Groups["more"].Success)
        {
            high = 50;
        }

        return (Math.Min(low, high), Math.Max(low, high));
    }

    private static int? Number(string word)
    {
        if (int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        var index = Array.IndexOf(NumberWords, word.ToLowerInvariant());
        return index < 0 ? null : index;
    }

    // Sentences end at a full stop; a question and its answer on one line stay together.
    [GeneratedRegex(@"(?<=\.)\s+|\n")]
    private static partial Regex Sentences();

    [GeneratedRegex(@"(?<less>less than\s+)?\b(?<low>\d{1,2}|zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)\b\+?(?:\s*(?:-|to)\s*(?<high>\d{1,2}))?(?<more>\s+or more)?", RegexOptions.IgnoreCase)]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"\b(?<low>\d{1,2}|zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty)\+?(?:\s*(?:-|to)\s*(?<high>\d{1,2}))?\s+years?\b", RegexOptions.IgnoreCase)]
    private static partial Regex YearsPattern();
}
