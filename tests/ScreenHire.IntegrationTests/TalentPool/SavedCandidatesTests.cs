using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Domain.History;
using ScreenHire.Infrastructure.Tenancy;
using ScreenHire.IntegrationTests.Automations;
using static ScreenHire.IntegrationTests.ScorecardSupport;

namespace ScreenHire.IntegrationTests.TalentPool;

/// <summary>
/// The saved candidates' list (docs/architecture/talent-pool.md): permission without an end date, the rating and the last
/// contact each row carries, the note the team can edit and its History, and saving someone who is already saved. Each
/// test makes its own organization and people.
/// </summary>
public sealed class SavedCandidatesTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_says_when_the_team_saved_a_candidate_from_the_application_it_found()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Searched job");
        var saved = await world.KeptPersonAsync(job, "found-saved");
        var email = ApiSession.NewEmail("found-open");
        await world.ApplyAsync(job, email, keep: true);

        var savedHit = (await world.Owner.GetAsync($"/v1/search?q={Uri.EscapeDataString(saved.Email)}")).Expect(HttpStatusCode.OK).At("candidates")[0];
        var openHit = (await world.Owner.GetAsync($"/v1/search?q={Uri.EscapeDataString(email)}")).Expect(HttpStatusCode.OK).At("candidates")[0];

        savedHit.GetProperty("keptInMindAt").ValueKind.Should().Be(JsonValueKind.String, "the palette shows Saved in place of the stage");
        openHit.GetProperty("keptInMindAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_permission_given_years_ago_still_lets_them_be_saved_and_stay_listed()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Long ago job");
        var alreadySaved = await world.KeptPersonAsync(job, "saved-first");
        var applied = await world.ApplyAsync(job, ApiSession.NewEmail("old-tick"), keep: true);
        await BackdateConsentAsync(world, alreadySaved.ApplicationId, DateTimeOffset.UtcNow.AddYears(-3));
        await BackdateConsentAsync(world, applied, DateTimeOffset.UtcNow.AddYears(-3));

        var result = (await world.KeepAsync([applied])).Expect(HttpStatusCode.OK);

        result.At("kept").GetInt32().Should().Be(1, "a permission has no end date, however old");
        result.At("notAllowed").GetInt32().Should().Be(0);
        var listed = await world.PoolCandidateIdsAsync();
        listed.Should().HaveCount(2).And.Contain(alreadySaved.CandidateId);
        (await world.DetailAsync(alreadySaved.ApplicationId)).At("allowsFutureRoles").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_new_entry_has_the_note_it_was_saved_with_and_a_candidate_saved_without_one_has_none()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Note job");
        var withNote = await world.KeptPersonAsync(job, "with-note", "  Strong portfolio  ");
        var without = await world.KeptPersonAsync(job, "without-note");

        var people = await world.PoolAsync();

        PersonOf(people, withNote.CandidateId).GetProperty("note").GetString().Should().Be("Strong portfolio");
        PersonOf(people, without.CandidateId).GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_list_carries_the_rating_the_candidate_page_shows_and_a_reviewer_with_a_scorecard_to_give_reads_after_your_scorecard()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        using var reviewer = await fixture.InviteAndJoinAsync(world.Owner, "Recruiter");
        var job = await AutomationSamples.JobWithAutomationsAsync(world.Owner, "Reviewed role", new JsonObject
        {
            ["id"] = "ask-review",
            ["name"] = "Ask for a review",
            ["trigger"] = new JsonObject { ["type"] = "StageEntered", ["stageId"] = "interview" },
            ["actions"] = new JsonArray(new JsonObject { ["type"] = "AssignReviewer", ["assigneeUserId"] = reviewer.UserId }),
        });
        await world.Owner.PublishJobAsync(job);
        var (plainJob, _) = await world.OpenJobAsync("Unrated role");
        var rated = await world.ApplyAsync(job, ApiSession.NewEmail("rated"), keep: true);
        (await world.Owner.PostAsync($"/v1/applications/{rated}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        await AutomationSamples.WaitForRunAsync(world.Owner, rated, "ask-review", "Completed");
        (await world.Owner.PostAsync($"/v1/applications/{rated}/scorecards", Submission("interview", "StrongYes", InterviewRatings(4, 5, 5))))
            .Expect(HttpStatusCode.Created);
        var unrated = await world.ApplyAsync(plainJob, ApiSession.NewEmail("unrated"), keep: true);
        (await world.KeepAsync([rated, unrated])).Expect(HttpStatusCode.OK).At("kept").GetInt32().Should().Be(2);
        var ratedCandidate = (await world.DetailAsync(rated)).Id("candidate.id");
        var unratedCandidate = (await world.DetailAsync(unrated)).Id("candidate.id");

        var ownerSees = await world.PoolAsync();
        var reviewerSees = await world.PoolAsync(reviewer);

        var ownersRating = PersonOf(ownerSees, ratedCandidate).GetProperty("rating");
        ownersRating.GetProperty("state").GetString().Should().Be("Rated");
        ownersRating.GetProperty("value").GetDecimal().Should().Be(4.7m, "the mean of the 4, 5 and 5 answers, to one decimal");
        ownersRating.GetProperty("scorecards").GetInt32().Should().Be(1);
        PersonOf(ownerSees, unratedCandidate).GetProperty("rating").GetProperty("state").GetString().Should().Be("NotRated");
        PersonOf(reviewerSees, ratedCandidate).GetProperty("rating").GetProperty("state").GetString()
            .Should().Be("AfterYourScorecard", "the reviewer reads the rating only after their own scorecard");
        PersonOf(reviewerSees, ratedCandidate).GetProperty("rating").Has("value").Should().BeFalse();
        PersonOf(reviewerSees, unratedCandidate).GetProperty("rating").GetProperty("state").GetString().Should().Be("NotRated");
    }

    [Fact]
    public async Task A_reply_from_the_candidate_is_contact_too_and_moves_last_contacted()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (savedFrom, _) = await world.OpenJobAsync("Saved from");
        var email = ApiSession.NewEmail("replier");
        var application = await world.ApplyAsync(savedFrom, email, keep: true);
        (await world.KeepAsync([application])).Expect(HttpStatusCode.OK).At("kept").GetInt32().Should().Be(1);
        var candidate = (await world.DetailAsync(application)).Id("candidate.id");
        var sent = await fixture.SendAsync(world.Owner, savedFrom, application, email, "Next steps");
        var sentAt = (await world.EntryAsync(application, "EmailSent")).GetProperty("occurredAt").GetDateTimeOffset();
        PersonOf(await world.PoolAsync(), candidate).GetProperty("lastContactedAt").GetDateTimeOffset().Should().Be(sentAt);

        (await fixture.DevReplyAsync(sent.ReplyAddress, sent.Address, "Thursday works for me.")).Should().Be("Stored");
        await world.WaitForTimelineAsync(application, "EmailReceived");
        var repliedAt = (await world.EntryAsync(application, "EmailReceived")).GetProperty("occurredAt").GetDateTimeOffset();

        repliedAt.Should().BeAfter(sentAt);
        PersonOf(await world.PoolAsync(), candidate).GetProperty("lastContactedAt").GetDateTimeOffset()
            .Should().Be(repliedAt, "what the candidate wrote counts as contact, and the newest wins");
    }

    [Fact]
    public async Task Last_contacted_is_the_newest_email_over_every_job_the_candidate_applied_for_and_empty_until_one_is_sent()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (savedFrom, _) = await world.OpenJobAsync("Saved from");
        var (other, _) = await world.OpenJobAsync("Applied elsewhere");
        var email = ApiSession.NewEmail("contacted");
        var first = await world.ApplyAsync(savedFrom, email, keep: true);
        var second = await world.ApplyAsync(other, email, keep: true);
        (await world.KeepAsync([first])).Expect(HttpStatusCode.OK).At("kept").GetInt32().Should().Be(1);
        var candidate = (await world.DetailAsync(first)).Id("candidate.id");
        (await world.PoolAsync()).Should().ContainSingle().Which.GetProperty("lastContactedAt").ValueKind
            .Should().Be(JsonValueKind.Null, "no email was sent to them");

        (await world.Owner.PostAsync("/v1/emails", SendReceived(first))).Expect(HttpStatusCode.Accepted).At("queued").GetInt32().Should().Be(1);
        await world.WaitForTimelineAsync(first, "EmailSent");
        var firstSentAt = (await world.EntryAsync(first, "EmailSent")).GetProperty("occurredAt").GetDateTimeOffset();
        PersonOf(await world.PoolAsync(), candidate).GetProperty("lastContactedAt").GetDateTimeOffset().Should().Be(firstSentAt);

        (await world.Owner.PostAsync("/v1/emails", SendReceived(second))).Expect(HttpStatusCode.Accepted).At("queued").GetInt32().Should().Be(1);
        await world.WaitForTimelineAsync(second, "EmailSent");
        var secondSentAt = (await world.EntryAsync(second, "EmailSent")).GetProperty("occurredAt").GetDateTimeOffset();

        secondSentAt.Should().BeAfter(firstSentAt);
        PersonOf(await world.PoolAsync(), candidate).GetProperty("lastContactedAt").GetDateTimeOffset()
            .Should().Be(secondSentAt, "an email for another job the candidate applied for is contact too, and the newest wins");
    }

    [Fact]
    public async Task Editing_the_note_shows_it_in_the_list_records_one_edit_in_History_and_leaves_the_Activity_entry_alone()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Edit note job");
        var person = await world.KeptPersonAsync(job, "edited", "First note");

        (await world.Owner.PutAsync(NotePath(person.CandidateId), new JsonObject { ["note"] = "  Second note  " })).Expect(HttpStatusCode.NoContent);

        var listed = PersonOf(await world.PoolAsync(), person.CandidateId);
        listed.GetProperty("note").GetString().Should().Be("Second note", "the note is trimmed");
        listed.GetProperty("noteUpdated").GetProperty("by").GetString().Should().Be("Member");
        listed.GetProperty("noteUpdated").GetProperty("userId").GetGuid().Should().Be(world.Owner.UserId);
        var history = (await world.Owner.GetAsync(HistoryPath(person.CandidateId))).Expect(HttpStatusCode.OK);
        history.Count("entries").Should().Be(1, "saving the candidate records nothing; only the edit does");
        history.Text("entries.0.by").Should().Be("Member");
        history.Id("entries.0.userId").Should().Be(world.Owner.UserId);
        history.Text("entries.0.lines.0.text").Should().Be("edited the note");
        history.Text("entries.0.lines.0.details.0.label").Should().Be("Note");
        history.Text("entries.0.lines.0.details.0.before").Should().Be("First note");
        history.Text("entries.0.lines.0.details.0.after").Should().Be("Second note");
        (await world.EntryAsync(person.ApplicationId, "KeptInMind")).GetProperty("note").GetString()
            .Should().Be("First note", "the Activity entry keeps the note as first written");
    }

    [Fact]
    public async Task A_blank_note_clears_it_and_the_History_says_so_newest_first()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Clear note job");
        var person = await world.KeptPersonAsync(job, "cleared", "First note");

        (await world.Owner.PutAsync(NotePath(person.CandidateId), new JsonObject { ["note"] = "   " })).Expect(HttpStatusCode.NoContent);
        (await world.Owner.PutAsync(NotePath(person.CandidateId), new JsonObject { ["note"] = "Back again" })).Expect(HttpStatusCode.NoContent);

        PersonOf(await world.PoolAsync(), person.CandidateId).GetProperty("note").GetString().Should().Be("Back again");
        var history = (await world.Owner.GetAsync(HistoryPath(person.CandidateId))).Expect(HttpStatusCode.OK);
        history.Count("entries").Should().Be(2);
        history.Text("entries.0.lines.0.text").Should().Be("added a note", "the newest edit comes first and the note had been cleared");
        history.Text("entries.1.lines.0.text").Should().Be("removed the note");
        history.Text("entries.1.lines.0.details.0.before").Should().Be("First note");
        history.At("entries.1.lines.0.details.0.after").ValueKind.Should().Be(JsonValueKind.Null);

        (await world.Owner.PutAsync(NotePath(person.CandidateId), new JsonObject { ["note"] = null })).Expect(HttpStatusCode.NoContent);
        PersonOf(await world.PoolAsync(), person.CandidateId).GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Saving_the_same_note_again_changes_nothing_and_records_nothing()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Same note job");
        var person = await world.KeptPersonAsync(job, "same", "Keep this");

        (await world.Owner.PutAsync(NotePath(person.CandidateId), new JsonObject { ["note"] = "Keep this" })).Expect(HttpStatusCode.NoContent);

        (await world.Owner.GetAsync(HistoryPath(person.CandidateId))).Expect(HttpStatusCode.OK).Count("entries").Should().Be(0);
        PersonOf(await world.PoolAsync(), person.CandidateId).GetProperty("noteUpdated").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_note_of_two_thousand_characters_is_kept_and_a_longer_one_is_refused_and_changes_nothing()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Long note job");
        var person = await world.KeptPersonAsync(job, "long", "Short");

        (await world.Owner.PutAsync(NotePath(person.CandidateId), new JsonObject { ["note"] = new string('a', 2001) }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("note");
        PersonOf(await world.PoolAsync(), person.CandidateId).GetProperty("note").GetString().Should().Be("Short");

        (await world.Owner.PutAsync(NotePath(person.CandidateId), new JsonObject { ["note"] = new string('a', 2000) })).Expect(HttpStatusCode.NoContent);
        PersonOf(await world.PoolAsync(), person.CandidateId).GetProperty("note").GetString()!.Length.Should().Be(2000);
    }

    [Fact]
    public async Task A_viewer_cannot_edit_the_note_but_can_read_its_History()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Viewer note job");
        var person = await world.KeptPersonAsync(job, "viewed", "Original");
        using var viewer = await fixture.InviteAndJoinAsync(world.Owner, "Viewer");

        (await viewer.PutAsync(NotePath(person.CandidateId), new JsonObject { ["note"] = "Viewer edit" })).Status.Should().Be(HttpStatusCode.Forbidden);

        PersonOf(await world.PoolAsync(), person.CandidateId).GetProperty("note").GetString().Should().Be("Original");
        (await viewer.GetAsync(HistoryPath(person.CandidateId))).Expect(HttpStatusCode.OK).Count("entries").Should().Be(0);
    }

    [Fact]
    public async Task Editing_the_note_or_reading_its_History_for_someone_who_is_not_saved_is_not_found()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (job, _) = await world.OpenJobAsync("Gone job");
        var removed = await world.KeptPersonAsync(job, "removed-note", "Note");
        (await world.Owner.PostAsync($"/v1/talent-pool/{removed.CandidateId}/remove", new JsonObject())).Expect(HttpStatusCode.NoContent);
        var neverSaved = await world.ApplyAsync(job, ApiSession.NewEmail("never"), keep: true);
        var neverSavedCandidate = (await world.DetailAsync(neverSaved)).Id("candidate.id");

        foreach (var candidate in new[] { removed.CandidateId, neverSavedCandidate, Guid.NewGuid() })
        {
            (await world.Owner.PutAsync(NotePath(candidate), new JsonObject { ["note"] = "Nobody" })).Status.Should().Be(HttpStatusCode.NotFound, candidate.ToString());
            (await world.Owner.GetAsync(HistoryPath(candidate))).Status.Should().Be(HttpStatusCode.NotFound, candidate.ToString());
        }
    }

    [Fact]
    public async Task A_member_on_only_their_jobs_edits_and_reads_the_note_history_only_for_people_saved_from_jobs_they_are_on()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (onTeam, _) = await world.OpenJobAsync("On my team");
        var (offTeam, _) = await world.OpenJobAsync("Not my team");
        var visible = await world.KeptPersonAsync(onTeam, "note-visible", "Mine");
        var hidden = await world.KeptPersonAsync(offTeam, "note-hidden", "Not mine");
        using var recruiter = await fixture.InviteAndJoinAsync(world.Owner, "Recruiter", jobAccess: "HiringTeamOnly");
        await world.Owner.SetHiringTeamAsync(onTeam, await world.Owner.MemberIdAsync(recruiter.UserId));

        (await recruiter.PutAsync(NotePath(visible.CandidateId), new JsonObject { ["note"] = "Edited by the team" })).Expect(HttpStatusCode.NoContent);
        (await recruiter.GetAsync(HistoryPath(visible.CandidateId))).Expect(HttpStatusCode.OK).Count("entries").Should().Be(1);

        (await recruiter.PutAsync(NotePath(hidden.CandidateId), new JsonObject { ["note"] = "Sneaky" })).Status.Should().Be(HttpStatusCode.NotFound);
        (await recruiter.GetAsync(HistoryPath(hidden.CandidateId))).Status.Should().Be(HttpStatusCode.NotFound);
        PersonOf(await world.PoolAsync(), hidden.CandidateId).GetProperty("note").GetString().Should().Be("Not mine");
    }

    [Fact]
    public async Task A_member_on_only_their_jobs_reads_rating_and_last_contacted_from_the_jobs_they_are_on_and_the_owner_from_all()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (onTeam, _) = await world.OpenJobAsync("Visible job");
        var (offTeam, _) = await world.OpenJobAsync("Hidden job");
        var email = ApiSession.NewEmail("scoped");
        var visibleApplication = await world.ApplyAsync(onTeam, email, keep: true);
        var hiddenApplication = await world.ApplyAsync(offTeam, email, keep: true);
        (await world.KeepAsync([visibleApplication])).Expect(HttpStatusCode.OK);
        var candidate = (await world.DetailAsync(visibleApplication)).Id("candidate.id");
        using var recruiter = await fixture.InviteAndJoinAsync(world.Owner, "Recruiter", jobAccess: "HiringTeamOnly");
        await world.Owner.SetHiringTeamAsync(onTeam, await world.Owner.MemberIdAsync(recruiter.UserId));

        // A scorecard on the application they cannot see changes no rating on the row: it shows the one it was saved from.
        (await world.Owner.PostAsync($"/v1/applications/{hiddenApplication}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        (await world.Owner.PostAsync($"/v1/applications/{hiddenApplication}/scorecards", Submission("interview", "Yes", InterviewRatings(5, 5, 5))))
            .Expect(HttpStatusCode.Created);
        PersonOf(await world.PoolAsync(), candidate).GetProperty("rating").GetProperty("state").GetString().Should().Be("NotRated");
        PersonOf(await world.PoolAsync(recruiter), candidate).GetProperty("rating").GetProperty("state").GetString().Should().Be("NotRated");

        (await world.Owner.PostAsync("/v1/emails", SendReceived(hiddenApplication))).Expect(HttpStatusCode.Accepted);
        await world.WaitForTimelineAsync(hiddenApplication, "EmailSent");
        var hiddenSentAt = (await world.EntryAsync(hiddenApplication, "EmailSent")).GetProperty("occurredAt").GetDateTimeOffset();

        PersonOf(await world.PoolAsync(), candidate).GetProperty("lastContactedAt").GetDateTimeOffset().Should().Be(hiddenSentAt);
        PersonOf(await world.PoolAsync(recruiter), candidate).GetProperty("lastContactedAt").ValueKind
            .Should().Be(JsonValueKind.Null, "an email on a job they are not on is not theirs to see");

        (await world.Owner.PostAsync("/v1/emails", SendReceived(visibleApplication))).Expect(HttpStatusCode.Accepted);
        await world.WaitForTimelineAsync(visibleApplication, "EmailSent");
        var visibleSentAt = (await world.EntryAsync(visibleApplication, "EmailSent")).GetProperty("occurredAt").GetDateTimeOffset();
        PersonOf(await world.PoolAsync(recruiter), candidate).GetProperty("lastContactedAt").GetDateTimeOffset().Should().Be(visibleSentAt);
        PersonOf(await world.PoolAsync(), candidate).GetProperty("lastContactedAt").GetDateTimeOffset().Should().Be(visibleSentAt);
    }

    [Fact]
    public async Task Saving_someone_already_saved_from_another_application_replaces_the_entry_and_uses_the_new_note_with_a_History_of_its_own()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (first, _) = await world.OpenJobAsync("First job");
        var (second, _) = await world.OpenJobAsync("Second job");
        var email = ApiSession.NewEmail("replaced");
        var firstApplication = await world.ApplyAsync(first, email, keep: true);
        var secondApplication = await world.ApplyAsync(second, email, keep: true);
        (await world.KeepAsync([firstApplication], "Written first")).Expect(HttpStatusCode.OK);
        var candidate = (await world.DetailAsync(firstApplication)).Id("candidate.id");
        (await world.Owner.PutAsync(NotePath(candidate), new JsonObject { ["note"] = "Edited once" })).Expect(HttpStatusCode.NoContent);

        (await world.KeepAsync([secondApplication], "Written second")).Expect(HttpStatusCode.OK).At("kept").GetInt32().Should().Be(1);

        var listed = (await world.PoolAsync()).Should().ContainSingle("one entry per candidate").Subject;
        listed.GetProperty("applicationId").GetGuid().Should().Be(secondApplication);
        listed.GetProperty("jobTitle").GetString().Should().Be("Second job");
        listed.GetProperty("note").GetString().Should().Be("Written second", "the new entry starts with the new request's note");
        (await world.EntryAsync(firstApplication, "LeftTalentPool")).GetProperty("poolExit").GetString().Should().Be("KeptFromAnotherJob");
        var history = (await world.Owner.GetAsync(HistoryPath(candidate))).Expect(HttpStatusCode.OK);
        history.Count("entries").Should().Be(0, "the History belongs to the entry, and the new entry has had no edit; replacing records nothing");
        (await world.Owner.PutAsync(NotePath(candidate), new JsonObject { ["note"] = "Edited after" })).Expect(HttpStatusCode.NoContent);
        var after = (await world.Owner.GetAsync(HistoryPath(candidate))).Expect(HttpStatusCode.OK);
        after.Count("entries").Should().Be(1);
        after.Text("entries.0.lines.0.details.0.before").Should().Be("Written second");
    }

    [Fact]
    public async Task The_stop_link_takes_them_out_leaves_the_note_History_as_it_was_and_returns_normally()
    {
        using var world = await PoolWorld.CreateAsync(fixture);
        var (origin, _) = await world.OpenJobAsync("Origin");
        var (target, _) = await world.OpenJobAsync("Target");
        var person = await world.KeptPersonAsync(origin, "stop-note", "Before the link");
        (await world.Owner.PutAsync(NotePath(person.CandidateId), new JsonObject { ["note"] = "Edited before stopping" })).Expect(HttpStatusCode.NoContent);
        (await world.InviteAsync([person.CandidateId], target)).Expect(HttpStatusCode.Accepted);
        var (_, token) = PoolWorld.RemoveLinkIn(await world.WaitForInvitationAsync(person.Email));
        await world.WaitForTimelineAsync(person.ApplicationId, "InvitedToApply", "EmailSent");
        var sentAt = (await world.EntryAsync(person.ApplicationId, "EmailSent")).GetProperty("occurredAt").GetDateTimeOffset();
        PersonOf(await world.PoolAsync(), person.CandidateId).GetProperty("lastContactedAt").GetDateTimeOffset()
            .Should().Be(sentAt, "the invitation is an email to them, so it counts as contact");
        var entryId = await EntryIdAsync(world, person.CandidateId);
        (await NoteHistoryRecordsAsync(world, entryId)).Should().Be(1);

        (await world.Careers.PostAsync(world.PublicPath(token), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await world.PoolAsync()).Should().BeEmpty();
        (await world.EntryAsync(person.ApplicationId, "LeftTalentPool")).GetProperty("poolExit").GetString().Should().Be("CandidateAsked");
        (await NoteHistoryRecordsAsync(world, entryId)).Should().Be(1, "removing the entry records no change of its own and deletes none");
    }

    // The application's setters are private, so the consent's date is moved with a set-based update as the application role
    // and the organization as tenant, which row-level security still checks.
    private async Task BackdateConsentAsync(PoolWorld world, Guid applicationId, DateTimeOffset givenAt)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(world.Owner.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var changed = await db.Applications.Where(a => a.Id == applicationId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.FutureRolesConsentGivenAt, (DateTimeOffset?)givenAt), Ct);
        changed.Should().Be(1);
    }

    // The id of the candidate's entry, which is the subject of its note History, read as the application role.
    private async Task<Guid> EntryIdAsync(PoolWorld world, Guid candidateId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(world.Owner.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        return await db.TalentPoolEntries.Where(e => e.CandidateId == candidateId).Select(e => e.Id).SingleAsync(Ct);
    }

    // The change records for an entry's note, read as the application role: after the entry is gone no route reaches them.
    private async Task<int> NoteHistoryRecordsAsync(PoolWorld world, Guid entryId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(world.Owner.OrganizationId);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        var subject = entryId.ToString("D");
        return await db.ChangeRecords.CountAsync(r => r.SubjectKind == ChangeSubjectKind.TalentPoolEntry && r.SubjectId == subject, Ct);
    }

    private static JsonElement PersonOf(IReadOnlyList<JsonElement> people, Guid candidateId) =>
        people.Single(p => p.GetProperty("candidateId").GetGuid() == candidateId);

    private static string NotePath(Guid candidateId) => $"/v1/talent-pool/{candidateId}/note";

    private static string HistoryPath(Guid candidateId) => $"/v1/talent-pool/{candidateId}/history";

    private static JsonObject SendReceived(Guid applicationId) => new()
    {
        ["templateId"] = "application-received",
        ["applicationIds"] = PoolWorld.Ids([applicationId]),
    };
}
