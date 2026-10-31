using ScreenHire.Domain.History;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.History;

/// <summary>
/// The Team page's history lines (change-history.md): who joined and with which role, each change of a member's role or
/// job access, removals, and invitations sent and revoked. A member is named by id, never by a stored name.
/// </summary>
public sealed class TeamChangesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organization = Guid.CreateVersion7();
    private static readonly Guid Sam = Guid.CreateVersion7();

    [Fact]
    public void The_first_member_creates_the_team_and_a_later_one_joins_it()
    {
        var owner = OrganizationMember.Create(Organization, Sam, OrganizationRole.Owner, Now);
        var viewer = OrganizationMember.Create(Organization, Sam, OrganizationRole.Viewer, Now, jobAccess: JobAccess.HiringTeamOnly);

        var created = TeamChanges.Member(null, owner, null, "Owner", withOrganization: true).Single();
        created.Sentence().Should().Be("created the team");
        created.Details.Should().Equal(new ChangeDetail("Role", null, "Owner"));

        var joined = TeamChanges.Member(null, viewer, null, "Viewer").Single();
        joined.Sentence().Should().Be("joined the team as Viewer");
        joined.Parts.Should().Contain(new ChangeText("Viewer", true));
        joined.Details.Should().Equal(new ChangeDetail("Jobs", null, "Only jobs they're on"));
    }

    [Fact]
    public void A_role_change_names_the_member_by_id_and_says_the_roles()
    {
        var before = OrganizationMember.Create(Organization, Sam, OrganizationRole.Viewer, Now);
        var after = OrganizationMember.Create(Organization, Sam, OrganizationRole.Viewer, Now);
        after.AssignRole(OrganizationRole.Custom, Guid.CreateVersion7());

        var line = TeamChanges.Member(before, after, "Viewer", "Interviewer").Single();

        line.Parts.Should().Equal(new ChangeText("changed "), ChangeText.Person(Sam), new ChangeText("'s role from Viewer to Interviewer"));
        line.Parts[1].Text.Should().Be(ChangeText.Unnamed, "the history never stores a member's name");
        line.Details.Should().Equal(new ChangeDetail("Role", "Viewer", "Interviewer"));
    }

    [Fact]
    public void A_job_access_change_says_which_jobs_before_and_after()
    {
        var before = OrganizationMember.Create(Organization, Sam, OrganizationRole.Recruiter, Now);
        var after = OrganizationMember.Create(Organization, Sam, OrganizationRole.Recruiter, Now);
        after.SetJobAccess(JobAccess.HiringTeamOnly);

        var line = TeamChanges.Member(before, after, "Recruiter", "Recruiter").Single();

        line.Sentence().Should().Be($"changed {ChangeText.Unnamed}'s jobs from All jobs to Only jobs they're on");
        line.Parts[1].UserId.Should().Be(Sam);
        line.Details.Should().Equal(new ChangeDetail("Jobs", "All jobs", "Only jobs they're on"));
        TeamChanges.Member(before, before, "Recruiter", "Recruiter").Should().BeEmpty("nothing changed");
    }

    [Fact]
    public void Removing_a_member_names_them_and_the_role_they_had()
    {
        var member = OrganizationMember.Create(Organization, Sam, OrganizationRole.Admin, Now);

        var line = TeamChanges.Member(member, null, "Admin", null).Single();

        line.Parts.Should().Equal(new ChangeText("removed "), ChangeText.Person(Sam), new ChangeText(" from the team"));
        line.Details.Should().Equal(new ChangeDetail("Role", "Admin", null));
    }

    [Fact]
    public void An_invitation_says_who_it_went_to_and_what_it_offers_and_a_revocation_says_so()
    {
        var sent = new InvitationFacts("sam@example.com", OrganizationRole.Recruiter, null, JobAccess.All, null);
        var revoked = sent with { RevokedAt = Now };

        var invited = TeamChanges.Invitation(null, sent, "Recruiter").Single();
        invited.Sentence().Should().Be("invited sam@example.com as Recruiter");
        invited.Details.Should().Equal(new ChangeDetail("Role", null, "Recruiter"), new ChangeDetail("Jobs", null, "All jobs"));
        TeamChanges.Invitation(sent, revoked, null).Single().Sentence().Should().Be("revoked the invitation to sam@example.com");
        TeamChanges.Invitation(revoked, revoked, null).Should().BeEmpty();
    }

    [Fact]
    public void An_invitations_view_keeps_its_token_out()
    {
        var invitation = new OrganizationInvitation
        {
            OrganizationId = Organization, Email = "sam@example.com", Role = OrganizationRole.Viewer, TokenHash = "0123abcd",
            CreatedAt = Now, ExpiresAt = Now.AddDays(7),
        };

        InvitationFacts.Of(invitation).ToString().Should().NotContain("0123abcd");
    }
}
