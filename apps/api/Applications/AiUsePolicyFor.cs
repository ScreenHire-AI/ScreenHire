using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Infrastructure.Persistence;

namespace ScreenHire.Api.Applications;

/// <summary>
/// The AI-use policy in force for a job: its own override, else the organization's (Verification), else
/// <see cref="AiUsePolicies.Fallback"/>. The apply form and the submit check both ask here, so they agree.
/// </summary>
internal static class AiUsePolicyFor
{
    public static async Task<AiUsePolicy> JobAsync(Job job, ScreenHireDbContext db, CancellationToken cancellationToken)
    {
        if (job.AiUseOverride is { } own)
        {
            return own;
        }

        // The tenant filter and row-level security scope this to the job's organization.
        var organizationDefault = await db.VerificationSettings.AsNoTracking()
            .Select(s => (AiUsePolicy?)s.DefaultAiUse).FirstOrDefaultAsync(cancellationToken);
        return AiUsePolicies.Effective(null, organizationDefault);
    }
}
