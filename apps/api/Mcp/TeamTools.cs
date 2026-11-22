using System.ComponentModel;
using ModelContextProtocol.Server;
using ScreenHire.Api.Hiring;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.Api.Mcp;

/// <summary>
/// The organization's members, so an assistant can name a person: the assignee of a task, the reviewer asked for a
/// scorecard, an interviewer. Names and roles only, from the read the hiring routes use (<see cref="HiringTeams"/>): no
/// email addresses.
/// </summary>
[McpServerToolType]
internal sealed class TeamTools(ScreenHireDbContext db, ITenantContext tenant)
{
    [McpServerTool(Name = "list_team_members", Title = "List team members", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List the organization's members by name, with their user id and role. The user ids are what create_task, request_scorecard and "
        + "invite_to_schedule take. seesAllJobs is false for someone who sees only the jobs whose hiring team they are on.")]
    public async Task<McpTeam> ListTeamMembers(CancellationToken cancellationToken = default) =>
        new([.. (await HiringTeams.MembersAsync(db, tenant, cancellationToken)).Values
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(m => new McpTeamMember(m.UserId, m.DisplayName, m.Role, m.SeesAllJobs))]);
}
