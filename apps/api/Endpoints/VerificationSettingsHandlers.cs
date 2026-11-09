using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScreenHire.Api.Auth;
using ScreenHire.Api.Contracts;
using ScreenHire.Application.Slack;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.History;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Verification;
using ScreenHire.Infrastructure.Email;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Persistence.History;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.Infrastructure.Verification;
using ScreenHire.Infrastructure.Verification.Llm;

namespace ScreenHire.Api.Endpoints;

/// <summary>
/// The organization's verification settings (verification-engine.md §10): read, replace, and the two routes behind the AI
/// model panel (list models, test a key). The key is write-only: it is protected the moment it arrives, tested before it is
/// saved, and appears in no response, log or History line.
/// </summary>
internal static class VerificationSettingsHandlers
{
    // Every member reads the settings, but the model address and the own key's prices only with Edit settings (its path can hold
    // a token, which is why History keeps the host alone), and the limits with what has been used only with View AI usage.
    public static async Task<Ok<OrganizationVerificationSettings>> Get(
        ScreenHireDbContext db, ITenantContext tenant, IOptions<VerificationOptions> verification, IServiceProvider services, SlackAccess slack,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var settings = await db.VerificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var permissions = await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(db, settings, verification.Value, services, slack, time.GetUtcNow(), permissions, cancellationToken));
    }

    // Upserts the organization's row. The settings apply from the next application: turning a group on or off, or the check,
    // does not reach back to applications already analyzed. A change to the key, provider, address or model is tested first.
    public static async Task<Results<Ok<OrganizationVerificationSettings>, ValidationProblem>> Update(
        UpdateVerificationSettingsRequest request,
        ScreenHireDbContext db,
        ITenantContext tenant,
        IOptions<VerificationOptions> verification,
        OrganizationModels models,
        LlmKeys keys,
        IServiceProvider services,
        SlackAccess slack,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var problems = Problems(request);
        var organizationId = tenant.OrganizationId!.Value;
        var settings = await db.VerificationSettings.SingleOrDefaultAsync(cancellationToken);
        string? newKey = null;
        if (problems.Count == 0 && request.ModelSource == LlmSource.OwnKey)
        {
            (newKey, var credentialProblems) = await CheckModelAsync(request, settings, organizationId, db, models, keys, time, cancellationToken);
            foreach (var (field, message) in credentialProblems)
            {
                problems[field] = [message];
            }
        }

        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var now = time.GetUtcNow();
        var permissions = await RoleAccess.PermissionsOfAsync(db, tenant, cancellationToken);
        if (settings is null)
        {
            settings = new VerificationSettings { OrganizationId = organizationId };
            db.VerificationSettings.Add(settings);
        }

        Apply(settings, request, newKey, keys, now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Two first saves at once: the other created the row. A PUT replaces the settings, so this one is applied
            // on top of it (last write wins) instead of failing.
            db.Entry(settings).State = EntityState.Detached;
            settings = await db.VerificationSettings.SingleAsync(cancellationToken);
            Apply(settings, request, newKey, keys, now);
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await ToResponseAsync(db, settings, verification.Value, services, slack, now, permissions, cancellationToken));
    }

    public static async Task<Results<Ok<VerificationModelList>, ValidationProblem>> ListModels(
        VerificationModelRequest request, ScreenHireDbContext db, OrganizationModels models, LlmKeys keys, CancellationToken cancellationToken)
    {
        var (endpoint, problems) = await EndpointAsync(request, requireModel: false, db, keys, cancellationToken);
        if (endpoint is null)
        {
            return TypedResults.ValidationProblem(problems);
        }

        var listing = await models.ListAsync(endpoint, cancellationToken);
        return TypedResults.Ok(new VerificationModelList(listing.Listed, listing.Models));
    }

    public static async Task<Results<Ok<VerificationModelTest>, ValidationProblem>> TestModel(
        VerificationModelRequest request, ScreenHireDbContext db, ITenantContext tenant,
        OrganizationModels models, LlmKeys keys, TimeProvider time, CancellationToken cancellationToken)
    {
        var (endpoint, problems) = await EndpointAsync(request, requireModel: true, db, keys, cancellationToken);
        if (endpoint is null)
        {
            return TypedResults.ValidationProblem(problems);
        }

        // The test call costs the organization a few tokens, so it is in the ledger like any other.
        var saved = await db.VerificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var result = await TestAndRecordAsync(endpoint, saved?.OwnKeyInputPrice, saved?.OwnKeyOutputPrice, tenant.OrganizationId!.Value, db, models, time, cancellationToken);
        return TypedResults.Ok(new VerificationModelTest(result.Ok, result.Problem));
    }

    // Runs the one-word test and saves its row at once, so a test that fails the save still shows in the ledger.
    private static async Task<ModelTestResult> TestAndRecordAsync(
        ModelEndpoint endpoint, decimal? inputPrice, decimal? outputPrice, Guid organizationId, ScreenHireDbContext db, OrganizationModels models,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var recorder = new AiUsageRecorder(
            new AiUsageContext(organizationId, null, endpoint.Provider.ToString(), AiKeySource.OwnKey, _ => (inputPrice, outputPrice)), time);
        var result = await models.TestAsync(endpoint, cancellationToken, recorder);
        if (recorder.Calls.Count > 0)
        {
            db.AiUsageCalls.AddRange(recorder.Calls);
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>What is wrong with the request itself, by field, before any key is tried.</summary>
    public static Dictionary<string, string[]> Problems(UpdateVerificationSettingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(request.ModelSource) || !Enum.IsDefined(request.DefaultAiUse) || (request.DeviceCheck is { } basis && !Enum.IsDefined(basis)))
        {
            problems["modelSource"] = ["Choose one of the listed options."];
        }

        if (request.ModelSource == LlmSource.OwnKey)
        {
            if (request.Provider is not { } provider || !Enum.IsDefined(provider))
            {
                problems["provider"] = ["Choose a provider."];
            }
            else if (ModelPresets.BaseUrlProblem(provider, request.BaseUrl) is { } urlProblem)
            {
                problems["baseUrl"] = [urlProblem];
            }
            else if (request.BaseUrl is { Length: > VerificationSettings.MaxBaseUrlLength })
            {
                problems["baseUrl"] = ["That address is too long."];
            }

            if (string.IsNullOrWhiteSpace(request.Model))
            {
                problems["model"] = ["Enter the model to use."];
            }
            else if (request.Model.Trim().Length > VerificationSettings.MaxModelLength)
            {
                problems["model"] = ["That model id is too long."];
            }

            if (request.ApiKey is { } key && (string.IsNullOrWhiteSpace(key) || key.Length > VerificationSettings.MaxApiKeyLength))
            {
                problems["apiKey"] = ["Enter the API key, or leave it empty to keep the saved one."];
            }

            AddPriceProblem(problems, "ownKeyInputPrice", request.OwnKeyInputPrice);
            AddPriceProblem(problems, "ownKeyOutputPrice", request.OwnKeyOutputPrice);
        }

        AddTrustedProblems(problems, request.TrustedDomains, "trustedDomains", TrustedSources.NormalizeDomain, "Enter a domain such as partner.example.com.");
        AddTrustedProblems(problems, request.TrustedNetworks, "trustedNetworks", TrustedSources.NormalizeNetwork, null);
        return problems;
    }

    private static void AddPriceProblem(Dictionary<string, string[]> problems, string field, decimal? price)
    {
        if (price is { } value && value is < 0 or > VerificationSettings.MaxPricePerMillion)
        {
            problems[field] = [$"A price is between 0 and {VerificationSettings.MaxPricePerMillion.ToString("N0", CultureInfo.InvariantCulture)} dollars per million tokens."];
        }
    }

    private static void AddTrustedProblems(
        Dictionary<string, string[]> problems, IReadOnlyList<string>? entries, string field, Func<string?, string?> normalize, string? message)
    {
        if (entries is null)
        {
            return;
        }

        if (entries.Count > TrustedSources.MaxEntries)
        {
            problems[field] = [$"At most {TrustedSources.MaxEntries} entries."];
            return;
        }

        foreach (var entry in entries)
        {
            if (normalize(entry) is not null)
            {
                continue;
            }

            // A network says why (too wide, or not a network); a domain has one message.
            problems[field] = [message ?? (TrustedSources.TryParseNetwork(entry, out _, out var problem) ? "Enter a network such as 203.0.113.0/24." : problem!)];
            return;
        }
    }

    // The key for an own-key save, and what the test found wrong. A key travels only when it changed; a save that keeps the
    // saved key and the same target needs no test. A different provider or address needs a new key, so the old one is never
    // sent to a new host.
    private static async Task<(string? NewKey, List<(string Field, string Message)> Problems)> CheckModelAsync(
        UpdateVerificationSettingsRequest request, VerificationSettings? settings, Guid organizationId, ScreenHireDbContext db,
        OrganizationModels models, LlmKeys keys, TimeProvider time, CancellationToken cancellationToken)
    {
        var problems = new List<(string, string)>();
        var provider = request.Provider!.Value;
        var baseUrl = NormalizeBaseUrl(provider, request.BaseUrl);
        var model = request.Model!.Trim();
        var sameTarget = settings is { UsesOwnKey: true } && settings.LlmProvider == provider && settings.LlmBaseUrl == baseUrl;
        string? key = request.ApiKey?.Trim();
        if (key is null)
        {
            if (!sameTarget || settings!.ProtectedLlmApiKey is null)
            {
                problems.Add(("apiKey", sameTarget ? "Enter the API key." : "Enter the API key for this provider."));
                return (null, problems);
            }

            // Keep the saved key, which must still read: a save that hides a dead key would leave every check unavailable.
            key = keys.Unprotect(settings.OrganizationId, settings.ProtectedLlmApiKey);
            if (key is null)
            {
                problems.Add(("apiKey", "The saved key can no longer be read. Enter it again."));
                return (null, problems);
            }

            // Test again only when the model changed.
            if (settings.LlmModel == model)
            {
                return (null, problems);
            }
        }

        var test = await TestAndRecordAsync(
            new ModelEndpoint(provider, baseUrl, key, model), request.OwnKeyInputPrice, request.OwnKeyOutputPrice, organizationId, db, models, time, cancellationToken);
        if (!test.Ok)
        {
            problems.Add(Describe(test.Problem ?? ModelProblem.Other));
        }

        return (request.ApiKey is null ? null : key, problems);
    }

    /// <summary>The field and sentence for a connection problem. Never the provider's own words.</summary>
    public static (string Field, string Message) Describe(ModelProblem problem) => problem switch
    {
        ModelProblem.InvalidKey => ("apiKey", "The provider did not accept this key."),
        ModelProblem.ModelNotFound => ("model", "The provider has no model with this id."),
        ModelProblem.AddressNotAllowed => ("baseUrl", "ScreenHire cannot reach this address. Use the provider's public https address."),
        ModelProblem.Unreachable => ("baseUrl", "The provider did not answer. Check the address and try again."),
        _ => ("model", "The connection test failed. Check the provider, model and key."),
    };

    // The endpoint for a list or test request, taking the saved key when the request names none and the target matches.
    private static async Task<(ModelEndpoint? Endpoint, Dictionary<string, string[]> Problems)> EndpointAsync(
        VerificationModelRequest request, bool requireModel, ScreenHireDbContext db, LlmKeys keys, CancellationToken cancellationToken)
    {
        var problems = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(request.Provider))
        {
            problems["provider"] = ["Choose a provider."];
        }
        else if (ModelPresets.BaseUrlProblem(request.Provider, request.BaseUrl) is { } urlProblem)
        {
            problems["baseUrl"] = [urlProblem];
        }
        else if (request.BaseUrl is { Length: > VerificationSettings.MaxBaseUrlLength })
        {
            problems["baseUrl"] = ["That address is too long."];
        }

        if (requireModel && string.IsNullOrWhiteSpace(request.Model))
        {
            problems["model"] = ["Enter the model to use."];
        }
        else if (request.Model?.Trim().Length > VerificationSettings.MaxModelLength)
        {
            problems["model"] = ["That model id is too long."];
        }

        if (request.ApiKey is { Length: > VerificationSettings.MaxApiKeyLength })
        {
            problems["apiKey"] = ["That key is too long."];
        }

        if (problems.Count > 0)
        {
            return (null, problems);
        }

        var baseUrl = NormalizeBaseUrl(request.Provider, request.BaseUrl);
        var key = request.ApiKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            var settings = await db.VerificationSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (settings is { UsesOwnKey: true, ProtectedLlmApiKey: { } protectedKey } && settings.LlmProvider == request.Provider && settings.LlmBaseUrl == baseUrl)
            {
                key = keys.Unprotect(settings.OrganizationId, protectedKey);
            }
        }

        if (string.IsNullOrEmpty(key))
        {
            problems["apiKey"] = ["Enter the API key."];
            return (null, problems);
        }

        return (new ModelEndpoint(request.Provider, baseUrl, key, request.Model?.Trim()), problems);
    }

    // A typed address ends with a slash, because the client adds its paths to it. A preset with a fixed address keeps none:
    // the preset supplies it each time, so a provider that moves its endpoint needs no data change.
    private static string? NormalizeBaseUrl(ModelProvider provider, string? baseUrl) =>
        ModelPresets.For(provider).BaseUrlEditable ? baseUrl!.Trim().TrimEnd('/') + "/" : null;

    private static void Apply(VerificationSettings settings, UpdateVerificationSettingsRequest request, string? newKey, LlmKeys keys, DateTimeOffset now)
    {
        settings.LlmConsistencyCheckEnabled = request.LlmConsistencyCheck;
        if (request.ModelSource == LlmSource.OwnKey)
        {
            settings.LlmSource = LlmSource.OwnKey;
            settings.OwnKeyInputPrice = request.OwnKeyInputPrice;
            settings.OwnKeyOutputPrice = request.OwnKeyOutputPrice;
            settings.LlmProvider = request.Provider;
            settings.LlmBaseUrl = NormalizeBaseUrl(request.Provider!.Value, request.BaseUrl);
            settings.LlmModel = request.Model!.Trim();
            if (newKey is not null)
            {
                settings.ProtectedLlmApiKey = keys.Protect(settings.OrganizationId, newKey);
                settings.LlmKeyVersion++;
            }
        }
        else
        {
            // ScreenHire's model: nothing of an own key is kept.
            settings.LlmSource = LlmSource.Included;
            settings.LlmProvider = null;
            settings.LlmBaseUrl = null;
            settings.LlmModel = null;
            settings.ProtectedLlmApiKey = null;
            settings.OwnKeyInputPrice = null;
            settings.OwnKeyOutputPrice = null;
        }

        var checks = request.Checks ?? new VerificationChecks();
        settings.CheckNetwork = checks.Network;
        settings.CheckContact = checks.Contact;
        settings.CheckDocuments = checks.Documents;
        settings.CheckAnswers = checks.Answers;
        settings.CheckDuplicates = checks.Duplicates;
        settings.CheckFormUse = checks.FormUse;
        settings.DefaultAiUse = request.DefaultAiUse;
        settings.DeviceCheck = request.DeviceCheck;
        settings.TrustedDomains = Normalized(request.TrustedDomains, TrustedSources.NormalizeDomain);
        settings.TrustedNetworks = Normalized(request.TrustedNetworks, TrustedSources.NormalizeNetwork);

        var alerts = request.Alerts ?? new VerificationAlerts();
        settings.SuggestedAlertEmail = alerts.SuggestedEmail;
        settings.SuggestedAlertSlack = alerts.SuggestedSlack;
        settings.BudgetAlertEmail = alerts.BudgetEmail;
        settings.BudgetAlertSlack = alerts.BudgetSlack;
        settings.UpdatedAt = now;
    }

    private static string[] Normalized(IReadOnlyList<string>? entries, Func<string?, string?> normalize) =>
        [.. (entries ?? []).Select(normalize).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static async Task<OrganizationVerificationSettings> ToResponseAsync(
        ScreenHireDbContext db, VerificationSettings? settings, VerificationOptions options, IServiceProvider services, SlackAccess slack,
        DateTimeOffset now, IReadOnlySet<Permission> permissions, CancellationToken cancellationToken)
    {
        var edits = permissions.Contains(Permission.EditSettings);
        var limits = permissions.Contains(Permission.ViewAiUsage)
            ? await AiUsageHandlers.LimitsAsync(db, settings, options.Llm, now, cancellationToken)
            : null;
        var last = settings is null
            ? null
            : await ChangeHistoryQueries.LastAsync(db, ChangeSubjectKind.VerificationSettings, settings.OrganizationId.ToString("D"), cancellationToken);
        // No row is ScreenHire's defaults, which nobody in the organization has changed.
        var lastUpdated = settings is null ? LastUpdate.ByScreenHire(null) : LastUpdate.FromChange(last, settings.UpdatedAt);
        var installation = slack.IsConfigured ? await db.SlackInstallations.AsNoTracking().SingleOrDefaultAsync(cancellationToken) : null;
        var config = VerificationConfig.From(settings);
        return new OrganizationVerificationSettings(
            settings?.LlmConsistencyCheckEnabled ?? false,
            lastUpdated,
            settings?.LlmSource ?? LlmSource.Included,
            settings?.LlmProvider,
            edits ? settings?.LlmBaseUrl : null,
            settings?.LlmModel,
            !string.IsNullOrEmpty(settings?.ProtectedLlmApiKey),
            edits ? settings?.OwnKeyInputPrice : null,
            edits ? settings?.OwnKeyOutputPrice : null,
            limits,
            [.. ModelPresets.All.Select(p => new ModelPresetView(p.Provider, p.BaseUrl, p.BaseUrlEditable, p.ListsModels))],
            new VerificationChecks(config.Network, config.Contact, config.Documents, config.Answers, config.Duplicates, config.FormUse),
            settings?.DefaultAiUse ?? AiUsePolicy.AllowedWithDisclosure,
            settings?.DeviceCheck,
            settings?.TrustedDomains ?? [],
            settings?.TrustedNetworks ?? [],
            new VerificationAlerts(
                settings?.SuggestedAlertEmail ?? false, settings?.SuggestedAlertSlack ?? false,
                settings?.BudgetAlertEmail ?? false, settings?.BudgetAlertSlack ?? false),
            services.GetService<IEmailSender>() is not null,
            installation is { IsDisconnected: false });
    }
}
