using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.IntegrationTests.Messaging;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// One email a person sent to a candidate, once it is on the application's timeline: the candidate's address, the reply
/// address from the Mailpit copy's Reply-To, and the ids a webhook names.
/// </summary>
internal sealed record SentToCandidate(
    Guid JobId, Guid ApplicationId, Guid CandidateId, string Address, Guid SendId, MailpitMessage Mail)
{
    /// <summary>The address the candidate's answer goes to: the email's Reply-To.</summary>
    public string ReplyAddress => Mail.ReplyTo!.Address;
}

/// <summary>
/// Helpers for the reply, bounce and sending-domain tests: sending a candidate an email as a person does, waiting for the
/// worker to commit it (a reply is accepted only for an email that went), and posting as the email provider does.
/// </summary>
internal static class ReplySupport
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Creates a job and a candidate, has <paramref name="owner"/> email them, and waits until the email has been sent and recorded.</summary>
    public static async Task<SentToCandidate> SendToNewCandidateAsync(
        this AppHostFixture fixture, Tenant owner, string fullName = "Jordan Reyes", string subject = "Next steps", Guid? jobId = null)
    {
        var job = jobId ?? (await owner.CreateJobAsync(Samples.RemoteJob($"Role {Guid.NewGuid():N}"))).Id();
        var address = ApiSession.NewEmail("candidate");
        var application = (await owner.CreateApplicationAsync(job, address, fullName)).Id();
        return await fixture.SendAsync(owner, job, application, address, subject);
    }

    /// <summary>Has <paramref name="owner"/> email an application's candidate and waits until the email is sent and recorded.</summary>
    public static async Task<SentToCandidate> SendAsync(
        this AppHostFixture fixture, Tenant owner, Guid job, Guid application, string address, string subject)
    {
        var before = (await fixture.Mailpit.MessagesAsync(address, Ct)).Count;
        (await owner.PostAsync("/v1/emails", new JsonObject
        {
            ["subject"] = subject,
            ["body"] = "Hello, can we talk?",
            ["templateId"] = null,
            ["applicationIds"] = new JsonArray(JsonValue.Create(application)),
        })).Expect(HttpStatusCode.Accepted).At("queued").GetInt32().Should().Be(1);

        var mail = (await fixture.Mailpit.WaitForMessagesAsync(address, before + 1, Ct))[0];
        var candidate = (await owner.GetAsync($"/v1/applications/{application}")).Expect(HttpStatusCode.OK).Id("candidate.id");
        var sendId = Guid.Empty;
        // Mailpit has the email just before the worker commits its timeline entry, and a reply is accepted only after it.
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                var entry = (await owner.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
                    .Where(e => e.GetProperty("type").GetString() == "EmailSent" && e.GetProperty("emailSubject").GetString() == subject)
                    .Select(e => (JsonElement?)e).FirstOrDefault();
                sendId = entry is { } found ? found.GetProperty("emailId").GetGuid() : Guid.Empty;
                return sendId != Guid.Empty;
            },
            "the email's timeline entry",
            Ct);
        return new SentToCandidate(job, application, candidate, address, sendId, mail);
    }

    /// <summary>The entries of <c>GET /v1/candidates/{id}/emails</c>.</summary>
    public static async Task<List<JsonElement>> MessagesAsync(this Tenant owner, Guid candidateId) =>
        [.. (await owner.GetAsync($"/v1/candidates/{candidateId}/emails")).Expect(HttpStatusCode.OK).Body.EnumerateArray()];

    public static List<JsonElement> Received(this IEnumerable<JsonElement> messages) =>
        [.. messages.Where(m => m.GetProperty("direction").GetString() == "Received")];

    /// <summary>The candidate as <c>GET /v1/applications/{id}</c> shows them: <c>emailIssue</c> and its reason.</summary>
    public static async Task<string> EmailIssueAsync(this Tenant owner, Guid applicationId) =>
        (await owner.GetAsync($"/v1/applications/{applicationId}")).Expect(HttpStatusCode.OK).Text("candidate.emailIssue");

    /// <summary>Waits for an email to <paramref name="address"/> whose subject is <paramref name="subject"/> and returns it.</summary>
    public static async Task<MailpitMessage> WaitForSubjectAsync(this AppHostFixture fixture, string address, string subject)
    {
        MailpitMessage? found = null;
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                found = (await fixture.Mailpit.MessagesAsync(address, Ct)).FirstOrDefault(m => m.Subject == subject);
                return found is not null;
            },
            $"an email to the test address with the subject '{subject}'",
            Ct);
        return found!;
    }

    public static async Task<string> EmailOfAsync(this Tenant owner) =>
        (await owner.Session.CurrentUserAsync(Ct))!.Value.GetProperty("email").GetString()!;

    /// <summary>A client with no cookies, as SendGrid is: it signs in with nothing but the credentials it is given.</summary>
    public static HttpClient AnonymousClient(this AppHostFixture fixture) =>
        new(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false }) { BaseAddress = fixture.GetEndpoint("api") };

    /// <summary>Delivers a reply through the sandbox's dev route and returns its outcome: Stored, Duplicate or Ignored.</summary>
    public static async Task<string> DevReplyAsync(
        this AppHostFixture fixture, string to, string from, string text, string? messageId = null, bool autoReply = false,
        string? html = null, string subject = "Re: Next steps", params (string FileName, byte[] Content)[] attachments)
    {
        using var client = fixture.AnonymousClient();
        var body = new JsonObject
        {
            ["to"] = to,
            ["from"] = from,
            ["fromName"] = "Jordan Reyes",
            ["subject"] = subject,
            ["text"] = text,
            ["html"] = html,
            ["messageId"] = messageId,
            ["autoReply"] = autoReply,
            ["attachments"] = new JsonArray([.. attachments.Select(a => (JsonNode)new JsonObject
            {
                ["fileName"] = a.FileName,
                ["contentBase64"] = Convert.ToBase64String(a.Content),
            })]),
        };
        using var response = await client.PostAsync("/v1/dev/inbound-email", JsonBody(body), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.GetProperty("outcome").GetString()!;
    }

    private static StringContent JsonBody(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    /// <summary>
    /// Posts a reply the way SendGrid's Inbound Parse does: multipart form fields, the envelope naming the recipient, and a
    /// file per attachment. <paramref name="password"/> null sends the stack's inbound secret; <paramref name="authorize"/>
    /// false sends no credentials.
    /// </summary>
    public static async Task<HttpStatusCode> PostInboundAsync(
        this AppHostFixture fixture,
        string envelopeTo,
        string from,
        string text,
        string? toHeader = null,
        string? headers = null,
        string? html = null,
        string subject = "Re: Next steps",
        string? password = null,
        bool authorize = true,
        params (string FileName, byte[] Content)[] attachments)
    {
        using var client = fixture.AnonymousClient();
        if (authorize)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"sendgrid:{password ?? EmailEvents.InboundSecret}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        using var form = new MultipartFormDataContent
        {
            { new StringContent(new JsonObject { ["to"] = new JsonArray(envelopeTo), ["from"] = from }.ToJsonString()), "envelope" },
            { new StringContent(from), "from" },
            { new StringContent(toHeader ?? envelopeTo), "to" },
            { new StringContent(subject), "subject" },
            { new StringContent(text), "text" },
            { new StringContent(headers ?? ""), "headers" },
            { new StringContent(attachments.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)), "attachments" },
        };
        if (html is not null)
        {
            form.Add(new StringContent(html), "html");
        }

        for (var i = 0; i < attachments.Length; i++)
        {
            var file = new ByteArrayContent(attachments[i].Content);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, $"attachment{i + 1}", attachments[i].FileName);
        }

        using var response = await client.PostAsync(EmailWebhookPaths.Inbound, form, Ct);
        return response.StatusCode;
    }

    /// <summary>
    /// Posts SendGrid events signed as of the real clock: the API checks the timestamp against its own, within five
    /// minutes, so a fixed time would be refused. The one place a test reads the clock.
    /// </summary>
    public static async Task<HttpStatusCode> PostEventsAsync(
        this AppHostFixture fixture, string body, bool sign = true, string? signedBody = null, DateTimeOffset? signedAt = null)
    {
        using var client = fixture.AnonymousClient();
        using var content = new StringContent(body, new UTF8Encoding(false), "application/json");
        if (sign)
        {
            var (signature, timestamp) = EmailEvents.Sign(signedBody ?? body, signedAt ?? DateTimeOffset.UtcNow);
            content.Headers.Add(EmailEvents.SignatureHeader, signature);
            content.Headers.Add(EmailEvents.TimestampHeader, timestamp);
        }

        using var response = await client.PostAsync(EmailWebhookPaths.Events, content, Ct);
        return response.StatusCode;
    }

    /// <summary>An event batch with one event about <paramref name="sent"/>'s email, as SendGrid sends it with our custom arguments.</summary>
    public static string EventBody(
        Guid organizationId, Guid sendId, string address, string kind, string? type = null, string? reason = null) =>
        new JsonArray(new JsonObject
        {
            ["event"] = kind,
            ["email"] = address,
            ["type"] = type,
            ["reason"] = reason,
            ["organizationId"] = organizationId.ToString("D"),
            ["emailId"] = sendId.ToString("D"),
        }).ToJsonString();

    /// <summary>A small file that starts like a PDF.</summary>
    public static byte[] Pdf(int extraBytes = 1024) => [.. "%PDF-1.7\n"u8.ToArray(), .. new byte[extraBytes]];
}

internal static class EmailWebhookPaths
{
    public const string Inbound = "/v1/webhooks/email/inbound";
    public const string Events = "/v1/webhooks/email/events";
}
