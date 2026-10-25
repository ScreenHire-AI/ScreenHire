using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using ScreenHire.IntegrationTests.Messaging;
using ScreenHire.IntegrationTests.Realtime;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Notes the team edits and deletes, and the mentions in them (design system section 22): only the author edits a
/// note, the author, an Owner or an Admin deletes it, and the server checks both. Deleting erases the note's text,
/// leaving only who deleted it and when. A mention is a member's user id; the person mentioned gets an email that says
/// who and where and never quotes the note, and a mention an edit adds notifies only that person.
/// </summary>
public sealed class ApplicationNotesTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_mention_emails_the_person_mentioned_without_the_note_and_an_edit_notifies_only_the_person_it_adds()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var robinEmail = ApiSession.NewEmail("robin");
        var samEmail = ApiSession.NewEmail("sam");
        using var robin = await fixture.InviteAndJoinAsync(owner, "Recruiter", robinEmail);
        using var sam = await fixture.InviteAndJoinAsync(owner, "Viewer", samEmail);
        var application = await ApplicationAsync(owner, "Taylor Brook");

        var added = (await owner.PostAsync($"/v1/applications/{application}/notes", Note($"{Mention(robin)}, ask how they measured the drop in run time.")))
            .Expect(HttpStatusCode.Created);
        var noteId = added.Id("noteId");
        added.Text("note").Should().Be($"{Mention(robin)}, ask how they measured the drop in run time.");

        var toRobin = (await WaitForMentionsAsync(robinEmail, 1)).Single();
        toRobin.Subject.Should().Contain("mentioned you on Taylor Brook");
        toRobin.Text.Should().Contain("Taylor Brook, Platform Engineer").And.Contain($"/applications/{application}?tab=activity#note-{noteId}");
        toRobin.Text.Should().NotContain("measured", "the email never quotes the note");
        toRobin.Html.Should().NotContain("measured");

        var edited = (await owner.PutAsync($"/v1/applications/{application}/notes/{noteId}", Note($"{Mention(robin)} and {Mention(sam)}, ask how they measured it.")))
            .Expect(HttpStatusCode.OK);
        edited.At("editedAt").ValueKind.Should().Be(JsonValueKind.String);

        (await WaitForMentionsAsync(samEmail, 1)).Single().Text.Should().NotContain("measured");
        (await MentionsAsync(robinEmail)).Should().HaveCount(1, "an edit notifies only the person it adds");
        var timeline = (await owner.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK).Body.EnumerateArray().ToList();
        var entry = timeline.Single(e => e.GetProperty("type").GetString() == "NoteAdded");
        entry.GetProperty("noteId").GetGuid().Should().Be(noteId);
        entry.GetProperty("note").GetString().Should().Be($"{Mention(robin)} and {Mention(sam)}, ask how they measured it.");
        entry.GetProperty("noteEditedAt").ValueKind.Should().Be(JsonValueKind.String);
        timeline.Should().ContainSingle(e => e.GetProperty("type").GetString() == "NoteEdited" && e.GetProperty("noteId").GetGuid() == noteId);
    }

    [Fact]
    public async Task Only_the_author_edits_and_the_author_an_owner_or_an_admin_deletes_and_deleting_erases_the_text()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        using var other = await fixture.SignedInWithOrganizationAsync();
        var application = await ApplicationAsync(owner, "Casey Morgan");
        var ownersNote = (await owner.PostAsync($"/v1/applications/{application}/notes", Note("Distinctive words: heliotrope lantern.")))
            .Expect(HttpStatusCode.Created).Id("noteId");
        var recruitersNote = (await recruiter.PostAsync($"/v1/applications/{application}/notes", Note("My own note.")))
            .Expect(HttpStatusCode.Created).Id("noteId");
        var path = $"/v1/applications/{application}/notes/{ownersNote}";

        (await recruiter.PutAsync(path, Note("Taken over."))).Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Author only");
        (await recruiter.DeleteAsync(path)).Expect(HttpStatusCode.Forbidden);
        (await admin.PutAsync(path, Note("Taken over."))).Expect(HttpStatusCode.Forbidden); // an admin deletes, but never edits, another's note
        (await viewer.PutAsync(path, Note("Taken over."))).Expect(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync(path)).Expect(HttpStatusCode.Forbidden);
        (await other.PutAsync(path, Note("Taken over."))).Expect(HttpStatusCode.NotFound);
        (await other.DeleteAsync(path)).Expect(HttpStatusCode.NotFound);
        (await owner.PutAsync($"/v1/applications/{application}/notes/{Guid.CreateVersion7()}", Note("Missing."))).Expect(HttpStatusCode.NotFound);
        (await recruiter.PutAsync($"/v1/applications/{application}/notes/{recruitersNote}", Note("My own note, edited."))).Expect(HttpStatusCode.OK);
        (await recruiter.DeleteAsync($"/v1/applications/{application}/notes/{recruitersNote}")).Expect(HttpStatusCode.NoContent);

        (await admin.DeleteAsync(path)).Expect(HttpStatusCode.NoContent);

        (await owner.DeleteAsync(path)).Expect(HttpStatusCode.NotFound); // a deleted note is gone
        (await owner.PutAsync(path, Note("Back again."))).Expect(HttpStatusCode.NotFound);
        var timeline = (await owner.GetAsync($"/v1/applications/{application}/timeline")).Body.EnumerateArray().ToList();
        timeline.Where(e => e.GetProperty("type").GetString() == "NoteAdded").Should().AllSatisfy(e =>
            e.GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null, "a deleted note's entry has no text"));
        timeline.Should().ContainSingle(e => e.GetProperty("type").GetString() == "NoteDeleted"
            && e.GetProperty("noteId").GetGuid() == ownersNote && e.GetProperty("actorUserId").GetGuid() == admin.UserId);
        await using var connection = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var count = new NpgsqlCommand(
            """
            SELECT (SELECT count(*) FROM application_notes WHERE application_id = @application)
                 + (SELECT count(*) FROM application_events WHERE application_id = @application AND data::text LIKE '%heliotrope%')
            """, connection);
        count.Parameters.AddWithValue("application", application);
        ((long)(await count.ExecuteScalarAsync(Ct))!).Should().Be(0, "deleting erases the note's text from storage");
    }

    [Fact]
    public async Task A_mention_must_name_a_member_of_this_organization()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var outsider = await fixture.SignedInWithOrganizationAsync();
        var application = await ApplicationAsync(owner, "Casey Morgan");

        (await owner.PostAsync($"/v1/applications/{application}/notes", Note($"Ask {Mention(outsider)}.")))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("note");
        (await owner.PostAsync($"/v1/applications/{application}/notes", Note($"Ask @[{Guid.CreateVersion7()}].")))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("note");
        var note = (await owner.PostAsync($"/v1/applications/{application}/notes", Note("Plain."))).Expect(HttpStatusCode.Created).Id("noteId");
        (await owner.PutAsync($"/v1/applications/{application}/notes/{note}", Note($"Ask {Mention(outsider)}.")))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("note");
    }

    [Fact]
    public async Task An_edit_keeps_a_mention_of_someone_who_has_left_and_checks_only_the_mentions_it_adds()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var leaver = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var outsider = await fixture.SignedInWithOrganizationAsync();
        var application = await ApplicationAsync(owner, "Casey Morgan");
        var note = (await owner.PostAsync($"/v1/applications/{application}/notes", Note($"Ask {Mention(leaver)}.")))
            .Expect(HttpStatusCode.Created).Id("noteId");
        (await owner.DeleteAsync($"/v1/organization/members/{await owner.MemberIdAsync(leaver.UserId)}")).Expect(HttpStatusCode.NoContent);

        (await owner.PutAsync($"/v1/applications/{application}/notes/{note}", Note($"Ask {Mention(leaver)} first.")))
            .Expect(HttpStatusCode.OK);
        (await owner.PutAsync($"/v1/applications/{application}/notes/{note}", Note($"Ask {Mention(leaver)} and {Mention(outsider)}.")))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("note");
    }

    [Fact]
    public async Task An_admin_of_another_organization_is_only_a_recruiter_here()
    {
        using var ownerA = await fixture.SignedInWithOrganizationAsync();
        using var ownerB = await fixture.SignedInWithOrganizationAsync();
        var email = ApiSession.NewEmail("both");
        using var inB = await fixture.InviteAndJoinAsync(ownerB, "Admin", email);
        using var inA = await fixture.InviteAndJoinAsync(ownerA, "Recruiter", email);
        var application = await ApplicationAsync(ownerA, "Casey Morgan");
        var note = (await ownerA.PostAsync($"/v1/applications/{application}/notes", Note("The owner's note."))).Expect(HttpStatusCode.Created).Id("noteId");

        (await inA.DeleteAsync($"/v1/applications/{application}/notes/{note}")).Expect(HttpStatusCode.Forbidden);
        (await ownerA.GetAsync($"/v1/applications/{application}/timeline")).Body.EnumerateArray()
            .Should().ContainSingle(e => e.GetProperty("type").GetString() == "NoteAdded" && e.GetProperty("note").GetString() == "The owner's note.");
    }

    [Fact]
    public async Task Edits_deletes_and_mentions_reach_their_own_organization_with_ids_only()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var robin = await fixture.InviteAndJoinAsync(ours, "Recruiter");
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        await using var ourClient = await EventsConnection.ConnectAsync(robin.Session, Ct);
        await using var theirClient = await EventsConnection.ConnectAsync(theirs.Session, Ct);
        var application = await ApplicationAsync(ours, "Casey Morgan");

        var note = (await ours.PostAsync($"/v1/applications/{application}/notes", Note($"{Mention(robin)}, have a look."))).Expect(HttpStatusCode.Created).Id("noteId");
        var mentioned = await ourClient.WaitForAsync("TeammateMentioned", "noteId", note, Ct);
        mentioned.GetProperty("mentionedUserId").GetGuid().Should().Be(robin.UserId);
        mentioned.GetProperty("authorUserId").GetGuid().Should().Be(ours.UserId);
        mentioned.TryGetProperty("note", out _).Should().BeFalse("the note's text stays behind the API");

        (await ours.PutAsync($"/v1/applications/{application}/notes/{note}", Note($"{Mention(robin)}, have a look today."))).Expect(HttpStatusCode.OK);
        (await ourClient.WaitForAsync("ApplicationNoteEdited", "noteId", note, Ct)).GetProperty("applicationId").GetGuid().Should().Be(application);
        (await ours.DeleteAsync($"/v1/applications/{application}/notes/{note}")).Expect(HttpStatusCode.NoContent);
        (await ourClient.WaitForAsync("ApplicationNoteDeleted", "noteId", note, Ct)).GetProperty("applicationId").GetGuid().Should().Be(application);

        theirClient.ReceivedAnythingAbout(application).Should().BeFalse("another organization's notes never reach us");
        theirClient.ReceivedAnythingAbout(note).Should().BeFalse();
    }

    private static JsonObject Note(string text) => new() { ["note"] = text };

    private static string Mention(Tenant member) => $"@[{member.UserId}]";

    private async Task<IReadOnlyList<MailpitMessage>> MentionsAsync(string address) =>
        [.. (await fixture.Mailpit.MessagesAsync(address, Ct)).Where(m => m.Subject.Contains("mentioned you", StringComparison.Ordinal))];

    private async Task<IReadOnlyList<MailpitMessage>> WaitForMentionsAsync(string address, int count)
    {
        await WolverineStore.WaitUntilAsync(async () => (await MentionsAsync(address)).Count >= count, $"{count} mention emails reach {address}", Ct);
        return await MentionsAsync(address);
    }

    private static async Task<Guid> ApplicationAsync(Tenant owner, string candidateName)
    {
        var job = await owner.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await owner.PublishJobAsync(job.Id());
        return (await owner.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"), candidateName)).Id();
    }
}
