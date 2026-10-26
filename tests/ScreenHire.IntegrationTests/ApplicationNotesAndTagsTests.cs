using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Notes and tags a person puts on an application: notes land on the timeline with their author, tags are added and
/// removed ignoring case and show in the lists that filter by them. A Viewer changes neither, and another
/// organization's application is a 404.
/// </summary>
public sealed class ApplicationNotesAndTagsTests(AppHostFixture fixture)
{
    [Fact]
    public async Task A_note_lands_on_the_timeline_with_its_author()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var application = await ApplicationAsync(recruiter);

        var note = (await recruiter.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = "  Ask about the notice period.  " }))
            .Expect(HttpStatusCode.Created);

        note.Text("type").Should().Be("NoteAdded");
        note.Text("note").Should().Be("Ask about the notice period.");
        note.Id("actorUserId").Should().Be(recruiter.UserId);
        var timeline = (await recruiter.GetAsync($"/v1/applications/{application}/timeline")).Expect(HttpStatusCode.OK);
        timeline.Body.EnumerateArray().Should().Contain(e => e.GetProperty("id").GetGuid() == note.Id() && e.GetProperty("type").GetString() == "NoteAdded");
        (await recruiter.GetAsync($"/v1/applications/{application}")).Text("stage.id").Should().Be("applied", "a note changes nothing else");
    }

    [Fact]
    public async Task A_blank_or_overlong_note_is_refused()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var application = await ApplicationAsync(recruiter);

        (await recruiter.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = "   " }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("note");
        (await recruiter.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = new string('a', 2001) }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("note");
    }

    [Fact]
    public async Task Tags_are_added_and_removed_ignoring_case_and_show_in_the_lists()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var application = await ApplicationAsync(recruiter);

        var tagged = (await recruiter.PostAsync($"/v1/applications/{application}/tags", new JsonObject { ["tag"] = " Referral " })).Expect(HttpStatusCode.OK);
        tagged.At("tags").EnumerateArray().Select(t => t.GetString()).Should().Equal("Referral");
        (await recruiter.PostAsync($"/v1/applications/{application}/tags", new JsonObject { ["tag"] = "referral" }))
            .Expect(HttpStatusCode.OK).Count("tags").Should().Be(1, "a tag it already has, in any case, changes nothing");
        var list = (await recruiter.GetAsync("/v1/applications")).Expect(HttpStatusCode.OK);
        list.Text("0.tags.0").Should().Be("Referral");

        var removed = (await recruiter.DeleteAsync($"/v1/applications/{application}/tags?tag=REFERRAL")).Expect(HttpStatusCode.OK);
        removed.Count("tags").Should().Be(0);
        (await recruiter.DeleteAsync($"/v1/applications/{application}/tags?tag=referral")).Expect(HttpStatusCode.OK).Count("tags").Should().Be(0);

        var timeline = (await recruiter.GetAsync($"/v1/applications/{application}/timeline")).Body.EnumerateArray()
            .Where(e => e.GetProperty("type").GetString() is "TagAdded" or "TagRemoved")
            .Select(e => (e.GetProperty("type").GetString(), e.GetProperty("tag").GetString(), e.GetProperty("actorUserId").GetGuid()))
            .ToList();
        timeline.Should().Equal(("TagAdded", "Referral", recruiter.UserId), ("TagRemoved", "Referral", recruiter.UserId));
    }

    [Fact]
    public async Task A_tag_must_be_named_and_short()
    {
        using var recruiter = await fixture.SignedInWithOrganizationAsync();
        var application = await ApplicationAsync(recruiter);

        (await recruiter.PostAsync($"/v1/applications/{application}/tags", new JsonObject { ["tag"] = "" }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("tag");
        (await recruiter.PostAsync($"/v1/applications/{application}/tags", new JsonObject { ["tag"] = new string('t', 51) }))
            .Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("tag");
        (await recruiter.DeleteAsync($"/v1/applications/{application}/tags")).Expect(HttpStatusCode.BadRequest).ProblemKeys().Should().Equal("tag");
    }

    [Fact]
    public async Task Another_organization_cannot_note_or_tag_an_application_and_a_viewer_cannot_change_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var application = await ApplicationAsync(owner);
        using var other = await fixture.SignedInWithOrganizationAsync();
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");

        (await other.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = "Hello" })).Expect(HttpStatusCode.NotFound);
        (await other.PostAsync($"/v1/applications/{application}/tags", new JsonObject { ["tag"] = "mine" })).Expect(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/v1/applications/{application}/tags?tag=mine")).Expect(HttpStatusCode.NotFound);
        (await viewer.PostAsync($"/v1/applications/{application}/notes", new JsonObject { ["note"] = "Hello" })).Expect(HttpStatusCode.Forbidden);
        (await viewer.PostAsync($"/v1/applications/{application}/tags", new JsonObject { ["tag"] = "mine" })).Expect(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"/v1/applications/{application}/tags?tag=mine")).Expect(HttpStatusCode.Forbidden);

        (await owner.GetAsync($"/v1/applications/{application}")).Count("tags").Should().Be(0);
        (await owner.GetAsync($"/v1/applications/{application}/timeline")).Body.EnumerateArray()
            .Should().NotContain(e => e.GetProperty("type").GetString() == "NoteAdded");
    }

    private static async Task<Guid> ApplicationAsync(Tenant recruiter)
    {
        var job = await recruiter.CreateJobAsync(Samples.RemoteJob("Platform Engineer"));
        await recruiter.PublishJobAsync(job.Id());
        return (await recruiter.CreateApplicationAsync(job.Id(), ApiSession.NewEmail("candidate"))).Id();
    }
}
