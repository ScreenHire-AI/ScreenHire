using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Privacy;
using ScreenHire.Application.Verification;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.IntegrationTests.Realtime;
using static ScreenHire.IntegrationTests.CandidateTestSupport;

namespace ScreenHire.IntegrationTests.DataRequests;

/// <summary>
/// The candidate's side of data requests (docs/architecture/data-requests.md): the emailed link, the choices it opens, the
/// request a choice makes, and the download of a copy. Every request carries an address of its own in
/// <c>X-Forwarded-For</c> (the API believes it from loopback), so the per-address limit never makes one test fail another.
/// Each test makes its own organization and candidate.
/// </summary>
public sealed partial class PublicDataRequestTests(AppHostFixture fixture)
{
    private const string LinkSubjectStart = "Your link to ask ";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_form_says_it_is_enabled_and_an_unknown_organization_has_none()
    {
        using var world = await ApplicantAsync();
        using var client = NewClient();

        var form = await client.GetFromJsonAsync<JsonElement>(Base(world.Slug), Ct);

        form.GetProperty("enabled").GetBoolean().Should().BeTrue("the stack sends email through Mailpit and has a careers host");
        form.GetProperty("organizationName").GetString().Should().NotBeNullOrWhiteSpace();
        form.GetProperty("turnstileSiteKey").ValueKind.Should().Be(JsonValueKind.Null, "the bot challenge is not configured in the test stack");
        (await client.GetAsync(Base("no-such-organization"), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_known_and_an_unknown_address_get_the_same_answer_and_only_the_known_one_gets_a_link_that_opens_the_choices()
    {
        using var world = await ApplicantAsync();
        using var client = NewClient();
        var stranger = ApiSession.NewEmail("stranger");

        var known = await RequestLinkAsync(client, world.Slug, world.Email);
        var unknown = await RequestLinkAsync(client, world.Slug, stranger);

        known.StatusCode.Should().Be(HttpStatusCode.Accepted);
        unknown.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await known.Content.ReadAsStringAsync(Ct)).Should().BeEmpty().And.Be(await unknown.Content.ReadAsStringAsync(Ct));
        known.Content.Headers.ContentLength.Should().Be(unknown.Content.Headers.ContentLength);

        var mail = (await WaitForLinkMailsAsync(world.Email, 1)).Single();
        mail.Subject.Should().Be($"Your link to ask {world.OrganizationName} about your data");
        mail.Text.Should().Contain("If you didn't ask for this you can ignore this email.").And.Contain("It works for 1 hour.");
        var token = TokenIn(mail.Text);
        (await fixture.Mailpit.MessagesAsync(stranger, Ct)).Should().BeEmpty("an address that never applied gets nothing");

        var choices = await client.GetFromJsonAsync<JsonElement>($"{Base(world.Slug)}/{token}", Ct);
        choices.GetProperty("organizationName").GetString().Should().Be(world.OrganizationName);
    }

    [Fact]
    public async Task The_address_is_matched_whatever_its_case_and_a_bad_address_is_a_validation_error_that_sends_nothing()
    {
        using var world = await ApplicantAsync();
        using var client = NewClient();

        (await RequestLinkAsync(client, world.Slug, world.Email.ToUpperInvariant())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await RequestLinkAsync(client, world.Slug, "not an address")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RequestLinkAsync(client, world.Slug, "")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RequestLinkAsync(client, "no-such-organization", world.Email)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await WaitForLinkMailsAsync(world.Email, 1);
    }

    [Fact]
    public async Task A_second_request_inside_the_cooldown_sends_nothing_and_one_after_it_sends_again()
    {
        using var world = await ApplicantAsync();
        using var client = NewClient();

        (await RequestLinkAsync(client, world.Slug, world.Email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var claimed = await LinkSentAtAsync(world.CandidateId);
        claimed.Should().NotBeNull("the first request claimed the candidate's cooldown");
        var second = await RequestLinkAsync(client, world.Slug, world.Email);

        second.StatusCode.Should().Be(HttpStatusCode.Accepted, "the answer is the same inside the cooldown");
        (await LinkSentAtAsync(world.CandidateId)).Should().Be(claimed, "the second request did not claim anything");
        await WaitForLinkMailsAsync(world.Email, 1);
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        (await LinkMailsAsync(world.Email)).Should().ContainSingle("a second email would have been queued by now");

        // Once the cooldown has passed (the claim moved back in time), the next request sends again.
        await OwnerSqlAsync(
            "UPDATE candidates SET data_request_link_sent_at = now() - interval '10 minutes' WHERE id = @id", ("id", world.CandidateId));
        (await RequestLinkAsync(client, world.Slug, world.Email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await WaitForLinkMailsAsync(world.Email, 2)).Select(m => TokenIn(m.Text)).Distinct().Should().HaveCount(2, "each email carries a token of its own");
    }

    [Fact]
    public async Task A_forged_token_another_organizations_token_and_an_erased_candidates_token_all_find_nothing()
    {
        using var world = await ApplicantAsync();
        using var other = await ApplicantAsync();
        using var client = NewClient();
        var token = await TokenForAsync(client, world);
        var otherToken = await TokenForAsync(client, other);

        foreach (var path in new[] { $"{Base(world.Slug)}/CfDJ8forged-token-that-reads-as-nothing", $"{Base(world.Slug)}/{otherToken}" })
        {
            (await client.GetAsync(path, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, path);
            (await client.PostAsJsonAsync(path, new { kind = "Copy" }, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, path);
        }

        (await client.GetAsync($"{Base(world.Slug)}/{token}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await world.Owner.PostAsync($"/v1/candidates/{world.CandidateId}/erase", new JsonObject { ["confirmation"] = world.Name }))
            .Expect(HttpStatusCode.NoContent);

        (await client.GetAsync($"{Base(world.Slug)}/{token}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, "the candidate is gone");
        (await client.PostAsJsonAsync($"{Base(world.Slug)}/{token}", new { kind = "Erasure" }, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RequestLinkAsync(client, world.Slug, world.Email)).StatusCode.Should().Be(HttpStatusCode.Accepted, "an erased address looks like any other");
        (await OwnerCountAsync("SELECT count(*) FROM data_requests WHERE organization_id = @id", world.OrganizationId)).Should().Be(0);
    }

    [Fact]
    public async Task An_expired_token_finds_nothing_like_a_forged_one()
    {
        using var world = await ApplicantAsync();
        using var client = NewClient();
        await TokenForAsync(client, world); // by now the stack has a key ring the test can read
        var longAgo = LinksFor(new FixedTime(DateTimeOffset.UtcNow.AddDays(-30)));
        var current = LinksFor(TimeProvider.System);
        var expired = longAgo.IssueLink(world.OrganizationId, world.CandidateId);
        var fresh = current.IssueLink(world.OrganizationId, world.CandidateId);

        (await client.GetAsync($"{Base(world.Slug)}/{fresh}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK, "the helper signs with the stack's key ring");
        (await client.GetAsync($"{Base(world.Slug)}/{expired}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"{Base(world.Slug)}/{expired}", new { kind = "Copy" }, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Copy")]
    [InlineData("Erasure")]
    public async Task A_choice_makes_one_request_and_a_repeat_returns_it_with_a_log_entry_that_names_no_one(string kind)
    {
        using var world = await ApplicantAsync();
        using var client = NewClient();
        var token = await TokenForAsync(client, world);

        var first = await client.PostAsJsonAsync($"{Base(world.Slug)}/{token}", new { kind }, Ct);
        var again = await client.PostAsJsonAsync($"{Base(world.Slug)}/{token}", new { kind }, Ct);

        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Ct));
        var receipt = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = receipt.GetProperty("id").GetGuid();
        receipt.GetProperty("kind").GetString().Should().Be(kind);
        receipt.GetProperty("receivedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        receipt.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["id", "kind", "receivedAt"], "the due date is the team's target, not a promise");
        (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid().Should().Be(id, "a repeat returns the request that is open");

        (await OwnerCountAsync("SELECT count(*) FROM data_requests WHERE organization_id = @id", world.OrganizationId)).Should().Be(1);
        await using var owner = await OwnerAsync();
        await using var command = new NpgsqlCommand(
            "SELECT status, kind, due_at - received_at, candidate_id FROM data_requests WHERE id = @id", owner);
        command.Parameters.AddWithValue("id", id);
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            (await reader.ReadAsync(Ct)).Should().BeTrue();
            reader.GetString(0).Should().Be("Open");
            reader.GetString(1).Should().Be(kind);
            reader.GetFieldValue<TimeSpan>(2).Should().Be(TimeSpan.FromDays(new DataRequestOptions().DueDays));
            reader.GetGuid(3).Should().Be(world.CandidateId);
        }

        var log = await RowsAsJsonAsync("SELECT row_to_json(m)::text FROM membership_events m WHERE data_request_id = @id", id);
        log.Should().ContainSingle("the repeat added no second entry");
        var entry = JsonDocument.Parse(log[0]).RootElement;
        entry.GetProperty("kind").GetString().Should().Be("DataRequestReceived");
        entry.GetProperty("actor_user_id").ValueKind.Should().Be(JsonValueKind.Null, "the candidate asked, not a member");
        log[0].Should().NotContain(world.Email).And.NotContain(world.Name).And.NotContain(world.CandidateId.ToString());
    }

    [Fact]
    public async Task A_copy_and_an_erasure_are_separate_requests_and_a_bad_kind_is_refused()
    {
        using var world = await ApplicantAsync();
        using var client = NewClient();
        var token = await TokenForAsync(client, world);

        var copy = await (await client.PostAsJsonAsync($"{Base(world.Slug)}/{token}", new { kind = "Copy" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var erasure = await (await client.PostAsJsonAsync($"{Base(world.Slug)}/{token}", new { kind = "Erasure" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var bad = await client.PostAsJsonAsync($"{Base(world.Slug)}/{token}", new { kind = "Everything" }, Ct);

        copy.GetProperty("id").GetGuid().Should().NotBe(erasure.GetProperty("id").GetGuid());
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await OwnerCountAsync("SELECT count(*) FROM data_requests WHERE organization_id = @id", world.OrganizationId)).Should().Be(2);
    }

    [Fact]
    public async Task Simultaneous_clicks_make_one_request_and_all_get_it()
    {
        using var world = await ApplicantAsync();
        using var client = NewClient();
        var token = await TokenForAsync(client, world);

        var answers = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => client.PostAsJsonAsync($"{Base(world.Slug)}/{token}", new { kind = "Erasure" }, Ct)));

        answers.Should().OnlyContain(a => a.StatusCode == HttpStatusCode.OK);
        var ids = new HashSet<Guid>();
        foreach (var answer in answers)
        {
            ids.Add((await answer.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid());
        }

        ids.Should().ContainSingle();
        (await OwnerCountAsync("SELECT count(*) FROM data_requests WHERE organization_id = @id", world.OrganizationId)).Should().Be(1);
        (await OwnerCountAsync(
            "SELECT count(*) FROM membership_events WHERE organization_id = @id AND kind = 'DataRequestReceived'", world.OrganizationId))
            .Should().Be(1, "only the request that was saved leaves an entry");
    }

    [Fact]
    public async Task A_request_reaches_its_own_organizations_dashboards_only()
    {
        using var world = await ApplicantAsync();
        using var elsewhere = await fixture.SignedInWithOrganizationAsync();
        await using var ours = await EventsConnection.ConnectAsync(world.Owner.Session, Ct);
        await using var theirs = await EventsConnection.ConnectAsync(elsewhere.Session, Ct);
        using var client = NewClient();
        var token = await TokenForAsync(client, world);

        var receipt = await (await client.PostAsJsonAsync($"{Base(world.Slug)}/{token}", new { kind = "Copy" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = receipt.GetProperty("id").GetGuid();

        var received = await ours.WaitForAsync("DataRequestReceived", "requestId", id, Ct);
        received.GetProperty("kind").GetString().Should().Be("Copy");
        received.GetProperty("organizationId").GetGuid().Should().Be(world.OrganizationId);
        theirs.ReceivedAnythingAbout(id).Should().BeFalse("another organization hears nothing about our candidates' requests");
    }

    [Fact]
    public async Task A_copy_downloads_through_a_short_link_until_it_expires_and_only_for_the_right_organization()
    {
        using var world = await ApplicantAsync();
        using var other = await ApplicantAsync();
        using var client = NewClient();
        var token = await TokenForAsync(client, world);
        var receipt = await (await client.PostAsJsonAsync($"{Base(world.Slug)}/{token}", new { kind = "Copy" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var requestId = receipt.GetProperty("id").GetGuid();
        var links = LinksFor(TimeProvider.System);
        var download = links.IssueDownload(world.OrganizationId, requestId);
        var path = $"{Base(world.Slug)}/downloads/{download}";

        (await client.GetAsync(path, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, "the copy is not built yet");

        // What the worker does when the copy is built, arranged directly: the blob, then the request pointing at it.
        var blobName = $"{world.OrganizationId}/{requestId}.json";
        var container = new BlobServiceClient(await fixture.App.GetConnectionStringAsync("blobs", Ct) ?? throw new InvalidOperationException("No blob connection."))
            .GetBlobContainerClient(DataRequestOptions.DefaultExportContainer);
        await container.CreateIfNotExistsAsync(cancellationToken: Ct);
        await container.GetBlobClient(blobName).UploadAsync(BinaryData.FromString("""{"candidate":{}}"""), overwrite: true, Ct);
        await OwnerSqlAsync(
            "UPDATE data_requests SET status = 'Completed', resolved_at = now(), copy_sent_at = now(), export_blob_name = @blob, export_expires_at = now() + interval '1 day' WHERE id = @id",
            ("id", requestId), ("blob", blobName));

        var ready = await client.GetFromJsonAsync<JsonElement>(path, Ct);
        using var storage = new HttpClient();
        using var file = await storage.GetAsync(ready.GetProperty("url").GetString(), Ct);
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        file.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        file.Content.Headers.ContentDisposition.FileName.Should().Be("your-data.json");
        (await file.Content.ReadAsStringAsync(Ct)).Should().Be("""{"candidate":{}}""");

        var otherDownload = links.IssueDownload(other.OrganizationId, requestId);
        (await client.GetAsync($"{Base(world.Slug)}/downloads/{otherDownload}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, "the token names another organization");
        (await client.GetAsync($"{Base(other.Slug)}/downloads/{download}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, "the token is not for this organization's page");
        (await client.GetAsync($"{Base(world.Slug)}/downloads/{links.IssueLink(world.OrganizationId, requestId)}", Ct)).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "a link to the choices is not a download link");
        (await client.GetAsync($"{Base(world.Slug)}/downloads/forged", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await OwnerSqlAsync("UPDATE data_requests SET export_expires_at = now() - interval '1 minute' WHERE id = @id", ("id", requestId));
        (await client.GetAsync(path, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, "the copy's window has ended");
    }

    [Fact]
    public async Task Too_many_requests_from_one_address_get_429()
    {
        using var world = await ApplicantAsync();
        using var client = NewClient();
        var limit = new DataRequestOptions().PermitsPerMinute;

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < limit + 3; i++)
        {
            using var response = await client.GetAsync(Base(world.Slug), Ct);
            statuses.Add(response.StatusCode);
        }

        statuses.First().Should().Be(HttpStatusCode.OK);
        statuses.Should().Contain(HttpStatusCode.TooManyRequests, $"{limit} requests a minute is the allowance");
        statuses.Take(limit).Should().OnlyContain(s => s == HttpStatusCode.OK);
        using var elsewhere = NewClient();
        (await elsewhere.GetAsync(Base(world.Slug), Ct)).StatusCode.Should().Be(HttpStatusCode.OK, "other addresses are not slowed");
    }

    private static string Base(string slug) => $"/v1/public/organizations/{slug}/data-requests";

    private HttpClient NewClient()
    {
        var client = fixture.CreateHttpClient("api");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 255)}");
        return client;
    }

    private static Task<HttpResponseMessage> RequestLinkAsync(HttpClient client, string slug, string email) =>
        client.PostAsJsonAsync($"{Base(slug)}/links", new { email }, Ct);

    private async Task<Applicant> ApplicantAsync()
    {
        var owner = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugAsync(owner);
        var organizationName = (await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("name");
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var application = (await owner.CreateApplicationAsync(await OpenJobTitledAsync(owner, "Platform Engineer"), person.Email, person.FullName)).Id();
        return new Applicant(owner, slug, organizationName, person.Email, person.FullName, await CandidateOfAsync(owner, application));
    }

    // Asks for a link as the candidate would and returns the token from the mail that arrives.
    private async Task<string> TokenForAsync(HttpClient client, Applicant world)
    {
        var before = (await LinkMailsAsync(world.Email)).Count;
        await OwnerSqlAsync("UPDATE candidates SET data_request_link_sent_at = NULL WHERE id = @id", ("id", world.CandidateId));
        (await RequestLinkAsync(client, world.Slug, world.Email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var mails = await WaitForLinkMailsAsync(world.Email, before + 1);
        return TokenIn(mails[^1].Text);
    }

    private async Task<IReadOnlyList<MailpitMessage>> LinkMailsAsync(string address) =>
        [.. (await fixture.Mailpit.MessagesAsync(address, Ct)).Where(m => m.Subject.StartsWith(LinkSubjectStart, StringComparison.Ordinal))];

    // Other emails to the address (an application can send some) are not the link mail; wait for this one only.
    private async Task<IReadOnlyList<MailpitMessage>> WaitForLinkMailsAsync(string address, int count)
    {
        IReadOnlyList<MailpitMessage> mails = [];
        await WolverineStore.WaitUntilAsync(
            async () => (mails = await LinkMailsAsync(address)).Count >= count, $"{count} data request link emails to the candidate", Ct);
        return mails;
    }

    private static string TokenIn(string text)
    {
        var match = LinkInMail().Match(text);
        match.Success.Should().BeTrue($"the email carries the link: {text}");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex(@"https?://\S+/data-request/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex LinkInMail();

    // The signer, over the same Postgres key ring the API and the worker use, with a clock of the test's choosing.
    private DataRequestLinks LinksFor(TimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => fixture.CreateDbContextAsync(new TenantContext(), Ct).GetAwaiter().GetResult());
        services.AddDataProtection().SetApplicationName(DataProtectionRegistration.ApplicationName).PersistKeysToDbContext<ScreenHireDbContext>();
        var provider = services.BuildServiceProvider();
        var careers = new CareersSiteOptions { Scheme = "http", CareersDomain = "localhost", CareersHostPattern = "{tenant}.{CareersDomain}" };
        return new DataRequestLinks(
            provider.GetRequiredService<IDataProtectionProvider>(), Options.Create(careers), Options.Create(new DataRequestOptions()), time);
    }

    private async Task<NpgsqlConnection> OwnerAsync()
    {
        var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task OwnerSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var owner = await OwnerAsync();
        await using var command = new NpgsqlCommand(sql, owner);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<long> OwnerCountAsync(string sql, Guid id)
    {
        await using var owner = await OwnerAsync();
        await using var command = new NpgsqlCommand(sql, owner);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<List<string>> RowsAsJsonAsync(string sql, Guid id)
    {
        await using var owner = await OwnerAsync();
        await using var command = new NpgsqlCommand(sql, owner);
        command.Parameters.AddWithValue("id", id);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private async Task<DateTimeOffset?> LinkSentAtAsync(Guid candidateId)
    {
        await using var owner = await OwnerAsync();
        await using var command = new NpgsqlCommand("SELECT data_request_link_sent_at FROM candidates WHERE id = @id", owner);
        command.Parameters.AddWithValue("id", candidateId);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).Should().BeTrue("the candidate exists");
        return reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0);
    }

    private sealed record Applicant(Tenant Owner, string Slug, string OrganizationName, string Email, string Name, Guid CandidateId) : IDisposable
    {
        public Guid OrganizationId => Owner.OrganizationId;

        public void Dispose() => Owner.Dispose();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
