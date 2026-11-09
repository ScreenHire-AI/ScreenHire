using System.Diagnostics;
using Microsoft.Extensions.AI;
using ScreenHire.Domain.Verification;

namespace ScreenHire.Infrastructure.Verification.Llm;

/// <summary>
/// What every row of one check's calls shares (docs/architecture/ai-usage.md). The key source, provider and price come from
/// the organization's settings, never from the client that happened to answer, so a fake model standing in for an own key
/// still records an own-key call.
/// </summary>
/// <param name="OrganizationId">The organization the calls belong to.</param>
/// <param name="ApplicationId">The application the check is about; null for a call about none.</param>
/// <param name="Provider">The provider name the rows carry.</param>
/// <param name="KeySource">Whose key pays.</param>
/// <param name="PriceOf">The input and output price per million tokens for a model id, or nulls when unknown.</param>
public sealed record AiUsageContext(
    Guid OrganizationId,
    Guid? ApplicationId,
    string Provider,
    AiKeySource KeySource,
    Func<string, (decimal? Input, decimal? Output)> PriceOf);

/// <summary>
/// Collects the rows for one check's model calls, in memory. The handler saves them with the check, in the same save that
/// completes it, so a call is recorded once: a redelivery finds a finished check and calls nothing.
/// </summary>
public sealed class AiUsageRecorder(AiUsageContext context, TimeProvider time)
{
    private readonly List<AiUsageCall> calls = [];

    /// <summary>The calls made so far, in order.</summary>
    public IReadOnlyList<AiUsageCall> Calls => calls;

    /// <summary>Wraps <paramref name="inner"/> so each call it makes is recorded here.</summary>
    public IChatClient Wrap(IChatClient inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return new UsageRecordingChatClient(inner, this);
    }

    internal void Record(
        string check, string askedModel, ChatResponse? response, Exception? failure, ReportedCost reported,
        DateTimeOffset startedAt, TimeSpan took, CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(response?.ModelId) ? askedModel : response.ModelId!;
        var inputTokens = response?.Usage?.InputTokenCount;
        var outputTokens = response?.Usage?.OutputTokenCount;
        var (inPrice, outPrice) = context.PriceOf(model);
        var (cost, basis) = AiUsageCost.Compute(reported.Value, inputTokens, outputTokens, inPrice, outPrice);
        var outcome = failure is null ? AiCallOutcome.Ok : IsTimeout(failure, cancellationToken) ? AiCallOutcome.TimedOut : AiCallOutcome.Error;
        calls.Add(new AiUsageCall
        {
            OrganizationId = context.OrganizationId,
            OccurredAt = startedAt,
            CheckName = Trim(check, AiUsageCall.MaxCheckNameLength),
            ApplicationId = context.ApplicationId,
            Provider = Trim(context.Provider, AiUsageCall.MaxProviderLength),
            Model = Trim(model, AiUsageCall.MaxModelLength),
            KeySource = context.KeySource,
            ProviderRequestId = response?.ResponseId is { Length: > 0 } id ? Trim(id, AiUsageCall.MaxRequestIdLength) : null,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Cost = cost,
            CostBasis = basis,
            DurationMs = (int)Math.Min(int.MaxValue, took.TotalMilliseconds),
            Outcome = outcome,
            // The type name only: the provider's own text can hold the request, and with it the candidate's words.
            ErrorKind = failure is null ? null : Trim(failure.GetType().Name, AiUsageCall.MaxErrorKindLength),
        });
    }

    internal DateTimeOffset Now() => time.GetUtcNow();

    // A timeout is a timeout the client applied, not the caller giving up.
    private static bool IsTimeout(Exception failure, CancellationToken cancellationToken)
    {
        for (var inner = failure; inner is not null; inner = inner.InnerException)
        {
            if (inner is TimeoutException || (inner is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                return true;
            }
        }

        return false;
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed class UsageRecordingChatClient(IChatClient inner, AiUsageRecorder recorder) : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var check = AiUsageStep.Current ?? AiUsageChecks.ConsistencyComparison;
            var asked = options?.ModelId ?? "";
            var startedAt = recorder.Now();
            var clock = Stopwatch.StartNew();
            var reported = ReportedCost.Begin();
            ChatResponse? response = null;
            Exception? failure = null;
            try
            {
                response = await base.GetResponseAsync(messages, options, cancellationToken);
                return response;
            }
#pragma warning disable CA1031 // Any failure is a row; the exception is rethrown unchanged below.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                failure = exception;
                throw;
            }
            finally
            {
                clock.Stop();
                recorder.Record(check, asked, response, failure, reported, startedAt, clock.Elapsed, cancellationToken);
            }
        }
    }
}

/// <summary>
/// Which check the next model call belongs to, for the usage ledger. Held in an async-local rather than in the request, since
/// the provider adapters send request properties on to the provider, which can refuse a field it does not know.
/// </summary>
public static class AiUsageStep
{
    private static readonly AsyncLocal<string?> Slot = new();

    /// <summary>The check named by the last <see cref="Set"/> in this flow, or null.</summary>
    public static string? Current => Slot.Value;

    /// <summary>Names the check the calls that follow, in this flow, belong to (one of <see cref="AiUsageChecks"/>).</summary>
    public static void Set(string check) => Slot.Value = check;
}

/// <summary>
/// A cost the provider reported for the call in progress (OpenRouter sends <c>usage.cost</c>). The HTTP handler that reads
/// the answer writes it here and the recorder reads it, through an async-local slot, since the chat abstraction carries no
/// cost.
/// </summary>
public sealed class ReportedCost
{
    private static readonly AsyncLocal<ReportedCost?> Slot = new();

    /// <summary>The cost in US dollars, or null when the provider reported none.</summary>
    public decimal? Value { get; set; }

    /// <summary>The slot of the call in progress, or null outside a recorded call.</summary>
    public static ReportedCost? Current => Slot.Value;

    /// <summary>Opens a new slot for the call about to start.</summary>
    public static ReportedCost Begin()
    {
        var slot = new ReportedCost();
        Slot.Value = slot;
        return slot;
    }
}
