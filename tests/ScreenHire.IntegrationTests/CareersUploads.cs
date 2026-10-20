using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ScreenHire.IntegrationTests;

/// <summary>A candidate's file upload, as the careers apply flow does it: a link from the API, the bytes to storage, a check.</summary>
internal static class CareersUploads
{
    /// <summary>The smallest content that passes the API's PDF signature check.</summary>
    public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Asks for an upload link, sends the bytes the way a browser does, and has the API check them.
    public static async Task<Guid> UploadAsync(HttpClient client, string organizationSlug, Guid jobId, string questionId, string fileName, byte[] content)
    {
        var ticket = await TicketAsync(client, organizationSlug, jobId, questionId, fileName, content.Length);
        await PutAsync(ticket, content);
        var uploadId = ticket.GetProperty("uploadId").GetGuid();
        var verified = await client.PostAsync(
            $"/v1/public/organizations/{organizationSlug}/jobs/{jobId}/questions/{questionId}/uploads/{uploadId}/verify", null, Ct);
        verified.StatusCode.Should().Be(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync(Ct));
        return uploadId;
    }

    public static async Task<JsonElement> TicketAsync(
        HttpClient client, string organizationSlug, Guid jobId, string questionId, string fileName, long sizeBytes)
    {
        var response = await client.PostAsJsonAsync(
            $"/v1/public/organizations/{organizationSlug}/jobs/{jobId}/questions/{questionId}/uploads", new { fileName, sizeBytes }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    public static async Task PutAsync(JsonElement ticket, byte[] content)
    {
        using var response = await SendToStorageAsync(ticket, content);
        response.IsSuccessStatusCode.Should().BeTrue("blob storage took the upload ({0})", response.StatusCode);
    }

    public static async Task<HttpResponseMessage> SendToStorageAsync(JsonElement ticket, byte[] content)
    {
        using var storage = new HttpClient();
        using var request = new HttpRequestMessage(new HttpMethod(ticket.GetProperty("method").GetString()!), ticket.GetProperty("uploadUrl").GetString())
        {
            Content = new ByteArrayContent(content),
        };
        foreach (var header in ticket.GetProperty("headers").EnumerateObject())
        {
            request.Headers.Add(header.Name, header.Value.GetString());
        }

        return await storage.SendAsync(request, Ct);
    }
}
