using ScreenHire.Application.Applications;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Applications;

public sealed class ApplicationNoteTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Author = Guid.CreateVersion7();
    private static readonly Guid Robin = Guid.CreateVersion7();
    private static readonly Guid Sam = Guid.CreateVersion7();

    [Fact]
    public void Mentions_are_read_from_their_tokens_once_each_in_order()
    {
        var text = $"{NoteMentions.Token(Robin)}, ask {NoteMentions.Token(Sam)} too. Thanks {NoteMentions.Token(Robin)}. Not a mention: @[nope] or @Robin";

        NoteMentions.Parse(text).Should().Equal(Robin, Sam);
        NoteMentions.Parse("No mentions here.").Should().BeEmpty();
    }

    [Fact]
    public void A_new_note_keeps_a_row_for_each_person_it_mentions()
    {
        var application = Application();

        var (note, _) = application.AddNote($"{NoteMentions.Token(Robin)} and {NoteMentions.Token(Sam)}, please look.", Monday, Author);

        note.Mentions.Select(m => m.UserId).Should().Equal(Robin, Sam);
        note.Mentions.Should().AllSatisfy(m =>
        {
            m.NoteId.Should().Be(note.Id);
            m.OrganizationId.Should().Be(application.OrganizationId);
            m.EmailedAt.Should().BeNull();
        });
    }

    [Fact]
    public void An_edit_keeps_the_latest_text_and_reports_only_the_mentions_it_adds()
    {
        var application = Application();
        var (note, _) = application.AddNote($"Ask {NoteMentions.Token(Robin)} about the call.", Monday, Author);
        var robin = note.Mentions.Single();

        var edit = application.EditNote(note, $"  Ask {NoteMentions.Token(Robin)} and {NoteMentions.Token(Sam)} about the call.  ", Monday.AddHours(1), Author);

        edit.Should().NotBeNull();
        var (entry, added) = edit!.Value;
        note.Text.Should().Be($"Ask {NoteMentions.Token(Robin)} and {NoteMentions.Token(Sam)} about the call.");
        note.EditedAt.Should().Be(Monday.AddHours(1));
        added.Select(m => m.UserId).Should().Equal([Sam], "only the person just added is notified");
        note.Mentions.Should().Contain(robin, "a mention the edit keeps keeps its row, and its email state");
        entry.Type.Should().Be(ApplicationEventType.NoteEdited);
        entry.Data.Should().Be(new ApplicationEventData(NoteId: note.Id), "the timeline never holds the text");
        entry.ActorUserId.Should().Be(Author);
    }

    [Fact]
    public void An_edit_that_drops_a_mention_removes_it_and_the_same_text_changes_nothing()
    {
        var application = Application();
        var (note, _) = application.AddNote($"Ask {NoteMentions.Token(Robin)}.", Monday, Author);

        application.EditNote(note, "Ask about the call.", Monday.AddHours(1), Author)!.Value.Added.Should().BeEmpty();
        note.Mentions.Should().BeEmpty();

        application.EditNote(note, " Ask about the call. ", Monday.AddHours(2), Author).Should().BeNull();
        note.EditedAt.Should().Be(Monday.AddHours(1), "an unchanged note is not edited");
    }

    [Fact]
    public void Deleting_records_who_and_when_but_never_the_text()
    {
        var application = Application();
        var (note, _) = application.AddNote("Strong portfolio.", Monday, Author);
        var admin = Guid.CreateVersion7();

        var entry = application.DeleteNote(note, Monday.AddDays(1), admin);

        entry.Type.Should().Be(ApplicationEventType.NoteDeleted);
        entry.Data.Should().Be(new ApplicationEventData(NoteId: note.Id));
        entry.ActorUserId.Should().Be(admin);
        entry.OccurredAt.Should().Be(Monday.AddDays(1));
    }

    [Fact]
    public void Another_applications_note_cannot_be_changed_through_this_one()
    {
        var (note, _) = Application().AddNote("A note.", Monday, Author);

        var other = Application();
        other.Invoking(a => a.DeleteNote(note, Monday, Author)).Should().Throw<InvalidOperationException>();
        other.Invoking(a => a.EditNote(note, "Changed.", Monday, Author)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Only_the_author_edits_and_the_author_an_owner_or_an_admin_deletes()
    {
        var application = Application();
        var (byPerson, _) = application.AddNote("A note.", Monday, Author);
        var key = Guid.CreateVersion7();
        var (byKey, _) = application.AddNote("From the key.", Monday, actorUserId: null, actorApiKeyId: key);
        var someoneElse = Guid.CreateVersion7();

        NotePolicy.MayEdit(byPerson, Author, null).Should().BeTrue();
        NotePolicy.MayEdit(byPerson, someoneElse, null).Should().BeFalse();
        NotePolicy.MayEdit(byKey, null, key).Should().BeTrue("a key is the author of the notes it wrote");
        NotePolicy.MayEdit(byKey, null, Guid.CreateVersion7()).Should().BeFalse();

        NotePolicy.MayDelete(byPerson, Author, null, OrganizationRole.Recruiter).Should().BeTrue();
        NotePolicy.MayDelete(byPerson, someoneElse, null, OrganizationRole.Recruiter).Should().BeFalse();
        NotePolicy.MayDelete(byPerson, someoneElse, null, OrganizationRole.Viewer).Should().BeFalse();
        NotePolicy.MayDelete(byPerson, someoneElse, null, OrganizationRole.Admin).Should().BeTrue();
        NotePolicy.MayDelete(byPerson, someoneElse, null, OrganizationRole.Owner).Should().BeTrue();
        NotePolicy.MayDelete(byPerson, someoneElse, null, role: null).Should().BeFalse("no membership here, no role here");
        NotePolicy.MayDelete(byPerson, null, Guid.CreateVersion7(), OrganizationRole.Owner).Should().BeFalse("a key has no role");
    }

    [Fact]
    public void The_mention_email_says_who_and_where_and_never_quotes_the_note()
    {
        var link = new Uri("http://localhost:4323/applications/0199e8a0-0000-7000-8000-000000000001?tab=activity#note-1");

        var email = MentionEmail.Compose("robin@example.com", "Avery <Quinn>", "Taylor Brook", "Senior .NET Engineer", "Brightwater Studio", link);

        email.To.Should().Be("robin@example.com");
        email.Subject.Should().Be("Avery <Quinn> mentioned you on Taylor Brook");
        email.TextBody.Should().Contain("Avery <Quinn> mentioned you in a note on Taylor Brook, Senior .NET Engineer, at Brightwater Studio.")
            .And.Contain(link.ToString());
        email.HtmlBody.Should().Contain("Avery &lt;Quinn&gt;", "names are encoded in the HTML part").And.Contain("Open the note");
        MentionEmail.NotePath(Guid.Empty, Guid.Empty).Should().StartWith("/applications/").And.Contain("?tab=activity#note-");
    }

    private static JobApplication Application()
    {
        var organization = Guid.CreateVersion7();
        var job = new Job { OrganizationId = organization, Title = "Barista", Slug = "barista" };
        var candidate = Candidate.Create(organization, "Alex", "Rivera", "alex@example.com", null, Monday);
        var (application, _) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Monday, Author);
        return application;
    }
}
