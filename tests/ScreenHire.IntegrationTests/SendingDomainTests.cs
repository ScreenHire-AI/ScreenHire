using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The organization's own sending domain through the running API with the email sandbox (docs/architecture/email.md,
/// ADR-0021): the DNS records to add, a check that stays pending until they are in place, and mail that switches to the
/// company's From and reply host only once verified. Owners and admins change it; everyone reads it. Each test uses its own
/// organization and its own made-up domain, because the sandbox's DNS is shared by the stack.
/// </summary>
public sealed class SendingDomainTests(AppHostFixture fixture)
{
    private const string Path = "/v1/organization/sending-domain";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewDomain() => $"acme-{Guid.NewGuid():N}.example.com";

    private static JsonObject Set(string domain) => new() { ["domain"] = domain };

    private async Task PublishAsync(string domain)
    {
        using var client = fixture.AnonymousClient();
        using var response = await client.PostAsync(
            "/v1/dev/sending-domains/publish", new StringContent(Set(domain).ToJsonString(), System.Text.Encoding.UTF8, "application/json"), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_sandbox_offers_sending_domains_and_a_new_organization_has_none()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var read = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);

        read.At("available").GetBoolean().Should().BeTrue();
        read.At("sandbox").GetBoolean().Should().BeTrue();
        read.At("domain").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("http://example.com")]
    [InlineData("casey@example.com")]
    [InlineData("example")]
    [InlineData("")]
    public async Task A_mail_providers_domain_or_something_that_is_not_a_domain_is_refused(string domain)
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var refused = (await owner.PutAsync(Path, Set(domain))).Expect(HttpStatusCode.BadRequest);

        refused.ProblemKeys().Should().Equal("domain");
        (await owner.GetAsync(Path)).At("domain").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Adding_a_domain_lists_the_four_records_to_add_pending_and_adding_it_again_changes_nothing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var domain = NewDomain();

        var added = (await owner.PutAsync(Path, Set(domain.ToUpperInvariant()))).Expect(HttpStatusCode.OK);

        added.Text("domain").Should().Be(domain);
        added.Text("status").Should().Be("Pending");
        added.Text("fromAddress").Should().Be($"hiring@{domain}");
        added.Text("replyHost").Should().Be($"reply.{domain}");
        added.Count("records").Should().Be(4);
        added.Body.GetProperty("records").EnumerateArray().Select(r => (r.GetProperty("purpose").GetString(), r.GetProperty("type").GetString()))
            .Should().BeEquivalentTo([("Dkim", "CNAME"), ("Dkim", "CNAME"), ("ReturnPath", "CNAME"), ("Replies", "MX")]);
        var mx = added.Body.GetProperty("records").EnumerateArray().Single(r => r.GetProperty("type").GetString() == "MX");
        mx.GetProperty("host").GetString().Should().Be($"reply.{domain}");
        mx.GetProperty("priority").GetInt32().Should().Be(10);
        added.Body.GetProperty("records").EnumerateArray().Should().OnlyContain(r => r.GetProperty("state").GetString() == "NotChecked");

        var again = (await owner.PutAsync(Path, Set(domain))).Expect(HttpStatusCode.OK);
        again.Text("createdAt").Should().Be(added.Text("createdAt"));
    }

    [Fact]
    public async Task A_check_before_the_records_exist_leaves_the_domain_pending_with_every_record_missing()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        (await owner.PutAsync(Path, Set(NewDomain()))).Expect(HttpStatusCode.OK);

        var checkedDomain = (await owner.PostAsync($"{Path}/check")).Expect(HttpStatusCode.OK);

        checkedDomain.Text("status").Should().Be("Pending");
        checkedDomain.Body.GetProperty("records").EnumerateArray().Should().OnlyContain(r => r.GetProperty("state").GetString() == "Missing");
        checkedDomain.At("lastCheckedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);
        checkedDomain.At("verifiedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Once_the_records_are_published_a_check_verifies_and_the_next_email_comes_from_the_organization_on_its_reply_host()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var domain = NewDomain();
        var orgName = (await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK).Text("name");
        (await owner.PutAsync(Path, Set(domain))).Expect(HttpStatusCode.OK);
        var beforeVerification = await fixture.SendToNewCandidateAsync(owner, subject: "Before");
        beforeVerification.Mail.From!.Address.Should().NotBe($"hiring@{domain}", "mail keeps ScreenHire's address until the domain is verified");
        beforeVerification.Mail.ReplyTo!.Address.Should().EndWith("@reply.localhost");

        await PublishAsync(domain);
        var verified = (await owner.PostAsync($"{Path}/check")).Expect(HttpStatusCode.OK);

        verified.Text("status").Should().Be("Verified");
        verified.Body.GetProperty("records").EnumerateArray().Should().OnlyContain(r => r.GetProperty("state").GetString() == "Matches");
        verified.At("verifiedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);

        var sent = await fixture.SendToNewCandidateAsync(owner, subject: "After");
        sent.Mail.From!.Name.Should().Be(orgName);
        sent.Mail.From.Address.Should().Be($"hiring@{domain}");
        sent.Mail.ReplyTo!.Address.Should().StartWith("reply+").And.EndWith($"@reply.{domain}");

        // A reply to the organization's own reply host reaches the application like any other.
        (await fixture.DevReplyAsync(sent.ReplyAddress, sent.Address, "Thanks, see you then.")).Should().Be("Stored");
        (await owner.MessagesAsync(sent.CandidateId)).Received().Should().ContainSingle();
    }

    [Fact]
    public async Task Removing_the_domain_returns_mail_to_the_default_from_and_reply_host()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var domain = NewDomain();
        (await owner.PutAsync(Path, Set(domain))).Expect(HttpStatusCode.OK);
        await PublishAsync(domain);
        (await owner.PostAsync($"{Path}/check")).Expect(HttpStatusCode.OK).Text("status").Should().Be("Verified");

        (await owner.DeleteAsync(Path)).Expect(HttpStatusCode.NoContent);

        (await owner.GetAsync(Path)).At("domain").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        var sent = await fixture.SendToNewCandidateAsync(owner);
        sent.Mail.From!.Address.Should().NotBe($"hiring@{domain}");
        sent.Mail.ReplyTo!.Address.Should().EndWith("@reply.localhost");
        (await owner.PostAsync($"{Path}/check")).Expect(HttpStatusCode.NotFound);
        (await owner.DeleteAsync(Path)).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Adding_another_domain_replaces_the_organizations_current_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var first = NewDomain();
        var second = NewDomain();
        (await owner.PutAsync(Path, Set(first))).Expect(HttpStatusCode.OK);

        (await owner.PutAsync(Path, Set(second))).Expect(HttpStatusCode.OK).Text("domain").Should().Be(second);

        (await owner.GetAsync(Path)).Text("domain.domain").Should().Be(second);
    }

    [Fact]
    public async Task Another_organization_cannot_add_a_domain_that_is_taken()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var domain = NewDomain();
        (await ours.PutAsync(Path, Set(domain))).Expect(HttpStatusCode.OK);

        var refused = (await theirs.PutAsync(Path, Set(domain))).Expect(HttpStatusCode.BadRequest);

        refused.ProblemKeys().Should().Equal("domain");
        refused.Body.GetProperty("errors").GetProperty("domain")[0].GetString().Should().NotContain(ours.OrganizationId.ToString());
        (await theirs.GetAsync(Path)).At("domain").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        (await ours.GetAsync(Path)).Text("domain.domain").Should().Be(domain);
    }

    [Fact]
    public async Task An_organization_that_asks_for_a_taken_domain_keeps_the_one_it_has_and_its_published_records()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        var mine = NewDomain();
        var taken = NewDomain();
        (await theirs.PutAsync(Path, Set(taken))).Expect(HttpStatusCode.OK);
        (await ours.PutAsync(Path, Set(mine))).Expect(HttpStatusCode.OK);
        await PublishAsync(mine);

        (await ours.PutAsync(Path, Set(taken))).Expect(HttpStatusCode.BadRequest);

        // Nothing changed for us, and the refusal did not unpublish either domain.
        (await ours.GetAsync(Path)).Text("domain.domain").Should().Be(mine);
        (await ours.PostAsync(Path + "/check", new JsonObject())).Expect(HttpStatusCode.OK).Text("status").Should().Be("Verified");
        (await theirs.GetAsync(Path)).Text("domain.domain").Should().Be(taken);
    }

    [Fact]
    public async Task The_history_says_who_added_verified_and_removed_the_domain_and_a_check_that_changes_nothing_adds_no_line()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var domain = NewDomain();
        (await owner.GetAsync(Path)).At("lastUpdated").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "no domain, nothing to date");
        (await owner.PutAsync(Path, Set(domain))).Expect(HttpStatusCode.OK);
        (await owner.PostAsync(Path + "/check", new JsonObject())).Expect(HttpStatusCode.OK);
        (await owner.PostAsync(Path + "/check", new JsonObject())).Expect(HttpStatusCode.OK);
        await PublishAsync(domain);
        (await owner.PostAsync(Path + "/check", new JsonObject())).Expect(HttpStatusCode.OK).Text("status").Should().Be("Verified");

        var last = (await owner.GetAsync(Path)).Expect(HttpStatusCode.OK);
        last.Text("lastUpdated.by").Should().Be("Member");
        last.Id("lastUpdated.userId").Should().Be(owner.UserId);

        (await owner.DeleteAsync(Path)).Expect(HttpStatusCode.NoContent);

        var history = (await owner.GetAsync(Path + "/history")).Expect(HttpStatusCode.OK);
        history.Count("entries").Should().Be(3, "added, verified and removed; the two checks that found nothing new recorded nothing");
        history.Text("entries.0.lines.0.text").Should().Be($"removed the sending domain {domain}");
        history.Text("entries.1.lines.0.text").Should().StartWith($"verified the sending domain {domain}");
        history.Text("entries.2.lines.0.text").Should().Be($"added the sending domain {domain}");
        history.Id("entries.2.userId").Should().Be(owner.UserId);
    }

    [Fact]
    public async Task Another_organizations_history_is_its_own_and_a_recruiter_can_read_it()
    {
        using var ours = await fixture.SignedInWithOrganizationAsync();
        using var theirs = await fixture.SignedInWithOrganizationAsync();
        (await ours.PutAsync(Path, Set(NewDomain()))).Expect(HttpStatusCode.OK);

        (await theirs.GetAsync(Path + "/history")).Expect(HttpStatusCode.OK).Count("entries").Should().Be(0);
        (await ours.GetAsync(Path + "/history")).Expect(HttpStatusCode.OK).Count("entries").Should().Be(1);
    }

    [Fact]
    public async Task A_recruiter_and_a_viewer_read_the_domain_but_only_owners_and_admins_change_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");
        using var viewer = await fixture.InviteAndJoinAsync(owner, "Viewer");
        using var admin = await fixture.InviteAndJoinAsync(owner, "Admin");
        var domain = NewDomain();
        (await owner.PutAsync(Path, Set(domain))).Expect(HttpStatusCode.OK);

        (await recruiter.GetAsync(Path)).Expect(HttpStatusCode.OK).Text("domain.domain").Should().Be(domain);
        (await viewer.GetAsync(Path)).Expect(HttpStatusCode.OK).Text("domain.domain").Should().Be(domain);

        (await recruiter.PutAsync(Path, Set(NewDomain()))).Expect(HttpStatusCode.Forbidden).Text("title").Should().Be("Not in your role");
        (await viewer.PutAsync(Path, Set(NewDomain()))).Expect(HttpStatusCode.Forbidden);
        (await recruiter.PostAsync($"{Path}/check")).Expect(HttpStatusCode.Forbidden);
        (await recruiter.DeleteAsync(Path)).Expect(HttpStatusCode.Forbidden);
        (await owner.GetAsync(Path)).Text("domain.domain").Should().Be(domain);

        (await admin.PostAsync($"{Path}/check")).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_signed_out_request_is_unauthorized()
    {
        using var anonymous = fixture.CreateHttpClient("api");

        using var response = await anonymous.GetAsync(Path, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
