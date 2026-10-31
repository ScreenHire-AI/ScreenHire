using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ScreenHire.E2E;

/// <summary>
/// The talent pool end to end (docs/architecture/talent-pool.md, roadmap item 7): a candidate allows the employer to keep
/// their details for future roles on the apply form with JavaScript off, the owner keeps them in mind from their page,
/// finds them in the pool and invites them to another job, and the candidate applies from the email's link. A second test
/// has the candidate stop it from the link in the email, again with JavaScript off. Each test signs in as a new person
/// with a new organization and sets the jobs up through the API as that person.
/// </summary>
public sealed partial class TalentPoolTests(E2EFixture fixture) : BrowserTest(fixture)
{
    private static readonly string[] Britain = ["GB"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_candidate_who_allows_it_is_kept_in_mind_invited_to_another_job_and_leaves_the_pool_by_applying()
    {
        using var scene = await SceneAsync();
        var candidate = ApiSession.NewEmail("candidate");

        // The candidate's browser runs no JavaScript: the apply form is a plain form, and the box is theirs to tick.
        await WithoutJavaScriptAsync();
        await GoToAsync("careers", $"/{scene.Slug}/jobs/{scene.First.Slug}/apply");
        await ApplyAsync(candidate, keep: true);

        await WithJavaScriptAsync();
        await SignInAsync(scene.Owner);
        await Page.GotoAsync(DashboardUrl($"/applications/{await ApplicationIdAsync(scene, scene.First.Id)}"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Casey Morgan");

        // Keep in mind sits in Move to stage, and keeps them out of a rejection: their stage pill says so afterwards.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Move to stage, Casey Morgan" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Keep in mind…" }).ClickAsync();
        var sheet = Page.GetByRole(AriaRole.Dialog, new() { Name = "Keep Casey Morgan in mind" });
        await Expect(sheet).ToContainTextAsync("They leave this job without a rejection and stay in the talent pool until");
        await Page.GetByLabel("Why").FillAsync("Strong systems thinking, wrong timing.");
        await ExpectNoAccessibilityViolationsAsync();
        await sheet.GetByRole(AriaRole.Button, new() { Name = "Keep in mind" }).ClickAsync();
        await Expect(sheet).ToBeHiddenAsync();
        await Expect(Page.GetByText("Kept Casey Morgan in mind.")).ToBeAttachedAsync();
        await Expect(Page.GetByText("Kept in mind", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("In the talent pool until", new() { Exact = false })).ToBeVisibleAsync();

        // The pool lists them with the note and when the permission ends.
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Main" }).GetByRole(AriaRole.Link, new() { Name = "Talent pool" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync(new Regex("^Talent pool"));
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "Casey Morgan" });
        await Expect(row).ToContainTextAsync("Strong systems thinking, wrong timing.");
        await Expect(row).ToContainTextAsync(scene.First.Title);
        await Expect(row.Locator("time[datetime]").Last).ToHaveTextAsync(DatePattern());
        await ExpectNoAccessibilityViolationsAsync();

        // Invite to apply for the second job: the email goes out and names it.
        await row.GetByRole(AriaRole.Button, new() { Name = "Actions for Casey Morgan" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Invite to apply" }).ClickAsync();
        var invite = Page.GetByRole(AriaRole.Dialog, new() { Name = "Invite Casey Morgan to apply" });
        await invite.GetByLabel("Job").SelectOptionAsync(new SelectOptionValue { Label = scene.Second.Title });
        // Choosing a job re-renders the email's preview, which moves the sheet's buttons: wait for the chosen job's email
        // before pressing Send, or the click can land where the button was.
        await Expect(invite.GetByText($"would like you to apply for {scene.Second.Title}")).ToBeVisibleAsync();
        await ExpectNoAccessibilityViolationsAsync();
        await invite.GetByRole(AriaRole.Button, new() { Name = "Send invitation" }).ClickAsync();
        await Expect(invite).ToBeHiddenAsync();
        await Expect(Page.GetByText("Sending 1 invitation.", new() { Exact = true })).ToBeAttachedAsync();

        var text = await InvitationTextAsync(candidate);
        text.Should().Contain($"{scene.Second.Title}", "the email names the job");
        var jobLink = JobLink().Match(text);
        jobLink.Success.Should().BeTrue($"the email carries the job's careers link: {text}");
        jobLink.Groups["slug"].Value.Should().Be(scene.Second.Slug);

        // The link opens the job; applying with the same email takes them out of the pool.
        await WithoutJavaScriptAsync();
        var jobPage = await Page.GotoAsync(jobLink.Value);
        jobPage!.Status.Should().Be(200);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Apply for this role" }).ClickAsync();
        await ApplyAsync(candidate, keep: false);

        await WithJavaScriptAsync();
        await SignInAsync(scene.Owner);
        await Page.GotoAsync(DashboardUrl("/talent-pool"));
        await Expect(Page.GetByText("Nobody has been kept in mind yet.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Casey Morgan" })).ToHaveCountAsync(0);

        // The new application's Overview links back to the one they were kept from.
        var newApplicationId = await ApplicationIdAsync(scene, scene.Second.Id);
        await Page.GotoAsync(DashboardUrl($"/applications/{newApplicationId}"));
        var others = Page.GetByRole(AriaRole.Region, new() { Name = "Other applications", Exact = true });
        await Expect(others).ToBeVisibleAsync();
        await Expect(others.GetByRole(AriaRole.Link, new() { Name = scene.First.Title })).ToHaveAttributeAsync(
            "href", $"/applications/{await ApplicationIdAsync(scene, scene.First.Id)}");
        await Expect(others).ToContainTextAsync("Kept in mind");
        await ExpectNoAccessibilityViolationsAsync();
    }

    [Fact]
    public async Task A_candidate_stops_being_kept_from_the_link_in_the_invitation_without_javascript()
    {
        using var scene = await SceneAsync();
        var candidate = ApiSession.NewEmail("candidate");
        using (var anonymous = Fixture.CreateHttpClient("api"))
        {
            var applied = await anonymous.PostAsJsonAsync($"/v1/public/organizations/{scene.Slug}/jobs/{scene.First.Id}/applications", new JsonObject
            {
                ["candidate"] = new JsonObject { ["firstName"] = "Casey", ["lastName"] = "Morgan", ["email"] = candidate },
                ["privacyNoticeVersion"] = "2026-10-20",
                ["answers"] = new JsonArray(),
                ["aiUseDeclaration"] = "DidNotUse",
                ["keepForFutureRoles"] = true,
            }, Ct);
            applied.StatusCode.Should().Be(HttpStatusCode.Created, await applied.Content.ReadAsStringAsync(Ct));
            var applicationId = (await applied.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("applicationId").GetGuid();
            (await scene.Session.PostAsync("/v1/talent-pool", new { applicationIds = new[] { applicationId }, note = "Keep in touch." }, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await scene.Session.PostAsync(
            "/v1/talent-pool/invitations", new { candidateIds = new[] { await CandidateIdAsync(scene) }, jobId = scene.Second.Id }, Ct)).StatusCode
            .Should().Be(HttpStatusCode.Accepted);
        var stopLink = StopLink().Match(await InvitationTextAsync(candidate));
        stopLink.Success.Should().BeTrue("the invitation carries the link to stop being kept");

        await WithoutJavaScriptAsync();
        var response = await Page.GotoAsync(stopLink.Value);
        response!.Status.Should().Be(200);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("keeping your details?");
        (await Page.Locator("script:not([type='application/ld+json'])").CountAsync()).Should().Be(0, "the page ships no JavaScript");
        await ExpectNoAccessibilityViolationsAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Stop keeping my details" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/talent-pool/stopped$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Done");
        Page.Url.Should().NotContain(stopLink.Groups["token"].Value, "the confirmation's address carries no token");
        await ExpectNoAccessibilityViolationsAsync();

        // The link now says they are no longer kept, and the team's pool no longer lists them.
        await Page.GotoAsync(stopLink.Value);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("no longer keeps your details");
        using var pool = await scene.Session.Client.GetAsync("/v1/talent-pool", Ct);
        (await pool.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("people").GetArrayLength().Should().Be(0);
    }

    // The candidate's journey through the apply form without JavaScript: details, then the review step with
    // the AI answer the job asks for and, when they choose, the box that allows the employer to keep their details.
    private async Task ApplyAsync(string email, bool keep)
    {
        await Page.GetByLabel("First name").FillAsync("Casey");
        await Page.GetByLabel("Last name").FillAsync("Morgan");
        await Page.GetByLabel("Email").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue to review" }).ClickAsync();

        await Expect(Page.Locator("#step-heading")).ToHaveTextAsync("Review and send");
        await Page.GetByLabel("No, I did not use AI tools").CheckAsync();
        var box = Page.GetByLabel("Keep my details for future roles (optional)");
        await Expect(box).Not.ToBeCheckedAsync(); // the box is the candidate's to tick, never ticked for them
        if (keep)
        {
            await box.CheckAsync();
        }

        await ExpectNoAccessibilityViolationsAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send application" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Application sent");
    }

    // A new organization with two open jobs, made through the API by a new owner.
    private async Task<Scene> SceneAsync()
    {
        var owner = ApiSession.NewEmail("e2e-pool");
        var session = new ApiSession(Fixture);
        await session.SignInWithDevSsoAsync(owner, Ct);
        var slug = $"test-{Guid.NewGuid():N}";
        await ReadAsync(await session.PostAsync("/v1/auth/onboarding", new { organizationName = "Harbour Lane Studio", slug }, Ct), HttpStatusCode.OK);
        return new Scene(owner, slug, session, await JobAsync(session, "Platform Engineer"), await JobAsync(session, "Support Engineer"));
    }

    private static async Task<JobRef> JobAsync(ApiSession session, string title)
    {
        var job = await ReadAsync(await session.PostAsync(
            "/v1/jobs",
            new
            {
                title,
                description = "<p>Build and run the things people rely on.</p>",
                employmentType = "FullTime",
                workplaceType = "Remote",
                applicantLocationRequirements = Britain,
            },
            Ct), HttpStatusCode.Created);
        var id = job.GetProperty("id").GetGuid();
        await ReadAsync(await session.PostAsync($"/v1/jobs/{id}/publish", new { }, Ct), HttpStatusCode.OK);
        return new JobRef(id, job.GetProperty("slug").GetString()!, title);
    }

    // Each candidate here has one application per job, so the job names it.
    private static async Task<Guid> ApplicationIdAsync(Scene scene, Guid jobId)
    {
        var rows = await ReadAsync(await scene.Session.Client.GetAsync($"/v1/jobs/{jobId}/applications", Ct), HttpStatusCode.OK);
        return rows.EnumerateArray().Single().GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CandidateIdAsync(Scene scene)
    {
        var rows = await ReadAsync(await scene.Session.Client.GetAsync($"/v1/jobs/{scene.First.Id}/applications", Ct), HttpStatusCode.OK);
        return rows.EnumerateArray().Single().GetProperty("candidateId").GetGuid();
    }

    // The invitation, which is the email that carries the link to stop being kept (the candidate may have others, such as
    // the receipt for an application). Polls the inbox until it is there.
    private async Task<string> InvitationTextAsync(string email)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            var invitation = (await Fixture.Mailpit.MessagesAsync(email, Ct)).FirstOrDefault(message => StopLink().IsMatch(message.Text));
            if (invitation is not null)
            {
                return invitation.Text;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
            }
            catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
            {
                throw new TimeoutException("The invitation did not reach the candidate's inbox within 60 s.");
            }
        }
    }

    private async Task SignInAsync(string email)
    {
        await GoToAsync("dashboard", "/sign-in");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Continue with Dev SSO" }).ClickAsync();
        await Page.GetByLabel("Any email address").FillAsync(email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in with this address" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Jobs");
    }

    private string DashboardUrl(string pathAndQuery) => new Uri(Fixture.GetEndpoint("dashboard"), pathAndQuery).ToString();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
            return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
    }

    private sealed record JobRef(Guid Id, string Slug, string Title);

    private sealed record Scene(string Owner, string Slug, ApiSession Session, JobRef First, JobRef Second) : IDisposable
    {
        public void Dispose() => Session.Dispose();
    }

    [GeneratedRegex(@"https?://\S+/jobs/(?<slug>[A-Za-z0-9_-]+)")]
    private static partial Regex JobLink();

    [GeneratedRegex(@"https?://\S+/talent-pool/(?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex StopLink();

    // "Mar 15, 2027": the date a table shows (design system section 13).
    [GeneratedRegex(@"^[A-Z][a-z]{2} \d{1,2}, \d{4}$")]
    private static partial Regex DatePattern();
}
