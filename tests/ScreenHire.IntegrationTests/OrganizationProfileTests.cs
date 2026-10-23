using System.Text.Json.Nodes;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// <c>/v1/organization</c>: any member reads the organization's name, slug and careers address; owners and admins rename
/// it, and the slug never changes with the name.
/// </summary>
public sealed class OrganizationProfileTests(AppHostFixture fixture)
{
    [Fact]
    public async Task An_owner_renames_the_organization_and_its_careers_address_stays()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var before = (await owner.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK);
        var slug = before.Text("slug");

        var renamed = (await owner.PutAsync("/v1/organization", new JsonObject { ["name"] = "  Harbour Lane Bakery  " })).Expect(HttpStatusCode.OK);

        renamed.Text("name").Should().Be("Harbour Lane Bakery");
        renamed.Text("slug").Should().Be(slug);
        renamed.Id().Should().Be(before.Id());
        new Uri(renamed.Text("careersUrl")).Host.Should().StartWith($"{slug}.");
        var me = (await owner.Session.CurrentUserAsync(TestContext.Current.CancellationToken))!.Value;
        me.At("organization.name").GetString().Should().Be("Harbour Lane Bakery");
        me.At("organization.slug").GetString().Should().Be(slug);
    }

    [Fact]
    public async Task A_blank_or_long_name_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();

        var blank = (await owner.PutAsync("/v1/organization", new JsonObject { ["name"] = "   " })).Expect(HttpStatusCode.BadRequest);
        var tooLong = (await owner.PutAsync("/v1/organization", new JsonObject { ["name"] = new string('a', 201) })).Expect(HttpStatusCode.BadRequest);

        blank.ProblemKeys().Should().Equal("name");
        tooLong.ProblemKeys().Should().Equal("name");
    }

    [Fact]
    public async Task A_recruiter_reads_the_organization_but_cannot_rename_it()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var recruiter = await fixture.InviteAndJoinAsync(owner, "Recruiter");

        var read = (await recruiter.GetAsync("/v1/organization")).Expect(HttpStatusCode.OK);
        var refused = (await recruiter.PutAsync("/v1/organization", new JsonObject { ["name"] = "Taken over" })).Expect(HttpStatusCode.Forbidden);

        read.Id().Should().Be((await owner.GetAsync("/v1/organization")).Id());
        refused.Text("title").Should().Be("Owners and admins only");
        (await owner.GetAsync("/v1/organization")).Text("name").Should().NotBe("Taken over");
    }

    [Fact]
    public async Task A_signed_out_request_is_unauthorized()
    {
        using var anonymous = fixture.CreateHttpClient("api");

        using var response = await anonymous.GetAsync("/v1/organization", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
