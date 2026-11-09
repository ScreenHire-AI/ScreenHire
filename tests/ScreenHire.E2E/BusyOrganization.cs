using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.E2E;

/// <summary>
/// An organization with the volume a small team reaches after a year, built through the API as its Owner, for
/// <see cref="LargeListsTests"/> (design system S9). Everything is a recruiter's own call, so the numbers are the ones
/// the dashboard will see: <see cref="Count"/> of each kind of record, and <see cref="Applications"/> on one job.
/// </summary>
public sealed class BusyOrganization
{
    /// <summary>How many of each kind: jobs, templates, scorecards, email templates, views, tasks, team members, API keys and webhook endpoints.</summary>
    public const int Count = 26;

    /// <summary>
    /// How many webhook endpoints: the most an organization may have (<c>WebhookEndpoints.MaxEndpoints</c>), because each
    /// event is sent to every one. The brief's 25 is past that limit, which is a product rule, so this is the full list.
    /// </summary>
    public const int WebhookEndpoints = 20;

    /// <summary>How many applications the busiest job holds.</summary>
    public const int Applications = 210;

    public const string BusyJobTitle = "Customer Support Lead";

    private static readonly string[] Countries = ["GB"];

    private static readonly string[] ReadScope = ["Read"];

    private static readonly string[] ClosedEvent = ["job.closed"];

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static BusyOrganization? built;

    private BusyOrganization(string ownerEmail, string slug, Guid busyJobId)
    {
        OwnerEmail = ownerEmail;
        Slug = slug;
        BusyJobId = busyJobId;
    }

    public string OwnerEmail { get; }

    public string Slug { get; }

    /// <summary>The job with <see cref="Applications"/> applications, in all stages the pipeline starts with.</summary>
    public Guid BusyJobId { get; }

    /// <summary>
    /// The organization, built once for the test run and shared by every test that reads it. Builds it as a new person
    /// with a new organization, and never changes a seeded one.
    /// </summary>
    public static async Task<BusyOrganization> GetAsync(E2EFixture fixture, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return built ??= await BuildAsync(fixture, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<BusyOrganization> BuildAsync(E2EFixture fixture, CancellationToken cancellationToken)
    {
        var email = ApiSession.NewEmail("e2e-busy");
        using var owner = new ApiSession(fixture);
        await owner.SignInWithDevSsoAsync(email, cancellationToken);
        var slug = $"test-{Guid.NewGuid():N}";
        using (var onboarded = await owner.PostAsync("/v1/auth/onboarding", new { organizationName = "Larkspur Foods", slug }, cancellationToken))
        {
            onboarded.StatusCode.Should().Be(HttpStatusCode.OK, await onboarded.Content.ReadAsStringAsync(cancellationToken));
        }

        // Jobs: the busiest first, then the rest as drafts and a few titles that search can tell apart.
        var busyJobId = await CreateJobAsync(owner, BusyJobTitle, cancellationToken);
        var titles = new[] { "Warehouse Operator", "Kitchen Manager", "Delivery Driver", "Brand Designer", "Finance Analyst", "Data Engineer", "Quality Inspector" };
        var otherJobs = await InParallelAsync(
            Enumerable.Range(1, Count - 1),
            i => CreateJobAsync(owner, $"{titles[i % titles.Length]} {i:D2}", cancellationToken),
            cancellationToken);

        // Applications: the busy job's, plus a handful on another job so the candidates list holds candidates from two jobs.
        var applicationIds = await InParallelAsync(
            Enumerable.Range(1, Applications),
            i => CreateApplicationAsync(owner, busyJobId, $"Applicant{i:D3}", $"Number{i:D3}", cancellationToken),
            cancellationToken);
        await InParallelAsync(
            Enumerable.Range(1, 5),
            i => CreateApplicationAsync(owner, otherJobs[0], $"Other{i:D3}", $"Person{i:D3}", cancellationToken),
            cancellationToken);

        var counted = Enumerable.Range(1, Count).ToList();
        await InParallelAsync(counted, async i =>
        {
            await Expect201Async(owner, "/v1/templates", new { name = $"Hiring template {i:D2}" }, cancellationToken);
            await Expect201Async(owner, "/v1/scorecard-templates", new
            {
                name = $"Scorecard {i:D2}",
                questions = new[] { new { id = "q1", type = "Rating5", label = "How well do they fit the role?", required = true } },
            }, cancellationToken);
            await Expect201Async(owner, "/v1/email-templates", new { name = $"Email template {i:D2}", subject = $"Hello {i:D2}", body = "Hi {{ candidateName }}." }, cancellationToken);
            await Expect201Async(owner, "/v1/views", new
            {
                name = $"View {i:D2}",
                scope = "Candidates",
                jobId = (Guid?)null,
                shared = i % 2 == 0,
                filters = new { baseView = (string?)null, jobIds = Array.Empty<Guid>(), stages = Array.Empty<string>(), authenticity = (string?)null, aiAssistance = (string?)null, tag = (string?)null, sortColumn = (string?)null, sortDescending = false },
            }, cancellationToken);
            await Expect201Async(owner, $"/v1/applications/{applicationIds[i - 1]}/tasks", new { note = $"Follow up with applicant {i:D2}" }, cancellationToken);
            await Expect201Async(owner, "/v1/organization/api-keys", new { name = $"Integration key {i:D2}", scopes = ReadScope }, cancellationToken);
            if (i <= WebhookEndpoints)
            {
                await Expect201Async(owner, "/v1/webhooks", new { url = $"https://hooks.example.com/in/{i:D2}", events = ClosedEvent, description = $"Receiver {i:D2}" }, cancellationToken);
            }

            return 0;
        }, cancellationToken);

        // Team members: each accepts a real invitation, so the page lists real members (the Owner is one more).
        await InParallelAsync(Enumerable.Range(1, Count - 1), i => JoinAsync(fixture, owner, i, cancellationToken), cancellationToken);

        return new BusyOrganization(email, slug, busyJobId);
    }

    private static async Task<Guid> CreateJobAsync(ApiSession owner, string title, CancellationToken cancellationToken)
    {
        var created = await Expect201Async(owner, "/v1/jobs", new
        {
            title,
            department = "Operations",
            description = "<p>Help a growing team run its day to day work, from the first shift to the last, with people who like to help.</p>",
            employmentType = "FullTime",
            workplaceType = "OnSite",
            applicantLocationRequirements = Countries,
            salary = new { min = 30_000, max = 40_000, currency = "GBP", period = "Year" },
        }, cancellationToken);
        return created.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateApplicationAsync(ApiSession owner, Guid jobId, string first, string last, CancellationToken cancellationToken)
    {
        var address = $"{first}.{last}@example.com".ToLowerInvariant();
        var created = await Expect201Async(owner, $"/v1/jobs/{jobId}/applications", new { candidate = new { fullName = $"{first} {last}", email = address } }, cancellationToken);
        return created.GetProperty("id").GetGuid();
    }

    private static async Task<int> JoinAsync(E2EFixture fixture, ApiSession owner, int index, CancellationToken cancellationToken)
    {
        var address = ApiSession.NewEmail($"member{index:D2}");
        await Expect201Async(owner, "/v1/organization/invitations", new { email = address, role = "Recruiter" }, cancellationToken);
        var token = await fixture.Mailpit.WaitForInvitationTokenAsync(address, cancellationToken);
        using var session = new ApiSession(fixture);
        using var accepted = await session.PostAsync("/v1/auth/invitations/accept", new { token }, cancellationToken);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync(cancellationToken));
        return index;
    }

    private static async Task<JsonElement> Expect201Async<T>(ApiSession session, string path, T body, CancellationToken cancellationToken)
    {
        using var response = await session.PostAsync(path, body, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"POST {path}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    // Small batches: the API's rate limits and the worker's backlog are what a recruiter's own clicks stay far under.
    private static async Task<List<TResult>> InParallelAsync<TItem, TResult>(
        IEnumerable<TItem> items, Func<TItem, Task<TResult>> work, CancellationToken cancellationToken)
    {
        var results = new List<TResult>();
        foreach (var batch in items.Chunk(8))
        {
            results.AddRange(await Task.WhenAll(batch.Select(work)).WaitAsync(TimeSpan.FromMinutes(3), cancellationToken));
        }

        return results;
    }

    /// <summary>Formats a count as the dashboard does for a short list.</summary>
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
