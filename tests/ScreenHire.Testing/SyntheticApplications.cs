using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenHire.Application.Integrity;
using ScreenHire.Domain.Applications;

namespace ScreenHire.Testing;

/// <summary>
/// Submits a synthetic applicant (<see cref="SyntheticApplicants"/>) the way the careers server does: the resume upload,
/// then the submission with the session context and the candidate's address in <c>X-Forwarded-For</c>, which the API
/// trusts from loopback. The integrity engine then assesses it like any careers application.
/// </summary>
public static class SyntheticApplications
{
    /// <summary>The careers notice version the submissions say the applicant saw (NOTICE_VERSION in apps/careers/src/lib/apply.ts).</summary>
    public const string PrivacyNoticeVersion = "2026-10-20.2";

    /// <summary>Applies to a published job of the organization with this slug and returns the application's id.</summary>
    public static async Task<Guid> ApplyAsync(
        this AppHostFixture fixture, string slug, Guid jobId, SyntheticApplicant applicant, CancellationToken cancellationToken, bool accommodation = false)
    {
        using var client = fixture.CreateHttpClient("api");
        var prefix = $"/v1/public/organizations/{slug}/jobs/{jobId}";
        var resume = applicant.ResumeBytes();
        var ticketResponse = await client.PostAsJsonAsync(
            $"{prefix}/questions/resume/uploads", new { fileName = applicant.ResumeFileName, sizeBytes = resume.Length }, cancellationToken);
        ticketResponse.StatusCode.Should().Be(HttpStatusCode.OK, await ticketResponse.Content.ReadAsStringAsync(cancellationToken));
        var ticket = await ticketResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using (var storage = new HttpClient())
        using (var put = new HttpRequestMessage(HttpMethod.Put, ticket.GetProperty("uploadUrl").GetString()) { Content = new ByteArrayContent(resume) })
        {
            foreach (var header in ticket.GetProperty("headers").EnumerateObject())
            {
                put.Headers.Add(header.Name, header.Value.GetString());
            }

            (await storage.SendAsync(put, cancellationToken)).IsSuccessStatusCode.Should().BeTrue();
        }

        var uploadId = ticket.GetProperty("uploadId").GetGuid();
        (await client.PostAsync($"{prefix}/questions/resume/uploads/{uploadId}/verify", null, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var body = new JsonObject
        {
            ["candidate"] = new JsonObject { ["fullName"] = applicant.FullName, ["email"] = applicant.Email, ["phone"] = applicant.Phone },
            ["privacyNoticeVersion"] = PrivacyNoticeVersion,
            ["answers"] = new JsonArray(new JsonObject { ["questionId"] = "resume", ["value"] = uploadId.ToString() }),
            // The default workflow wants AI use disclosed, so an applicant who did not say is sent as "did not use".
            ["aiUseDeclaration"] = (applicant.AiUseDeclaration == AiUseDeclaration.NotProvided ? AiUseDeclaration.DidNotUse : applicant.AiUseDeclaration).ToString(),
            ["aiUseDetails"] = applicant.AiUseDetails,
            ["honeypotFilled"] = applicant.HoneypotFilled,
            ["accommodationRequested"] = accommodation,
            ["userAgent"] = applicant.UserAgent,
            ["formStartedAt"] = DateTimeOffset.UtcNow - applicant.TimeToComplete,
            ["automationMarkers"] = new JsonArray([.. applicant.AutomationMarkers.Select(m => JsonValue.Create(m))]),
            ["deviceFingerprint"] = applicant.DeviceFingerprint,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{prefix}/applications") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Forwarded-For", applicant.IpAddress);
        using var response = await client.SendAsync(request, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("applicationId").GetGuid();
    }
}
