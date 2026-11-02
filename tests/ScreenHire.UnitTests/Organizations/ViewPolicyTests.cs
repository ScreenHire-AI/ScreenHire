using System.Text.RegularExpressions;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Views;

namespace ScreenHire.UnitTests.Organizations;

/// <summary>Who sees and changes a saved view, and what a view keeps (docs/architecture/saved-views.md).</summary>
public sealed partial class ViewPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organization = Guid.CreateVersion7();
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Someone = Guid.CreateVersion7();

    private static SavedFilterView View(bool shared = false) =>
        SavedFilterView.Create(Organization, Owner, "Interviewing", ViewScope.Candidates, jobId: null, shared, new ViewDefinition(), Now);

    [Fact]
    public void The_owner_changes_their_own_view_whatever_their_role_allows()
    {
        ViewPolicy.MayChange(View(), Owner, new HashSet<Permission>()).Should().BeTrue();
        ViewPolicy.MayChange(View(shared: true), Owner, permissions: null).Should().BeTrue();
    }

    [Fact]
    public void Someone_else_needs_Edit_anyones_views()
    {
        var view = View(shared: true);

        ViewPolicy.MayChange(view, Someone, new HashSet<Permission> { Permission.ViewHiring }).Should().BeFalse();
        ViewPolicy.MayChange(view, Someone, permissions: null).Should().BeFalse("an API key has no role");
        ViewPolicy.MayChange(view, Someone, new HashSet<Permission> { Permission.EditAnyView }).Should().BeTrue();
        ViewPolicy.MayChange(view, userId: null, new HashSet<Permission> { Permission.EditAnyView }).Should().BeFalse();
    }

    [Fact]
    public void Owners_and_Admins_edit_anyones_views_and_Recruiters_and_Viewers_do_not()
    {
        RolePermissions.Of(OrganizationRole.Owner).Should().Contain(Permission.EditAnyView);
        RolePermissions.Of(OrganizationRole.Admin).Should().Contain(Permission.EditAnyView);
        RolePermissions.Of(OrganizationRole.Recruiter).Should().NotContain(Permission.EditAnyView);
        RolePermissions.Of(OrganizationRole.Viewer).Should().NotContain(Permission.EditAnyView);
    }

    [Fact]
    public void A_private_view_is_seen_by_its_owner_only_and_a_shared_one_by_everyone()
    {
        var none = new HashSet<Guid>();

        ViewPolicy.MaySee(View(), Owner, none).Should().BeTrue();
        ViewPolicy.MaySee(View(), Someone, none).Should().BeFalse();
        ViewPolicy.MaySee(View(), userId: null, none).Should().BeFalse();
        ViewPolicy.MaySee(View(shared: true), Someone, none).Should().BeTrue();
    }

    [Fact]
    public void A_shared_view_that_picks_jobs_shows_only_to_someone_who_sees_every_one_of_them()
    {
        var seen = Guid.CreateVersion7();
        var unseen = Guid.CreateVersion7();
        var view = SavedFilterView.Create(
            Organization, Owner, "Two jobs", ViewScope.Candidates, jobId: null, shared: true, new ViewDefinition { JobIds = [seen, unseen] }, Now);

        ViewPolicy.MaySee(view, Someone, new HashSet<Guid> { seen }).Should().BeFalse("the view carries the id of a job they cannot see");
        ViewPolicy.MaySee(view, Someone, new HashSet<Guid> { seen, unseen }).Should().BeTrue();
        ViewPolicy.MaySee(view, Owner, new HashSet<Guid>()).Should().BeTrue("a person keeps their own view after leaving a job's team");
    }

    [Fact]
    public void A_job_view_names_its_job_and_a_candidates_view_takes_none()
    {
        var job = Guid.CreateVersion7();

        SavedFilterView.Create(Organization, Owner, "Screening", ViewScope.Job, job, shared: false, new ViewDefinition(), Now).JobId.Should().Be(job);
        FluentActions.Invoking(() => SavedFilterView.Create(Organization, Owner, "x", ViewScope.Job, null, false, new ViewDefinition(), Now))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SavedFilterView.Create(Organization, Owner, "x", ViewScope.Candidates, job, false, new ViewDefinition(), Now))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_share_delete_and_restore_report_what_changed_and_stamp_the_time()
    {
        var view = View();
        var later = Now.AddMinutes(5);

        view.Rename("Interviewing", later).Should().BeFalse("the same name changes nothing");
        view.UpdatedAt.Should().Be(Now);
        view.Rename("Interviewing now", later).Should().BeTrue();
        view.SetShared(true, later).Should().BeTrue();
        view.SetShared(true, later).Should().BeFalse();
        view.UpdatedAt.Should().Be(later);

        view.Delete(later);
        view.Delete(later.AddMinutes(1));
        view.DeletedAt.Should().Be(later, "a second delete keeps the first time");
        view.Restore(later.AddMinutes(2)).Should().BeTrue();
        view.DeletedAt.Should().BeNull();
        view.Restore(later.AddMinutes(3)).Should().BeFalse("a view that is not deleted has nothing to restore");
    }

    [Fact]
    public void Undo_has_a_minute()
    {
        var view = View();
        view.IsPastUndo(Now.AddDays(1)).Should().BeFalse("a view that is not deleted has no Undo to run out");

        view.Delete(Now);

        view.IsPastUndo(Now + SavedFilterView.RestoreWindow).Should().BeFalse();
        view.IsPastUndo(Now + SavedFilterView.RestoreWindow + TimeSpan.FromSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void Definitions_and_hidden_view_lists_compare_by_what_they_hold()
    {
        var job = Guid.CreateVersion7();

        new ViewDefinition { JobIds = [job], Stages = ["Screening"] }.Should().Be(new ViewDefinition { JobIds = [job], Stages = ["Screening"] });
        new ViewDefinition { Stages = ["Screening"] }.Should().NotBe(new ViewDefinition { Stages = ["Interview"] });
        new ViewPreferences { HiddenBuiltIns = ["overdue"] }.Should().Be(new ViewPreferences { HiddenBuiltIns = ["overdue"] });
        ViewPreferences.Default.Should().NotBe(new ViewPreferences { HiddenBuiltIns = ["overdue"] });
    }

    [Fact]
    public void A_member_keeps_hidden_views_and_reports_whether_they_changed()
    {
        var member = OrganizationMember.Create(Organization, Owner, OrganizationRole.Recruiter, Now);

        member.Views.HiddenBuiltIns.Should().BeEmpty();
        member.SetViews(ViewPreferences.Default).Should().BeFalse();
        member.SetViews(new ViewPreferences { HiddenBuiltIns = ["overdue"] }).Should().BeTrue();
        member.SetViews(new ViewPreferences { HiddenBuiltIns = ["overdue"] }).Should().BeFalse();
    }

    [Fact]
    public void Only_known_built_in_views_exist()
    {
        BuiltInViews.Exists("needs-review").Should().BeTrue();
        BuiltInViews.Exists("made-up").Should().BeFalse();
        BuiltInViews.Ids.Should().OnlyHaveUniqueItems().And.HaveCount(6);
    }

    [Fact]
    public void The_server_knows_exactly_the_dashboards_built_in_view_ids()
    {
        var source = File.ReadAllText(RepositoryFiles.PathTo("apps", "dashboard", "src", "lib", "saved-views.ts"));

        BuiltInIds().Matches(source).Select(m => m.Groups[1].Value).Should().Equal(BuiltInViews.Ids);
    }

    [GeneratedRegex(@"^\s+id: '([a-z-]+)',$", RegexOptions.Multiline)]
    private static partial Regex BuiltInIds();
}
