using System.Text.Json.Nodes;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Verification;
using static ScreenHire.IntegrationTests.CandidateTestSupport;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// A person is one candidate per organization, matched on their address trimmed and lower-cased, whichever way they
/// arrive: the careers form or a recruiter adding them by hand. A later application never rewrites the name or phone the
/// first one stored, and another organization's candidate with the same address is a different candidate.
/// </summary>
public sealed class CandidateMatchingTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_recruiter_adding_the_same_person_to_another_job_with_a_different_case_and_spaces_adds_an_application_to_the_same_candidate()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugAsync(owner);
        var firstJob = await OpenJobTitledAsync(owner, "Platform Engineer");
        var secondJob = await OpenJobTitledAsync(owner, "Data Analyst");
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var first = await fixture.ApplyAsync(slug, firstJob, person, Ct);

        var second = (await owner.PostAsync($"/v1/jobs/{secondJob}/applications", new JsonObject
        {
            ["candidate"] = new JsonObject
            {
                ["firstName"] = "Somebody", ["lastName"] = "Else", ["email"] = $"  {person.Email.ToUpperInvariant()}  ", ["phone"] = "+44 20 7946 0999",
            },
        })).Expect(HttpStatusCode.Created).Id();

        var candidateId = await CandidateOfAsync(owner, first);
        (await CandidateOfAsync(owner, second)).Should().Be(candidateId);
        var all = (await owner.GetAsync("/v1/candidates")).Expect(HttpStatusCode.OK);
        all.Count().Should().Be(1, "one person, one candidate");
        var detail = (await owner.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.OK);
        detail.At("applications").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).Should().BeEquivalentTo([first, second]);
        detail.Text("fullName").Should().Be(person.FullName, "the first application's name stays");
        detail.Text("phone").Should().Be(person.Phone, "the first application's phone stays");
        detail.Text("email").Should().Be(person.Email);
    }

    [Fact]
    public async Task Applying_again_on_the_careers_page_with_the_address_in_another_case_is_the_same_candidate_and_not_a_duplicate()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugAsync(owner);
        var firstJob = await OpenJobTitledAsync(owner, "Platform Engineer");
        var secondJob = await OpenJobTitledAsync(owner, "Data Analyst");
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var first = await fixture.ApplyAsync(slug, firstJob, person, Ct);
        await VerificationSupport.SettledAsync(fixture, owner, first);

        var again = person with { Email = person.Email.ToUpperInvariant(), FirstName = "Other", LastName = "Name", Phone = "+44 20 7946 0888" };
        var second = await fixture.ApplyAsync(slug, secondJob, again, Ct);

        var candidateId = await CandidateOfAsync(owner, first);
        (await CandidateOfAsync(owner, second)).Should().Be(candidateId);
        (await owner.GetAsync("/v1/candidates")).Expect(HttpStatusCode.OK).Count().Should().Be(1);
        var detail = (await owner.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.OK);
        detail.At("applications").GetArrayLength().Should().Be(2);
        detail.Text("fullName").Should().Be(person.FullName);
        detail.Text("phone").Should().Be(person.Phone);

        // The same person applying twice is not an application farm: the duplicate check counts other candidates only.
        var verification = await VerificationSupport.SettledAsync(fixture, owner, second);
        AuthenticityCodes(verification).Should().NotContain(SignalCatalogue.DuplicateInOrganization);
        AuthenticityCodes(await VerificationSupport.AssessedAsync(owner, first)).Should().NotContain(SignalCatalogue.DuplicateInOrganization);
        ReviewEntries(await VerificationSupport.AssessedAsync(owner, first), "EvidenceChanged").Should().BeEmpty();
    }

    [Fact]
    public async Task The_same_address_in_another_organization_is_a_separate_candidate()
    {
        using var first = await fixture.SignedInWithOrganizationAsync();
        using var second = await fixture.SignedInWithOrganizationAsync();
        var email = ApiSession.NewEmail("shared");
        var firstApplication = (await first.CreateApplicationAsync(await OpenJobTitledAsync(first, "Platform Engineer"), email, "Casey Morgan")).Id();
        var secondApplication = (await second.CreateApplicationAsync(await OpenJobTitledAsync(second, "Platform Engineer"), email.ToUpperInvariant(), "Casey Morgan")).Id();

        var firstCandidate = await CandidateOfAsync(first, firstApplication);
        var secondCandidate = await CandidateOfAsync(second, secondApplication);

        secondCandidate.Should().NotBe(firstCandidate);
        (await first.GetAsync("/v1/candidates")).Expect(HttpStatusCode.OK).Count().Should().Be(1);
        (await second.GetAsync("/v1/candidates")).Expect(HttpStatusCode.OK).Count().Should().Be(1);
        (await second.GetAsync($"/v1/candidates/{firstCandidate}")).Expect(HttpStatusCode.NotFound);
        (await first.GetAsync($"/v1/candidates/{firstCandidate}")).At("applications").GetArrayLength().Should().Be(1);
    }
}
