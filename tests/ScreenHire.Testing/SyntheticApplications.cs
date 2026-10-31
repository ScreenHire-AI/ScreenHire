using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;

namespace ScreenHire.Testing;

/// <summary>
/// Submits an applicant (<see cref="SyntheticApplicants"/> or one a test builds) the way the careers server does: the
/// resume upload, then the submission with the session context and the candidate's address in <c>X-Forwarded-For</c>,
/// which the API trusts from loopback. The integrity engine then assesses it like any careers application. The one
/// implementation, used by the integration and the end-to-end tests.
/// </summary>
public static class SyntheticApplications
{
    /// <summary>The careers notice version the submissions say the applicant saw (NOTICE_VERSION in apps/careers/src/lib/apply.ts).</summary>
    public const string PrivacyNoticeVersion = "2026-10-31.1";

    /// <summary>
    /// Applies to a published job of the organization with this slug and returns the application's id: the applicant's
    /// resume, then any <paramref name="files"/> for other file questions, then the submission with
    /// <paramref name="answers"/> (none by default: the default workflow asks nothing else). <paramref name="keepForFutureRoles"/>
    /// is the "Keep my details for future roles" tick.
    /// </summary>
    public static async Task<Guid> ApplyAsync(
        this AppHostFixture fixture,
        string slug,
        Guid jobId,
        SyntheticApplicant applicant,
        CancellationToken cancellationToken,
        bool accommodation = false,
        IReadOnlyList<ApplicationAnswer>? answers = null,
        IReadOnlyList<(string QuestionId, string FileName, byte[] Content)>? files = null,
        bool keepForFutureRoles = false)
    {
        using var client = fixture.CreateHttpClient("api");
        var prefix = $"/v1/public/organizations/{slug}/jobs/{jobId}";
        var submitted = new JsonArray(
            Answer("resume", (await UploadAsync(client, prefix, "resume", applicant.ResumeFileName, applicant.ResumeBytes(), cancellationToken)).ToString()));
        foreach (var (questionId, fileName, content) in files ?? [])
        {
            submitted.Add(Answer(questionId, (await UploadAsync(client, prefix, questionId, fileName, content, cancellationToken)).ToString()));
        }

        foreach (var answer in answers ?? [])
        {
            submitted.Add(Answer(answer.QuestionId, answer.Value!));
        }

        var body = new JsonObject
        {
            ["candidate"] = new JsonObject
            {
                ["firstName"] = applicant.FirstName, ["lastName"] = applicant.LastName, ["email"] = applicant.Email, ["phone"] = applicant.Phone,
            },
            ["privacyNoticeVersion"] = PrivacyNoticeVersion,
            ["answers"] = submitted,
            // The default workflow wants AI use disclosed, so an applicant who did not say is sent as "did not use".
            ["aiUseDeclaration"] = (applicant.AiUseDeclaration == AiUseDeclaration.NotProvided ? AiUseDeclaration.DidNotUse : applicant.AiUseDeclaration).ToString(),
            ["aiUseDetails"] = applicant.AiUseDetails,
            ["honeypotFilled"] = applicant.HoneypotFilled,
            ["accommodationRequested"] = accommodation,
            ["userAgent"] = applicant.UserAgent,
            ["pageTimes"] = new JsonArray([.. applicant.PageTimes().Select(p => (JsonNode)new JsonObject { ["page"] = p.Page, ["seconds"] = p.Seconds })]),
            ["automationMarkers"] = new JsonArray([.. applicant.AutomationMarkers.Select(m => JsonValue.Create(m))]),
            ["deviceFingerprint"] = applicant.DeviceFingerprint,
            ["keepForFutureRoles"] = keepForFutureRoles,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{prefix}/applications") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Forwarded-For", applicant.IpAddress);
        using var response = await client.SendAsync(request, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("applicationId").GetGuid();
    }

    private static JsonObject Answer(string questionId, string value) => new() { ["questionId"] = questionId, ["value"] = value };

    private static async Task<Guid> UploadAsync(
        HttpClient client, string prefix, string questionId, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var ticketResponse = await client.PostAsJsonAsync($"{prefix}/questions/{questionId}/uploads", new { fileName, sizeBytes = content.Length }, cancellationToken);
        ticketResponse.StatusCode.Should().Be(HttpStatusCode.OK, await ticketResponse.Content.ReadAsStringAsync(cancellationToken));
        var ticket = await ticketResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using (var storage = new HttpClient())
        using (var put = new HttpRequestMessage(HttpMethod.Put, ticket.GetProperty("uploadUrl").GetString()) { Content = new ByteArrayContent(content) })
        {
            foreach (var header in ticket.GetProperty("headers").EnumerateObject())
            {
                put.Headers.Add(header.Name, header.Value.GetString());
            }

            (await storage.SendAsync(put, cancellationToken)).IsSuccessStatusCode.Should().BeTrue();
        }

        var uploadId = ticket.GetProperty("uploadId").GetGuid();
        (await client.PostAsync($"{prefix}/questions/{questionId}/uploads/{uploadId}/verify", null, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        return uploadId;
    }
}
