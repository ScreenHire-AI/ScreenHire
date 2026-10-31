using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests.TalentPool;

/// <summary>
/// An owner signed in to a new organization, for the talent pool tests (docs/architecture/talent-pool.md): jobs made
/// through the API, careers applications made the way the careers server sends them, and the reads the pool tests assert
/// on. Every test makes its own, so no test sees another's people. Dispose it.
/// </summary>
internal sealed partial class PoolWorld : IDisposable
{
    private readonly AppHostFixture fixture;

    private PoolWorld(AppHostFixture fixture, Tenant owner, string slug)
    {
        this.fixture = fixture;
        Owner = owner;
        Slug = slug;
        Careers = fixture.CreateHttpClient("api");
    }

    public Tenant Owner { get; }

    /// <summary>The organization's slug, which names its public careers routes.</summary>
    public string Slug { get; }

    /// <summary>An anonymous client, as the careers server calls the public routes.</summary>
    public HttpClient Careers { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<PoolWorld> CreateAsync(AppHostFixture fixture)
    {
        var owner = await fixture.SignedInWithOrganizationAsync();
        try
        {
            var slug = (await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("slug");
            return new PoolWorld(fixture, owner, slug);
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    /// <summary>A job that takes applications, made and posted through the API. Returns its id and slug.</summary>
    public async Task<(Guid Id, string Slug)> OpenJobAsync(string title)
    {
        var created = await Owner.CreateJobAsync(Samples.RemoteJob(title));
        await Owner.PublishJobAsync(created.Id());
        return (created.Id(), created.Text("slug"));
    }

    /// <summary>A job that was posted and then closed.</summary>
    public async Task<Guid> ClosedJobAsync(string title)
    {
        var (id, _) = await OpenJobAsync(title);
        await Owner.CloseJobAsync(id);
        return id;
    }

    /// <summary>A draft job, which takes no applications from the careers site.</summary>
    public async Task<Guid> DraftJobAsync(string title) => (await Owner.CreateJobAsync(Samples.RemoteJob(title))).Id();

    /// <summary>Applies on the careers site as the careers server does, with or without the "keep my details" tick.</summary>
    public async Task<Guid> ApplyAsync(Guid jobId, string email, bool keep, string firstName = "Robin", string lastName = "Ellis")
    {
        var response = await Careers.PostAsJsonAsync($"/v1/public/organizations/{Slug}/jobs/{jobId}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["firstName"] = firstName, ["lastName"] = lastName, ["email"] = email },
            ["privacyNoticeVersion"] = "2026-10-20",
            ["answers"] = new JsonArray(),
            ["aiUseDeclaration"] = "DidNotUse",
            ["keepForFutureRoles"] = keep,
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("applicationId").GetGuid();
    }

    /// <summary>Applies with the tick, keeps the application in mind and returns the application and its candidate.</summary>
    public async Task<(Guid ApplicationId, Guid CandidateId, string Email)> KeptPersonAsync(Guid jobId, string label, string? note = null)
    {
        var email = ApiSession.NewEmail(label);
        var application = await ApplyAsync(jobId, email, keep: true);
        (await KeepAsync([application], note)).Expect(HttpStatusCode.OK).At("kept").GetInt32().Should().Be(1);
        return (application, (await DetailAsync(application)).Id("candidate.id"), email);
    }

    public Task<ApiResult> KeepAsync(IEnumerable<Guid> applicationIds, string? note = null, Tenant? by = null)
    {
        var body = new JsonObject { ["applicationIds"] = Ids(applicationIds) };
        if (note is not null)
        {
            body["note"] = note;
        }

        return (by ?? Owner).PostAsync("/v1/talent-pool", body);
    }

    public async Task<ApiResult> DetailAsync(Guid applicationId) => (await Owner.GetAsync($"/v1/applications/{applicationId}")).Expect(HttpStatusCode.OK);

    public async Task<IReadOnlyList<JsonElement>> TimelineAsync(Guid applicationId) =>
        [.. (await Owner.GetAsync($"/v1/applications/{applicationId}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()];

    /// <summary>The talent pool as <paramref name="by"/> (the owner by default) reads it: the people listed.</summary>
    public async Task<IReadOnlyList<JsonElement>> PoolAsync(Tenant? by = null) =>
        [.. (await (by ?? Owner).GetAsync("/v1/talent-pool")).Expect(HttpStatusCode.OK).At("people").EnumerateArray()];

    public async Task<IReadOnlyList<Guid>> PoolCandidateIdsAsync(Tenant? by = null) =>
        [.. (await PoolAsync(by)).Select(p => p.GetProperty("candidateId").GetGuid())];

    /// <summary>The one timeline entry of this type, or a failed assertion naming the types it has.</summary>
    public async Task<JsonElement> EntryAsync(Guid applicationId, string type)
    {
        var timeline = await TimelineAsync(applicationId);
        var matches = timeline.Where(e => e.GetProperty("type").GetString() == type).ToList();
        matches.Should().ContainSingle($"the timeline has one {type} entry (it has {string.Join(", ", timeline.Select(e => e.GetProperty("type").GetString()))})");
        return matches[0];
    }

    /// <summary>Waits until the application's timeline has an entry of every one of these types.</summary>
    public Task WaitForTimelineAsync(Guid applicationId, params string[] types) =>
        WolverineStore.WaitUntilAsync(
            async () =>
            {
                var present = (await TimelineAsync(applicationId)).Select(e => e.GetProperty("type").GetString()).ToHashSet();
                return types.All(present.Contains);
            },
            $"the timeline to show {string.Join(" and ", types)}",
            Ct);

    /// <summary>Invites these candidates to the job through the API.</summary>
    public Task<ApiResult> InviteAsync(IEnumerable<Guid> candidateIds, Guid jobId, Tenant? by = null) =>
        (by ?? Owner).PostAsync("/v1/talent-pool/invitations", new JsonObject { ["candidateIds"] = Ids(candidateIds), ["jobId"] = jobId });

    /// <summary>
    /// Waits for the invitation to reach the candidate's inbox and returns its text. The inbox may hold other email (the
    /// received confirmation), so this looks for the one that carries the link to stop being kept.
    /// </summary>
    public async Task<string> WaitForInvitationAsync(string email)
    {
        var text = "";
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                text = (await fixture.Mailpit.MessagesAsync(email, Ct)).Select(m => m.Text).FirstOrDefault(t => RemoveLink().IsMatch(t)) ?? "";
                return text.Length > 0;
            },
            "the invitation email to reach the candidate",
            Ct);
        return text;
    }

    /// <summary>The careers board and the token of the link to stop being kept in an invitation's text.</summary>
    public static (string Board, string Token) RemoveLinkIn(string text)
    {
        var match = RemoveLink().Match(text);
        match.Success.Should().BeTrue($"the invitation carries the link to stop being kept: {text}");
        return (match.Groups["board"].Value, match.Groups["token"].Value);
    }

    public string PublicPath(string token, string? slug = null) => $"/v1/public/organizations/{slug ?? Slug}/talent-pool/{token}";

    public static JsonArray Ids(IEnumerable<Guid> ids) => new([.. ids.Select(id => JsonValue.Create(id))]);

    public void Dispose()
    {
        Careers.Dispose();
        Owner.Dispose();
    }

    [GeneratedRegex(@"(?<board>https?://\S+)/talent-pool/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex RemoveLink();
}
