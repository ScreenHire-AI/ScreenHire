using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Verification;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.IntegrationTests.Realtime;
using static ScreenHire.IntegrationTests.CandidateTestSupport;
using static ScreenHire.IntegrationTests.ScorecardSupport;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Erasing a candidate (<c>POST /v1/candidates/{id}/erase</c>): the database's cascades remove every row about them, the
/// membership log keeps a tombstone with no personal data, the files' blobs go, and the applications that shared a
/// detail with them are checked again. Each test makes its own organization and a person no other test uses, so a scan
/// of the whole database for their name and address finds only their own rows.
/// </summary>
public sealed class CandidateErasureTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Erasing_a_candidate_with_rows_in_the_append_only_tables_is_accepted_by_the_database()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var world = await PersonWithEverythingAsync(owner);

        // These tables refuse DELETE and UPDATE to the application role (their migrations revoke it). The person has rows
        // in every one that a candidate reaches, so the cascade below really is tested against them.
        var rows = await RowsReferencingAsync(fixture, [world.CandidateId], [world.FirstApplication, world.SecondApplication]);
        rows.Should().Contain(
            [
                "applications.candidate_id", "application_events.application_id", "application_files.application_id", "application_notes.application_id",
                "application_scorecards.application_id", "application_emails.application_id", "application_tasks.application_id",
                "application_documents.application_id", "submission_observations.application_id", "consistency_checks.application_id",
                "verification_analyzer_runs.application_id", "verification_signals.application_id", "verification_assessments.application_id",
                "verification_review_actions.application_id", "automation_runs.application_id", "talent_pool_entries.application_id", "talent_pool_entries.candidate_id",
            ]);

        // The application role cannot delete from application_events, scorecards, verification rows and the like; the
        // cascade from the candidate row has to. A refusal here is a 500.
        var erased = await owner.PostAsync($"/v1/candidates/{world.CandidateId}/erase", new JsonObject { ["confirmation"] = world.Person.FullName });

        erased.Expect(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/v1/candidates/{world.CandidateId}")).Expect(HttpStatusCode.NotFound);
        foreach (var application in new[] { world.FirstApplication, world.SecondApplication })
        {
            (await owner.GetAsync($"/v1/applications/{application}")).Expect(HttpStatusCode.NotFound);
            (await owner.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.NotFound);
            (await owner.GetAsync($"/v1/applications/{application}/verification")).Expect(HttpStatusCode.NotFound);
        }

        (await owner.GetAsync("/v1/candidates")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Should().NotContain(c => c.GetProperty("id").GetGuid() == world.CandidateId);
        (await owner.GetAsync("/v1/talent-pool")).Expect(HttpStatusCode.OK).At("people").EnumerateArray()
            .Should().NotContain(p => p.GetProperty("candidateId").GetGuid() == world.CandidateId);
    }

    [Fact]
    public async Task Nothing_about_an_erased_candidate_is_left_in_any_table_and_the_files_blobs_are_deleted()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var world = await PersonWithEverythingAsync(owner);
        var values = new[] { world.Person.Email, world.Person.FullName, world.Person.FirstName, world.Person.LastName };
        var ids = new[] { world.CandidateId.ToString(), world.FirstApplication.ToString(), world.SecondApplication.ToString() };

        // The scan must be able to see the person before it can show they are gone. The draft is keyed by the address, so
        // no cascade reaches it.
        var before = await TablesHoldingAsync(fixture, [.. values, .. ids]);
        before.Should().Contain(
            [
                $"candidates holds {world.Person.Email}", $"candidates holds {world.Person.FullName}", $"application_drafts holds {world.Person.Email}",
                $"application_documents holds {world.Person.FullName}", $"application_notes holds {world.FirstApplication}",
                $"application_scorecards holds {world.SecondApplication}", $"application_emails holds {world.Person.Email}",
                $"talent_pool_entries holds {world.CandidateId}",
            ]);
        var blobLinks = await FileLinksAsync(owner, world.FirstApplication);
        blobLinks.Should().NotBeEmpty();
        foreach (var link in blobLinks)
        {
            using var storage = new HttpClient();
            using var present = await storage.GetAsync(link, Ct);
            present.StatusCode.Should().Be(HttpStatusCode.OK, "the resume's blob exists before the erasure");
        }

        (await owner.PostAsync($"/v1/candidates/{world.CandidateId}/erase", new JsonObject { ["confirmation"] = world.Person.FullName }))
            .Expect(HttpStatusCode.NoContent);

        // The worker deletes the blobs from the message sent in the erasure's transaction; poll the links captured before.
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                using var storage = new HttpClient();
                foreach (var link in blobLinks)
                {
                    using var response = await storage.GetAsync(link, Ct);
                    if (response.StatusCode != HttpStatusCode.NotFound)
                    {
                        return false;
                    }
                }

                return true;
            },
            "the worker to delete the erased candidate's file blobs",
            Ct);

        // After the worker has handled the erasure's message, so a handler that wrote a row late would be seen too.
        (await RowsReferencingAsync(fixture, [world.CandidateId], [world.FirstApplication, world.SecondApplication]))
            .Should().BeEmpty("no row of a table with a candidate_id or application_id column may reference the erased ids");
        (await TablesHoldingAsync(fixture, [.. values, .. ids]))
            .Should().BeEmpty("neither the name, the address nor an id of the erased person may remain in any column of any table");
    }

    [Fact]
    public async Task The_organizations_open_dashboards_hear_that_a_candidate_was_erased_and_no_other_organization_does()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var application = (await owner.CreateApplicationAsync(await OpenJobTitledAsync(owner, "Platform Engineer"), person.Email, person.FullName)).Id();
        var candidateId = await CandidateOfAsync(owner, application);
        await using var events = await EventsConnection.ConnectAsync(owner.Session, Ct);
        await using var otherEvents = await EventsConnection.ConnectAsync(other.Session, Ct);

        (await owner.PostAsync($"/v1/candidates/{candidateId}/erase", new JsonObject { ["confirmation"] = person.FullName })).Expect(HttpStatusCode.NoContent);

        await events.WaitForAsync("CandidateErased", "candidateId", candidateId, Ct);
        otherEvents.ReceivedAnythingAbout(candidateId).Should().BeFalse("another organization hears nothing about our erasures");
    }

    [Fact]
    public async Task An_erasure_leaves_one_tombstone_in_the_membership_log_with_the_actor_and_a_time_and_nothing_about_the_candidate()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await OpenJobTitledAsync(owner, "Platform Engineer");
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var application = (await owner.CreateApplicationAsync(job, person.Email, person.FullName)).Id();
        var candidateId = await CandidateOfAsync(owner, application);

        (await owner.PostAsync($"/v1/candidates/{candidateId}/erase", new JsonObject { ["confirmation"] = person.FullName })).Expect(HttpStatusCode.NoContent);

        var tombstones = (await owner.GetAsync("/v1/organization/membership-log")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Where(e => e.GetProperty("kind").GetString() == "CandidateErased").ToList();
        tombstones.Should().ContainSingle();
        var tombstone = tombstones[0];
        tombstone.GetProperty("actorUserId").GetGuid().Should().Be(owner.UserId);
        tombstone.GetProperty("occurredAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        tombstone.GetProperty("memberUserId").ValueKind.Should().Be(JsonValueKind.Null);
        tombstone.GetProperty("jobId").ValueKind.Should().Be(JsonValueKind.Null, "the log does not say which job's candidate it was");
        tombstone.GetRawText().Should().NotContainEquivalentOf(person.FirstName).And.NotContainEquivalentOf(person.LastName)
            .And.NotContainEquivalentOf(person.Email).And.NotContain(candidateId.ToString()).And.NotContain(application.ToString());
    }

    [Fact]
    public async Task A_wrong_confirmation_is_a_validation_problem_and_deletes_nothing_while_case_and_surrounding_spaces_do_not_matter()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await OpenJobTitledAsync(owner, "Platform Engineer");
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var application = (await owner.CreateApplicationAsync(job, person.Email, person.FullName)).Id();
        var candidateId = await CandidateOfAsync(owner, application);
        var path = $"/v1/candidates/{candidateId}/erase";

        foreach (var wrong in new[] { person.FirstName, $"{person.FullName} Jr", person.Email, "" })
        {
            (await owner.PostAsync(path, new JsonObject { ["confirmation"] = wrong })).Expect(HttpStatusCode.BadRequest)
                .ProblemKeys().Should().Equal("confirmation");
        }

        (await owner.PostAsync(path, new JsonObject())).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("confirmation");
        (await owner.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.OK);
        (await owner.GetAsync($"/v1/applications/{application}")).Expect(HttpStatusCode.OK);
        (await owner.GetAsync("/v1/organization/membership-log")).Body.EnumerateArray()
            .Should().NotContain(e => e.GetProperty("kind").GetString() == "CandidateErased", "a refused erasure leaves no tombstone");

        (await owner.PostAsync(path, new JsonObject { ["confirmation"] = $"   {person.FullName.ToUpperInvariant()}  " })).Expect(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_owners_and_admins_who_see_every_job_can_erase_and_another_organizations_candidate_is_404()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var stranger = await fixture.SignedInWithOrganizationAsync();
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        var firstJob = await OpenJobTitledAsync(owner, "Platform Engineer");
        var secondJob = await OpenJobTitledAsync(owner, "Data Analyst");
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var application = (await owner.CreateApplicationAsync(firstJob, person.Email, person.FullName)).Id();
        await owner.CreateApplicationAsync(secondJob, person.Email, person.FullName);
        var candidateId = await CandidateOfAsync(owner, application);
        var path = $"/v1/candidates/{candidateId}/erase";
        var body = new JsonObject { ["confirmation"] = person.FullName };

        (await stranger.PostAsync(path, body)).Expect(HttpStatusCode.NotFound);
        (await recruiter.PostAsync(path, body)).Expect(HttpStatusCode.Forbidden);
        (await viewer.PostAsync(path, body)).Expect(HttpStatusCode.Forbidden);
        (await owner.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.OK).At("applications").GetArrayLength().Should().Be(2);

        // A custom role may be given Erase candidates, but job access still decides: on Only jobs they're on, a member who
        // reads the candidate (their team has one of the two jobs) is told there is nothing to erase.
        var roleId = (await owner.PostAsync("/v1/organization/roles", new JsonObject { ["name"] = "Compliance", ["copyFrom"] = "Viewer" })).Expect(HttpStatusCode.Created).Id();
        (await owner.PutAsync($"/v1/organization/roles/{roleId}", new JsonObject
        {
            ["name"] = "Compliance",
            ["permissions"] = new JsonArray("ViewHiring", "EraseCandidates"),
        })).Expect(HttpStatusCode.OK);
        using var narrow = await fixture.InviteAndJoinAsync(owner, "Viewer", jobAccess: "HiringTeamOnly");
        await owner.GiveRoleAsync(narrow, roleId);
        await owner.SetHiringTeamAsync(firstJob, await owner.MemberIdAsync(narrow.UserId));
        (await narrow.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.OK).At("applications").GetArrayLength()
            .Should().Be(1, "the member sees the candidate through the one job they are on");

        (await narrow.PostAsync(path, body)).Expect(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.OK).At("applications").GetArrayLength().Should().Be(2);

        (await admin.PostAsync(path, body)).Expect(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.NotFound);
        (await owner.GetAsync("/v1/organization/membership-log")).Body.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "CandidateErased").GetProperty("actorUserId").GetGuid().Should().Be(admin.UserId);
    }

    [Fact]
    public async Task An_organization_API_key_cannot_erase_a_candidate()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await OpenJobTitledAsync(owner, "Platform Engineer");
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var application = (await owner.CreateApplicationAsync(job, person.Email, person.FullName)).Id();
        var candidateId = await CandidateOfAsync(owner, application);
        using var key = new KeyClient(fixture, (await owner.CreateKeyAsync("Everything", ["Read", "Write"])).Text("token"));

        var erase = await key.PostAsync($"/v1/candidates/{candidateId}/erase", new JsonObject { ["confirmation"] = person.FullName });

        erase.Status.Should().Be(HttpStatusCode.Unauthorized, "only the hiring API accepts keys, and erasing is not part of it");
        (await key.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.OK);
        (await owner.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_new_application_with_the_erased_address_makes_a_brand_new_candidate_with_none_of_the_old_history()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugAsync(owner);
        var job = await OpenJobTitledAsync(owner, "Platform Engineer");
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var first = await fixture.ApplyAsync(slug, job, person, Ct);
        var oldCandidate = await CandidateOfAsync(owner, first);
        (await owner.PostAsync($"/v1/applications/{first}/notes", new JsonObject { ["note"] = "Strong first call." })).Expect(HttpStatusCode.Created);
        (await owner.PostAsync($"/v1/candidates/{oldCandidate}/erase", new JsonObject { ["confirmation"] = person.FullName })).Expect(HttpStatusCode.NoContent);

        var again = await fixture.ApplyAsync(slug, job, person, Ct);

        var newCandidate = await CandidateOfAsync(owner, again);
        newCandidate.Should().NotBe(oldCandidate);
        var detail = (await owner.GetAsync($"/v1/candidates/{newCandidate}")).Expect(HttpStatusCode.OK);
        detail.At("applications").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).Should().Equal(again);
        (await owner.GetAsync($"/v1/applications/{again}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Should().NotContain(e => e.GetProperty("type").GetString() == "NoteAdded");
        (await owner.GetAsync($"/v1/candidates/{oldCandidate}")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Erasing_one_of_two_candidates_who_shared_a_device_and_phone_withdraws_the_evidence_from_the_other_without_naming_the_erased()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugAsync(owner);
        var firstJob = await OpenJobTitledAsync(owner, "Platform Engineer");
        var secondJob = await OpenJobTitledAsync(owner, "Data Analyst");
        var (erasedPerson, keptPerson) = (SyntheticApplicants.ApplicationFarm[0].AsNewFarmMember(), SyntheticApplicants.ApplicationFarm[1].AsNewFarmMember());
        var erased = await fixture.ApplyAsync(slug, firstJob, erasedPerson, Ct);
        await VerificationSupport.SettledAsync(fixture, owner, erased);
        var kept = await fixture.ApplyAsync(slug, secondJob, keptPerson, Ct);
        await VerificationSupport.SettledAsync(fixture, owner, kept);
        var withEvidence = await VerificationSupport.AssessedAsync(owner, kept);
        AuthenticityCodes(withEvidence).Should().Contain(SignalCatalogue.DuplicateInOrganization, "the later application shares a device and phone with the erased one");
        var erasedCandidate = await CandidateOfAsync(owner, erased);
        // The first member's re-check has finished: nothing else is in flight for the pair when the erasure starts.
        await WaitForReviewEntryAsync(owner, erased, "EvidenceChanged");

        (await owner.PostAsync($"/v1/candidates/{erasedCandidate}/erase", new JsonObject { ["confirmation"] = erasedPerson.FullName })).Expect(HttpStatusCode.NoContent);

        var after = await WaitForReviewEntryAsync(owner, kept, "EvidenceChanged", alsoHolds: v => !AuthenticityCodes(v).Contains(SignalCatalogue.DuplicateInOrganization));
        AuthenticityCodes(after).Should().NotContain(SignalCatalogue.DuplicateInOrganization);
        var entry = ReviewEntries(after, "EvidenceChanged").Should().ContainSingle().Which;
        entry.GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null);
        entry.GetProperty("note").GetString().Should().Be("A detail this application shared with another application no longer applies.");
        entry.GetRawText().Should().NotContainEquivalentOf(erasedPerson.FirstName).And.NotContainEquivalentOf(erasedPerson.LastName)
            .And.NotContainEquivalentOf(erasedPerson.Email).And.NotContain(erased.ToString()).And.NotContain(erasedCandidate.ToString());

        // The kept application's own rows, the superseded revision included, hold nothing of the erased person.
        (await TablesHoldingAsync(fixture, erasedPerson.Email, erasedPerson.FullName, erasedPerson.FirstName, erasedPerson.LastName, erased.ToString(), erasedCandidate.ToString()))
            .Should().BeEmpty();
        var keptCandidate = await CandidateOfAsync(owner, kept);
        (await owner.GetAsync($"/v1/candidates/{keptCandidate}")).Expect(HttpStatusCode.OK).Text("email").Should().Be(keptPerson.Email.ToLowerInvariant());
    }

    [Fact]
    public async Task Erasing_the_later_of_two_candidates_who_shared_a_device_and_phone_returns_the_earlier_application_to_its_first_assessment()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugAsync(owner);
        var firstJob = await OpenJobTitledAsync(owner, "Platform Engineer");
        var secondJob = await OpenJobTitledAsync(owner, "Data Analyst");
        var (keptPerson, erasedPerson) = (SyntheticApplicants.ApplicationFarm[0].AsNewFarmMember(), SyntheticApplicants.ApplicationFarm[1].AsNewFarmMember());
        var kept = await fixture.ApplyAsync(slug, firstJob, keptPerson, Ct);
        await VerificationSupport.SettledAsync(fixture, owner, kept);
        var before = await VerificationSupport.AssessedAsync(owner, kept);
        var erased = await fixture.ApplyAsync(slug, secondJob, erasedPerson, Ct);
        await VerificationSupport.SettledAsync(fixture, owner, erased);
        // The kept application's own assessment from before the match is the one the erasure has to bring back: the same
        // signals, so the same inputs, as a row that is already stored.
        await WaitForReviewEntryAsync(owner, kept, "EvidenceChanged", alsoHolds: v => AuthenticityCodes(v).Contains(SignalCatalogue.DuplicateInOrganization));
        var erasedCandidate = await CandidateOfAsync(owner, erased);

        (await owner.PostAsync($"/v1/candidates/{erasedCandidate}/erase", new JsonObject { ["confirmation"] = erasedPerson.FullName })).Expect(HttpStatusCode.NoContent);

        var after = await WaitForReviewEntryAsync(
            owner, kept, "EvidenceChanged", count: 2, alsoHolds: v => !AuthenticityCodes(v).Contains(SignalCatalogue.DuplicateInOrganization));
        after.Text("authenticity.level").Should().Be(before.Text("authenticity.level"));
        AuthenticityCodes(after).Should().BeEquivalentTo(AuthenticityCodes(before));
        ReviewEntries(after, "EvidenceChanged")[^1].GetProperty("note").GetString()
            .Should().Be("A detail this application shared with another application no longer applies.");
    }

    // A person with two applications on two jobs and a row in every kind of table the cascade has to reach: the careers
    // application (with its resume, answers, observation, documents and verification), a recruiter-added one on another
    // job, a note, a tag, a task, a scorecard, a verification note, a sent email, a talent pool entry and a saved draft.
    [Fact]
    public async Task A_reply_goes_with_the_candidate_and_so_does_its_attachments_blob()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var name = $"Reply Writer {Guid.NewGuid():N}";
        var text = $"My CV is attached {Guid.NewGuid():N}";
        var sent = await fixture.SendToNewCandidateAsync(owner, fullName: name);
        await fixture.DevReplyAsync(sent.ReplyAddress, sent.Address, text, attachments: [("cv.pdf", ReplySupport.Pdf())]);
        var replyId = (await owner.MessagesAsync(sent.CandidateId)).Received().Single().GetProperty("id").GetGuid();
        var attachmentId = (await owner.GetAsync($"/v1/replies/{replyId}")).Expect(HttpStatusCode.OK).At("attachments.0.id").GetGuid();
        await WolverineStore.WaitUntilAsync(
            async () => (await owner.GetAsync($"/v1/replies/{replyId}")).Expect(HttpStatusCode.OK).Text("attachments.0.scanStatus") != "Pending",
            "the attachment's malware scan",
            Ct);
        using var download = await owner.Session.Client.GetAsync($"/v1/replies/{replyId}/attachments/{attachmentId}", Ct);
        download.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var link = download.Headers.Location!;
        using (var storage = new HttpClient())
        {
            (await storage.GetAsync(link, Ct)).StatusCode.Should().Be(HttpStatusCode.OK, "the attachment's blob exists before the erasure");
        }

        (await TablesHoldingAsync(fixture, text)).Should().Contain($"inbound_emails holds {text}");

        (await owner.PostAsync($"/v1/candidates/{sent.CandidateId}/erase", new JsonObject { ["confirmation"] = name })).Expect(HttpStatusCode.NoContent);

        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                using var storage = new HttpClient();
                using var response = await storage.GetAsync(link, Ct);
                return response.StatusCode == HttpStatusCode.NotFound;
            },
            "the worker to delete the erased candidate's reply attachment",
            Ct);
        (await TablesHoldingAsync(fixture, text)).Should().BeEmpty("a reply's text goes with the candidate");
    }

    [Fact]
    public async Task Erasing_a_saved_candidate_removes_their_note_history_even_from_an_entry_that_left_the_list_and_keeps_another_candidates()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugAsync(owner);
        var job = await OpenJobTitledAsync(owner, "Platform Engineer");
        var erasedPerson = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var keptPerson = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var erased = await fixture.ApplyAsync(slug, job, erasedPerson, Ct, keepForFutureRoles: true);
        var kept = await fixture.ApplyAsync(slug, job, keptPerson, Ct, keepForFutureRoles: true);
        var erasedCandidate = await CandidateOfAsync(owner, erased);
        var keptCandidate = await CandidateOfAsync(owner, kept);
        var (noteThatLeft, lastNote, keptNote) = ($"left {Guid.NewGuid():N}", $"last {Guid.NewGuid():N}", $"kept {Guid.NewGuid():N}");

        await SaveWithNoteAsync(owner, erased, erasedCandidate, noteThatLeft);
        (await owner.PostAsync($"/v1/talent-pool/{erasedCandidate}/remove", new JsonObject())).Expect(HttpStatusCode.NoContent);
        // An application stays Saved once it leaves the list, so being saved again takes a new one, and a new entry.
        var later = await fixture.ApplyAsync(slug, await OpenJobTitledAsync(owner, "Data Analyst"), erasedPerson, Ct, keepForFutureRoles: true);
        await SaveWithNoteAsync(owner, later, erasedCandidate, lastNote);
        await SaveWithNoteAsync(owner, kept, keptCandidate, keptNote);
        // In another organization, the kind of row the erasure purges in its own: a note whose entry left the list.
        using var other = await fixture.SignedInWithOrganizationAsync();
        var otherPerson = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var otherApplication = await fixture.ApplyAsync(await SlugAsync(other), await OpenJobTitledAsync(other, "Platform Engineer"), otherPerson, Ct, keepForFutureRoles: true);
        var otherCandidate = await CandidateOfAsync(other, otherApplication);
        var otherNote = $"other {Guid.NewGuid():N}";
        await SaveWithNoteAsync(other, otherApplication, otherCandidate, otherNote);
        (await other.PostAsync($"/v1/talent-pool/{otherCandidate}/remove", new JsonObject())).Expect(HttpStatusCode.NoContent);
        (await TablesHoldingAsync(fixture, noteThatLeft, lastNote, keptNote, otherNote)).Should().Contain(
            [$"change_records holds {noteThatLeft}", $"change_records holds {lastNote}", $"change_records holds {keptNote}", $"change_records holds {otherNote}"]);

        (await owner.PostAsync($"/v1/candidates/{otherCandidate}/erase", new JsonObject { ["confirmation"] = otherPerson.FullName }))
            .Expect(HttpStatusCode.NotFound);
        (await owner.PostAsync($"/v1/candidates/{erasedCandidate}/erase", new JsonObject { ["confirmation"] = erasedPerson.FullName }))
            .Expect(HttpStatusCode.NoContent);

        (await TablesHoldingAsync(fixture, noteThatLeft, lastNote)).Should().BeEmpty("the free text of a saved note may name the person");
        (await TablesHoldingAsync(fixture, keptNote)).Should().Contain($"change_records holds {keptNote}", "another candidate's history stays");
        (await TablesHoldingAsync(fixture, otherNote)).Should().Contain($"change_records holds {otherNote}", "another organization's history is out of reach");
    }

    private static async Task SaveWithNoteAsync(Tenant owner, Guid application, Guid candidate, string note)
    {
        (await owner.PostAsync("/v1/talent-pool", new JsonObject { ["applicationIds"] = new JsonArray(JsonValue.Create(application)) })).Expect(HttpStatusCode.OK);
        (await owner.PutAsync($"/v1/talent-pool/{candidate}/note", new JsonObject { ["note"] = note })).Expect(HttpStatusCode.NoContent);
    }

    private async Task<World> PersonWithEverythingAsync(Tenant owner)
    {
        // The first job is the engineering template, which asks the typical answers: their fingerprints and the job's
        // automations leave rows of their own. The applicant is the bot, whose observed signals fill the verification tables.
        var (slug, firstJob) = await VerificationSupport.OpenJobAsync(fixture, owner, SystemTemplates.SoftwareEngineerVersion2);
        var secondJob = await OpenJobTitledAsync(owner, "Data Analyst");
        var person = SyntheticApplicants.Bot.AsNewPerson() with { Email = $"bot-{Guid.NewGuid():N}@tempinbox.example.net" };

        var first = await fixture.ApplyAsync(slug, firstJob, person, Ct, answers: person.Answers, keepForFutureRoles: true);
        var second = (await owner.CreateApplicationAsync(secondJob, person.Email, person.FullName)).Id();
        var candidateId = await CandidateOfAsync(owner, first);
        (await CandidateOfAsync(owner, second)).Should().Be(candidateId, "the same address is the same person");
        var verification = await VerificationSupport.SettledAsync(fixture, owner, first);
        await VerificationSupport.AssessedAsync(owner, second);
        // The engineering template's automation acts on the first application (it can tag or route it) once the assessment
        // lands. Everything below writes to that application, and an automation still running would make one of those
        // writes lose the row to it (a 409), so wait until every action of the application has finished.
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId))
        {
            await WolverineStore.WaitUntilAsync(
                async () =>
                {
                    var runs = await db.AutomationRuns.Where(r => r.ApplicationId == first && r.Trigger == TriggerType.VerificationAssessed).Select(r => r.Id).ToListAsync(Ct);
                    return runs.Count > 0 && !await db.AutomationActionRuns.AnyAsync(a => runs.Contains(a.RunId) && a.FinishedAt == null, Ct);
                },
                "the verification automation to finish its actions",
                Ct);
        }

        // The bot's signals give the verification tables rows, and a reviewer's dismissal adds an append-only review action.
        (await owner.PostAsync(
            $"/v1/applications/{first}/verification/assessments/{verification.Id("authenticity.id")}/dismiss",
            new JsonObject { ["reason"] = "VerifiedOutsideScreenHire", ["note"] = "Video call on Monday." })).Expect(HttpStatusCode.OK);

        (await owner.PostAsync($"/v1/applications/{first}/notes", new JsonObject { ["note"] = $"Spoke with {person.FullName}." })).Expect(HttpStatusCode.Created);
        (await owner.PostAsync($"/v1/applications/{first}/tags", new JsonObject { ["tag"] = "Referral" })).Expect(HttpStatusCode.OK);
        (await owner.PostAsync($"/v1/applications/{second}/tasks", new JsonObject { ["note"] = "Check the references." })).Expect(HttpStatusCode.Created);
        (await owner.PostAsync($"/v1/applications/{first}/verification/notes", new JsonObject { ["note"] = "Checked with the candidate." })).Expect(HttpStatusCode.OK);
        (await owner.PostAsync($"/v1/applications/{second}/move", new JsonObject { ["toStageId"] = "interview" })).Expect(HttpStatusCode.OK);
        (await owner.PostAsync($"/v1/applications/{second}/scorecards", Submission("interview", "Yes", InterviewRatings(4, 5, 5)))).Expect(HttpStatusCode.Created);
        (await owner.PostAsync("/v1/talent-pool", new JsonObject { ["applicationIds"] = new JsonArray(JsonValue.Create(first)) })).Expect(HttpStatusCode.OK);

        var marker = Guid.NewGuid().ToString("N")[..8];
        (await owner.PostAsync("/v1/emails", new JsonObject
        {
            ["subject"] = $"Next steps {marker}",
            ["body"] = "Hi {{ firstName }}, thanks for applying.",
            ["applicationIds"] = new JsonArray(JsonValue.Create(first)),
        })).Expect(HttpStatusCode.Accepted);
        await WolverineStore.WaitUntilAsync(
            async () => (await owner.GetAsync($"/v1/candidates/{candidateId}/emails")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
                .Any(e => e.GetProperty("subject").GetString() == $"Next steps {marker}"),
            "the worker to send and record the email",
            Ct);

        // Saved after the applications: submitting an application deletes the drafts for its address.
        using var careers = fixture.CreateHttpClient("api");
        var draft = await careers.PostAsJsonAsync($"/v1/public/organizations/{slug}/jobs/{secondJob}/application-drafts", new JsonObject
        {
            ["candidate"] = new JsonObject { ["firstName"] = person.FirstName, ["lastName"] = person.LastName, ["email"] = person.Email },
            ["answers"] = new JsonArray(),
            ["aiUseDeclaration"] = "DidNotUse",
        }, Ct);
        draft.StatusCode.Should().Be(HttpStatusCode.Created, await draft.Content.ReadAsStringAsync(Ct));

        return new World(slug, person, first, second, candidateId);
    }

    // The download links of an application's files, which are redirects to storage with a read-only signature.
    private static async Task<IReadOnlyList<Uri>> FileLinksAsync(Tenant owner, Guid applicationId)
    {
        var links = new List<Uri>();
        var files = new List<Guid>();
        await WolverineStore.WaitUntilAsync(
            async () =>
            {
                var application = (await owner.GetAsync($"/v1/applications/{applicationId}")).Expect(HttpStatusCode.OK);
                files = [.. application.At("files").EnumerateArray().Select(f => f.GetProperty("id").GetGuid())];
                return files.Count > 0 && application.At("files").EnumerateArray().All(f => f.GetProperty("scanStatus").GetString() != "Pending");
            },
            "the worker to scan the files",
            Ct);
        foreach (var file in files)
        {
            using var response = await owner.Session.Client.GetAsync($"/v1/applications/{applicationId}/files/{file}", Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            links.Add(response.Headers.Location!);
        }

        return links;
    }

    private sealed record World(string Slug, SyntheticApplicant Person, Guid FirstApplication, Guid SecondApplication, Guid CandidateId);
}
