using System.Text.Json;
using Npgsql;
using ScreenHire.Application.Verification;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Shared steps for the tests about one person across jobs: jobs on the careers board, applicants that no other test
/// uses (so a scan of the whole database for their name only ever finds their own rows), and the owner-role reads
/// that prove an erasure left nothing behind.
/// </summary>
internal static class CandidateTestSupport
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A published remote job with this title in the tenant's organization.</summary>
    public static async Task<Guid> OpenJobTitledAsync(Tenant tenant, string title)
    {
        var jobId = (await tenant.CreateJobAsync(Samples.RemoteJob(title))).Id();
        await tenant.PublishJobAsync(jobId);
        return jobId;
    }

    /// <summary>The organization's slug, which names its public careers routes.</summary>
    public static async Task<string> SlugAsync(Tenant tenant) =>
        (await tenant.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("slug");

    /// <summary>
    /// The applicant with a first name, last name and address no other test has, and a resume that says the name, so the
    /// extracted document holds it. Everything else (network, device, phone) is the template's.
    /// </summary>
    public static SyntheticApplicant AsNewPerson(this SyntheticApplicant template)
    {
        var (first, last) = (Letters("Quill", 5), Letters("Marlow", 6));
        return template with
        {
            FirstName = first,
            LastName = last,
            Email = ApiSession.NewEmail("person"),
            ResumeFileName = $"{first} {last} CV.pdf",
            ResumeText = $"{first} {last}\nBackend engineer. I build payment and reporting services.",
        };
    }

    /// <summary>
    /// A member of the application farm under a name and address no other test has. The resume, phone and device stay the
    /// farm's, so the members still share them.
    /// </summary>
    public static SyntheticApplicant AsNewFarmMember(this SyntheticApplicant template) => template with
    {
        FirstName = Letters("Wren", 5),
        LastName = Letters("Ashdown", 6),
        Email = $"talentdesk+{Letters("t", 8).ToLowerInvariant()}@example.net",
    };

    /// <summary>Random lower-case letters after a capital prefix, so a name is never also a word that appears in other data.</summary>
    private static string Letters(string prefix, int count) =>
        prefix + new string([.. Guid.NewGuid().ToByteArray().Take(count).Select(b => (char)('a' + (b % 26)))]);

    /// <summary>The id of the candidate behind an application.</summary>
    public static async Task<Guid> CandidateOfAsync(Tenant tenant, Guid applicationId) =>
        (await tenant.GetAsync($"/v1/applications/{applicationId}")).Expect(HttpStatusCode.OK).Id("candidate.id");

    /// <summary>
    /// The application's verification once its review log has <paramref name="count"/> entries of this type and, if given,
    /// <paramref name="alsoHolds"/>. The verification route reads the log, the assessments and the signals in separate
    /// queries, so a read that lands between a re-check's commit and its own queries can show the new log entry beside the
    /// old level. Waiting for what the test goes on to assert, in the same read, keeps the test off that seam.
    /// </summary>
    public static async Task<ApiResult> WaitForReviewEntryAsync(
        Tenant tenant, Guid applicationId, string type, int count = 1, Func<ApiResult, bool>? alsoHolds = null)
    {
        ApiResult? result = null;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                result = (await tenant.GetAsync($"/v1/applications/{applicationId}/verification")).Expect(HttpStatusCode.OK);
                return result.At("reviewLog").EnumerateArray().Count(e => e.GetProperty("type").GetString() == type) >= count
                    && (alsoHolds is null || alsoHolds(result));
            },
            $"a {type} entry in the review log" + (alsoHolds is null ? "" : " beside the evidence it describes"),
            Ct);
        return result!;
    }

    /// <summary>The review log's entries of one type.</summary>
    public static IReadOnlyList<JsonElement> ReviewEntries(ApiResult verification, string type) =>
        [.. verification.At("reviewLog").EnumerateArray().Where(e => e.GetProperty("type").GetString() == type)];

    /// <summary>The codes of the signals the authenticity assessment counts.</summary>
    public static IReadOnlyList<string> AuthenticityCodes(ApiResult verification) =>
        [.. verification.At("authenticity.signals").EnumerateArray().Select(s => s.GetProperty("code").GetString()!)];

    /// <summary>
    /// The tables (and the value that matched) with a row that holds one of these values anywhere in its text form: text,
    /// varchar and jsonb columns, arrays and ids alike. Read as the table owner, past row-level security, across every
    /// organization, so a value must be one no other test uses. Empty when nothing holds them.
    /// </summary>
    public static async Task<IReadOnlyList<string>> TablesHoldingAsync(AppHostFixture fixture, params string[] values)
    {
        await using var connection = await OwnerAsync(fixture);
        var hits = new List<string>();
        foreach (var table in await BaseTablesAsync(connection))
        {
            foreach (var value in values)
            {
                // strpos, not LIKE: an email's underscore or a percent sign would be a wildcard.
                await using var command = new NpgsqlCommand($"SELECT count(*) FROM public.\"{table}\" AS r WHERE strpos(lower(r::text), lower(@value)) > 0", connection);
                command.Parameters.AddWithValue("value", value);
                if ((long)(await command.ExecuteScalarAsync(Ct))! > 0)
                {
                    hits.Add($"{table} holds {value}");
                }
            }
        }

        return hits;
    }

    /// <summary>
    /// The tables with a <c>candidate_id</c> or <c>application_id</c> column that still has a row for these ids, read as the
    /// table owner. Which tables have such a column is read from <c>information_schema</c>, so a table added later is checked too.
    /// </summary>
    public static async Task<IReadOnlyList<string>> RowsReferencingAsync(AppHostFixture fixture, IReadOnlyCollection<Guid> candidateIds, IReadOnlyCollection<Guid> applicationIds)
    {
        await using var connection = await OwnerAsync(fixture);
        var tables = await BaseTablesAsync(connection);
        var columns = new List<(string Table, string Column)>();
        await using (var list = new NpgsqlCommand(
            "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'public' AND column_name IN ('candidate_id', 'application_id')", connection))
        await using (var reader = await list.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                if (tables.Contains(reader.GetString(0)))
                {
                    columns.Add((reader.GetString(0), reader.GetString(1)));
                }
            }
        }

        columns.Should().NotBeEmpty("the scan must find the tables that hold candidate and application ids");
        var hits = new List<string>();
        foreach (var (table, column) in columns)
        {
            var ids = column == "candidate_id" ? candidateIds : applicationIds;
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM public.\"{table}\" WHERE \"{column}\" = ANY(@ids)", connection);
            command.Parameters.AddWithValue("ids", ids.ToArray());
            if ((long)(await command.ExecuteScalarAsync(Ct))! > 0)
            {
                hits.Add($"{table}.{column}");
            }
        }

        return hits;
    }

    private static async Task<NpgsqlConnection> OwnerAsync(AppHostFixture fixture)
    {
        var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        return connection;
    }

    private static async Task<HashSet<string>> BaseTablesAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var tables = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(Ct))
        {
            tables.Add(reader.GetString(0));
        }

        tables.Should().NotBeEmpty();
        return tables;
    }
}
