using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// A recruiter adds a candidate by hand with a resume (<c>POST /v1/jobs/{id}/applications/resume-uploads</c>, then
/// <c>POST /v1/jobs/{id}/applications</c> with <c>resumeUploadId</c>): the file goes straight to storage as a careers
/// upload does, and nothing from a careers form (network or session data, telemetry) is stored.
/// </summary>
public sealed class AddCandidateTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_hand_added_candidate_keeps_their_resume_and_nothing_from_a_careers_form()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        var ticket = await TicketAsync(recruiter, job.Id(), "jordan-lee.pdf", CareersUploads.Pdf.Length);
        await CareersUploads.PutAsync(ticket, CareersUploads.Pdf);

        var created = (await recruiter.PostAsync($"/v1/jobs/{job.Id()}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["firstName"] = "Jordan", ["lastName"] = "Lee", ["email"] = ApiSession.NewEmail("jordan") },
            ["resumeUploadId"] = ticket.GetProperty("uploadId").GetGuid(),
        })).Expect(HttpStatusCode.Created);

        created.Text("source").Should().Be("Recruiter");
        created.Count("files").Should().Be(1);
        created.Text("files.0.questionId").Should().Be("resume");
        created.Text("files.0.fileName").Should().Be("jordan-lee.pdf");
        await using var db = await VerificationSupport.TenantDbAsync(fixture, recruiter.OrganizationId);
        (await db.SubmissionObservations.AnyAsync(o => o.ApplicationId == created.Id(), Ct)).Should().BeFalse();
        (await db.TelemetrySnapshots.AnyAsync(t => t.ApplicationId == created.Id(), Ct)).Should().BeFalse();
        (await db.Applications.SingleAsync(a => a.Id == created.Id(), Ct)).PrivacyNoticeVersion.Should().BeNull();
    }

    [Fact]
    public async Task A_resume_that_is_not_what_it_says_is_refused_and_nothing_is_added()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        byte[] program = [(byte)'M', (byte)'Z', 0x90, 0x00];
        var ticket = await TicketAsync(recruiter, job.Id(), "resume.pdf", program.Length);
        await CareersUploads.PutAsync(ticket, program);

        var refused = await recruiter.PostAsync($"/v1/jobs/{job.Id()}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject { ["firstName"] = "Jordan", ["email"] = ApiSession.NewEmail("jordan") },
            ["resumeUploadId"] = ticket.GetProperty("uploadId").GetGuid(),
        });

        refused.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("resumeUploadId");
        (await recruiter.GetAsync($"/v1/jobs/{job.Id()}/applications")).Expect(HttpStatusCode.OK).Count().Should().Be(0);
    }

    [Fact]
    public async Task A_resume_uploaded_for_one_job_cannot_be_used_on_another_job_or_by_another_organization()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        var other = await recruiter.CreateJobAsync(Samples.RemoteJob("Payroll Specialist"));
        var ticket = await TicketAsync(recruiter, job.Id(), "jordan-lee.pdf", CareersUploads.Pdf.Length);
        await CareersUploads.PutAsync(ticket, CareersUploads.Pdf);
        var uploadId = ticket.GetProperty("uploadId").GetGuid();
        using var stranger = await fixture.SignedInWithOrganizationAsync();
        var strangersJob = await stranger.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));

        // Uploads are keyed by the job's organization and the job, so an id from elsewhere is a file that is not there.
        foreach (var (tenant, jobId) in new[] { (recruiter, other.Id()), (stranger, strangersJob.Id()) })
        {
            var refused = await tenant.PostAsync($"/v1/jobs/{jobId}/applications", new JsonObject
            {
                ["candidate"] = new JsonObject { ["firstName"] = "Jordan", ["email"] = ApiSession.NewEmail("jordan") },
                ["resumeUploadId"] = uploadId,
            });
            refused.Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("resumeUploadId");
            (await tenant.GetAsync($"/v1/jobs/{jobId}/applications")).Expect(HttpStatusCode.OK).Count().Should().Be(0);
        }
    }

    [Fact]
    public async Task An_upload_link_is_refused_for_a_wrong_type_a_closed_job_a_viewer_and_a_job_out_of_sight()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        var closed = await owner.CreateJobAsync(Samples.RemoteJob("Payroll Specialist"));
        await owner.PublishJobAsync(closed.Id());
        await owner.CloseJobAsync(closed.Id());
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        using var narrow = await fixture.InviteAndJoinAsync(owner, "Recruiter", jobAccess: "HiringTeamOnly");

        (await RequestTicketAsync(owner, job.Id(), "resume.exe", 100)).Expect(HttpStatusCode.BadRequest)
            .ProblemKeys().Should().Equal("fileName");
        (await RequestTicketAsync(owner, job.Id(), "resume.pdf", 0)).Expect(HttpStatusCode.BadRequest);
        (await RequestTicketAsync(owner, closed.Id(), "resume.pdf", 100)).Expect(HttpStatusCode.Conflict);
        (await RequestTicketAsync(viewer, job.Id(), "resume.pdf", 100)).Expect(HttpStatusCode.Forbidden);
        (await RequestTicketAsync(narrow, job.Id(), "resume.pdf", 100)).Expect(HttpStatusCode.NotFound);
        (await RequestTicketAsync(owner, Guid.NewGuid(), "resume.pdf", 100)).Expect(HttpStatusCode.NotFound);
    }

    private static Task<ApiResult> RequestTicketAsync(Tenant tenant, Guid jobId, string fileName, long sizeBytes) =>
        tenant.PostAsync($"/v1/jobs/{jobId}/applications/resume-uploads", new JsonObject { ["fileName"] = fileName, ["sizeBytes"] = sizeBytes });

    private static async Task<JsonElement> TicketAsync(Tenant tenant, Guid jobId, string fileName, long sizeBytes) =>
        (await RequestTicketAsync(tenant, jobId, fileName, sizeBytes)).Expect(HttpStatusCode.OK).Body;
}
