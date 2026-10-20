using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ScreenHire.Domain.Integrity;

namespace ScreenHire.Infrastructure.Integrity.Llm;

/// <summary>
/// The LLM consistency check (integrity-engine.md §10.10): the extraction model lists the claims in each source with
/// quotes, every quote is checked against its source, then the consistency model names pairs of claims that contradict
/// each other. What comes back is ids into checked claims, so every contradiction cites a verbatim span from each of
/// two sources, whatever the model wrote.
/// </summary>
/// <remarks>
/// A model that cannot be reached, declines, or answers with something unusable makes the check
/// <see cref="ConsistencyCheckStatus.Unavailable"/>: a missing signal, never a failed application. No source text is
/// ever logged.
/// </remarks>
public sealed partial class ConsistencyChecker(LlmAccess llm, ILogger<ConsistencyChecker> logger)
{
    /// <summary>The longest source text sent, in characters. Longer text is cut, and the check says so.</summary>
    public const int MaxSourceLength = 30_000;

    public const int MaxQuoteLength = 300;

    private const int MaxClaimsPerSource = 60;

    public async Task<ConsistencyCheckOutcome> CheckAsync(IReadOnlyList<ConsistencySourceText> sources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var usable = sources.Where(s => !string.IsNullOrWhiteSpace(s.Text)).GroupBy(s => s.Source).Select(g => g.First()).ToList();
        if (usable.Count < 2)
        {
            return new ConsistencyCheckOutcome(ConsistencyCheckStatus.NotEnoughSources, "Fewer than two sources have text to compare.");
        }

        if (llm.Client is not { } client)
        {
            return new ConsistencyCheckOutcome(ConsistencyCheckStatus.Unavailable, llm.UnavailableReason);
        }

        var usage = new Usage();
        try
        {
            var claims = new List<CheckedClaim>();
            string? extractionModel = null;
            foreach (var source in usable)
            {
                var text = source.Text.Length > MaxSourceLength ? source.Text[..MaxSourceLength] : source.Text;
                var nonce = ConsistencyPrompts.NewNonce();
                var response = await client.GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, ConsistencyPrompts.ExtractionSystem),
                        new ChatMessage(ChatRole.User, ConsistencyPrompts.ExtractionMessage(source.Source, text, nonce)),
                    ],
                    Options(llm.ExtractionModel, ConsistencyPrompts.ExtractionSchema, "claims"),
                    cancellationToken);
                usage.Add(response);
                extractionModel ??= response.ModelId ?? llm.ExtractionModel;
                if (Unusable(response) is { } problem)
                {
                    return usage.Unavailable(problem);
                }

                if (!TryParse<ExtractionAnswer>(response.Text, out var answer))
                {
                    return usage.Unavailable("The model's answer could not be read.");
                }

                var normalized = Normalize(text);
                var prefix = Prefix(source.Source);
                foreach (var claim in answer.Claims ?? [])
                {
                    if (claims.Count(c => c.Source == source.Source) >= MaxClaimsPerSource)
                    {
                        break;
                    }

                    if (claim.Topic is { } topicName && ConsistencyPrompts.Topics.TryGetValue(topicName, out var topic)
                        && Verbatim(normalized, claim.Quote) is { } quote)
                    {
                        claims.Add(new CheckedClaim($"{prefix}{claims.Count(c => c.Source == source.Source) + 1}", source.Source, topic, quote));
                    }
                }
            }

            if (claims.Select(c => c.Source).Distinct().Count() < 2)
            {
                return usage.Completed(extractionModel, null, []);
            }

            var comparisonNonce = ConsistencyPrompts.NewNonce();
            var comparison = await client.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, ConsistencyPrompts.ComparisonSystem),
                    new ChatMessage(ChatRole.User, ConsistencyPrompts.ComparisonMessage(
                        [.. claims.Select(c => new PromptClaim(c.Id, ConsistencyPrompts.SourceName(c.Source), TopicName(c.Topic), c.Quote))],
                        comparisonNonce)),
                ],
                Options(llm.ConsistencyModel, ConsistencyPrompts.ComparisonSchema, "contradictions"),
                cancellationToken);
            usage.Add(comparison);
            var consistencyModel = comparison.ModelId ?? llm.ConsistencyModel;
            if (Unusable(comparison) is { } comparisonProblem)
            {
                return usage.Unavailable(comparisonProblem);
            }

            if (!TryParse<ComparisonAnswer>(comparison.Text, out var found))
            {
                return usage.Unavailable("The model's answer could not be read.");
            }

            var byId = claims.ToDictionary(c => c.Id, StringComparer.Ordinal);
            var contradictions = new List<Contradiction>();
            foreach (var pair in found.Contradictions ?? [])
            {
                if (pair.LeftClaimId is null || pair.RightClaimId is null
                    || !byId.TryGetValue(pair.LeftClaimId, out var left) || !byId.TryGetValue(pair.RightClaimId, out var right)
                    || left.Source == right.Source)
                {
                    continue; // only pairs of checked claims from two different sources
                }

                if (left.Source > right.Source)
                {
                    (left, right) = (right, left);
                }

                var topic = pair.Topic is { } name && ConsistencyPrompts.Topics.TryGetValue(name, out var said) ? said : left.Topic;
                var contradiction = new Contradiction(topic, new CitedSpan(left.Source, left.Quote), new CitedSpan(right.Source, right.Quote));
                if (!contradictions.Contains(contradiction))
                {
                    contradictions.Add(contradiction);
                }
            }

            return usage.Completed(extractionModel, consistencyModel, contradictions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Whatever the provider throws (network, HTTP status, timeout), the check is unavailable, not failed.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogUnavailable(logger, exception.GetType().Name);
            return usage.Unavailable("The model could not be reached.");
        }
    }

    /// <summary>
    /// Text as quotes are compared: whitespace collapsed, typographic quotes and dashes made plain. Applied to both the
    /// source and the quote, so a quote that differs only in spacing or quote style still matches.
    /// </summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            var plain = c switch
            {
                '\u2018' or '\u2019' => '\'',
                '\u201C' or '\u201D' => '"',
                '\u2013' or '\u2014' => '-',
                _ => c,
            };
            if (char.IsWhiteSpace(plain))
            {
                space = result.Length > 0;
                continue;
            }

            if (space)
            {
                result.Append(' ');
                space = false;
            }

            result.Append(plain);
        }

        return result.ToString();
    }

    /// <summary>
    /// The quote as it appears in the source (<paramref name="normalizedSource"/>, already <see cref="Normalize"/>d), or
    /// null when the source does not contain it. Case is ignored for the match; the source's own casing is kept.
    /// </summary>
    public static string? Verbatim(string normalizedSource, string? quote)
    {
        ArgumentNullException.ThrowIfNull(normalizedSource);
        if (string.IsNullOrWhiteSpace(quote))
        {
            return null;
        }

        var wanted = Normalize(quote).Trim().Trim('"');
        if (wanted.Length < 3 || wanted.Length > MaxQuoteLength)
        {
            return null;
        }

        var at = normalizedSource.IndexOf(wanted, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? null : normalizedSource.Substring(at, wanted.Length);
    }

    public static string TopicName(ContradictionTopic topic) =>
        ConsistencyPrompts.Topics.First(t => t.Value == topic).Key;

    private static ChatOptions Options(string model, JsonElement schema, string name) => new()
    {
        ModelId = model,
        ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, name),
    };

    private static string? Unusable(ChatResponse response) =>
        response.FinishReason == ChatFinishReason.ContentFilter ? "The model declined to answer."
        : response.FinishReason == ChatFinishReason.Length ? "The model's answer was cut off."
        : null;

    private static bool TryParse<T>(string text, out T value)
        where T : class
    {
        value = null!;
        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(text[start..(end + 1)], PromptJson.Options)!;
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Prefix(ConsistencySource source) => source switch
    {
        ConsistencySource.Resume => "R",
        ConsistencySource.Answers => "A",
        _ => "L",
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consistency check unavailable: the model call failed with {ExceptionType}")]
    private static partial void LogUnavailable(ILogger logger, string exceptionType);

    private sealed record CheckedClaim(string Id, ConsistencySource Source, ContradictionTopic Topic, string Quote);

    private sealed record ExtractionAnswer(List<ExtractedClaim>? Claims);

    private sealed record ExtractedClaim(string? Topic, string? Quote);

    private sealed record ComparisonAnswer(List<ComparedPair>? Contradictions);

    private sealed record ComparedPair(string? Topic, string? LeftClaimId, string? RightClaimId);

    // Tokens across the calls of one check, for the organization's budget.
    private sealed class Usage
    {
        private long input;
        private long output;

        public void Add(ChatResponse response)
        {
            input += response.Usage?.InputTokenCount ?? 0;
            output += response.Usage?.OutputTokenCount ?? 0;
        }

        public ConsistencyCheckOutcome Unavailable(string note) =>
            new(ConsistencyCheckStatus.Unavailable, note, InputTokens: input, OutputTokens: output);

        public ConsistencyCheckOutcome Completed(string? extractionModel, string? consistencyModel, IReadOnlyList<Contradiction> contradictions) =>
            new(ConsistencyCheckStatus.Completed, null, extractionModel, consistencyModel, contradictions, input, output);
    }
}

/// <summary>One source's text, as it is sent to the model.</summary>
public sealed record ConsistencySourceText(ConsistencySource Source, string Text);
