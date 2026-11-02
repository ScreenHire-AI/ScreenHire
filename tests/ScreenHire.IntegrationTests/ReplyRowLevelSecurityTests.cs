using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The tables behind candidate replies and sending domains in the database itself, as the <c>screenhire_app</c> role the API
/// and worker use: another organization sees none of their rows, even with the query filters off. The catalogue checks in
/// <c>RowLevelSecurityTests</c> and <c>JobAccessTests</c> cover that every such table has its policies; this proves the
/// policies hold for these three.
/// </summary>
public sealed class ReplyRowLevelSecurityTests(AppHostFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Replies_their_attachments_and_sending_domains_stay_with_their_organization()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var other = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var job = TestData.NewJob(organization, "replied-to");
        var candidate = Candidate.Create(organization.Id, "Casey", "Morgan", "casey.morgan@example.com", null, Now);
        var (application, submitted) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Now, null);
        var copy = application.PrepareEmail(Guid.CreateVersion7(), null, candidate.Email, "A question", "Hi Casey,", "<p>Hi Casey,</p>", Now);
        var reply = new InboundEmail
        {
            OrganizationId = organization.Id,
            ApplicationId = application.Id,
            SendId = copy.Id,
            FromAddress = candidate.Email,
            Subject = "Re: A question",
            TextBody = "Yes.",
            HtmlBody = "",
            ReceivedAt = Now,
        };
        var attachment = new InboundEmailAttachment
        {
            OrganizationId = organization.Id,
            ApplicationId = application.Id,
            InboundEmailId = reply.Id,
            BlobName = $"{organization.Id}/{application.Id}/{reply.Id}/cv.pdf",
            FileName = "cv.pdf",
            ContentType = "application/pdf",
            SizeBytes = 10,
            Sha256 = new string('0', 64),
        };
        var domain = new SendingDomain { OrganizationId = organization.Id, Domain = $"rls-{Guid.NewGuid():N}.example.com", CreatedAt = Now };
        await using (var db = await ContextAsync(organization.Id))
        {
            db.Jobs.Add(job);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);
            db.ApplicationEvents.AddRange(submitted, application.EmailSent(copy, Now, null), application.EmailReceived(reply));
            db.ApplicationEmails.Add(copy);
            db.InboundEmails.Add(reply);
            db.InboundEmailAttachments.Add(attachment);
            db.SendingDomains.Add(domain);
            await db.SaveChangesAsync(Ct);
        }

        await using (var own = await ContextAsync(organization.Id))
        {
            (await own.InboundEmails.CountAsync(Ct)).Should().Be(1);
            (await own.InboundEmailAttachments.CountAsync(Ct)).Should().Be(1);
            (await own.SendingDomains.CountAsync(Ct)).Should().Be(1);
        }

        await using var outside = await ContextAsync(other.Id);
        (await outside.InboundEmails.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0, "row-level security hides replies even without the query filter");
        (await outside.InboundEmailAttachments.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
        (await outside.SendingDomains.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
    }

    private async Task<ScreenHireDbContext> ContextAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return await fixture.CreateDbContextAsync(tenant, Ct);
    }
}
