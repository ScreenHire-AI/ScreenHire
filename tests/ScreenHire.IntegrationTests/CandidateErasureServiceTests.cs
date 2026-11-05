using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Candidates;
using ScreenHire.Application.Verification;
using ScreenHire.Domain.Privacy;
using ScreenHire.Infrastructure.Persistence;
using static ScreenHire.IntegrationTests.CandidateTestSupport;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The shared erasure service (<see cref="CandidateErasure"/>) run in-process against the AppHost's database as the
/// application role, the way the record retention run uses it: <c>EraseApplicationsAsync</c> deletes just the applications
/// it is given, and a candidate left with none is erased whole. Each test makes its own organization.
/// </summary>
public sealed class CandidateErasureServiceTests(AppHostFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_candidate_with_two_applications_loses_only_the_selected_one()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var first = (await owner.CreateApplicationAsync(await OpenJobTitledAsync(owner, "Platform Engineer"), person.Email, person.FullName)).Id();
        var second = (await owner.CreateApplicationAsync(await OpenJobTitledAsync(owner, "Data Analyst"), person.Email, person.FullName)).Id();
        var candidateId = await CandidateOfAsync(owner, first);

        var result = await EraseApplicationsAsync(owner.OrganizationId, [first]);

        result.ApplicationsDeleted.Should().Be(1);
        result.CandidatesErased.Should().Be(0);
        result.Messages.OfType<ScreenHire.Application.Candidates.CandidateErased>().Should().BeEmpty("the candidate stays");
        (await owner.GetAsync($"/v1/applications/{first}")).Expect(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/v1/applications/{second}")).Expect(HttpStatusCode.OK);
        (await owner.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.OK).At("applications").GetArrayLength().Should().Be(1);
        (await owner.GetAsync("/v1/organization/membership-log")).Body.EnumerateArray()
            .Should().NotContain(e => e.GetProperty("kind").GetString() == "CandidateErased", "a partial removal leaves no tombstone");
    }

    [Fact]
    public async Task A_candidate_who_loses_their_last_application_is_erased_whole_with_a_tombstone_and_no_actor()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var first = (await owner.CreateApplicationAsync(await OpenJobTitledAsync(owner, "Platform Engineer"), person.Email, person.FullName)).Id();
        var second = (await owner.CreateApplicationAsync(await OpenJobTitledAsync(owner, "Data Analyst"), person.Email, person.FullName)).Id();
        var candidateId = await CandidateOfAsync(owner, first);

        var result = await EraseApplicationsAsync(owner.OrganizationId, [first, second]);

        result.ApplicationsDeleted.Should().Be(2);
        result.CandidatesErased.Should().Be(1);
        result.Messages.OfType<ScreenHire.Application.Candidates.CandidateErased>().Should().ContainSingle().Which.CandidateId.Should().Be(candidateId);
        (await owner.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.NotFound);
        var tombstone = (await owner.GetAsync("/v1/organization/membership-log")).Expect(HttpStatusCode.OK).Body.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "CandidateErased");
        tombstone.GetProperty("actorUserId").ValueKind.Should().Be(JsonValueKind.Null, "the retention schedule is not a member");
        (await TablesHoldingAsync(fixture, person.Email, person.FullName, candidateId.ToString())).Should().BeEmpty();
    }

    [Fact]
    public async Task A_saved_candidate_is_untouched_even_when_all_their_applications_are_given()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        var slug = await SlugAsync(owner);
        var job = await OpenJobTitledAsync(owner, "Platform Engineer");
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var application = await fixture.ApplyAsync(slug, job, person, Ct, keepForFutureRoles: true);
        var candidateId = await CandidateOfAsync(owner, application);
        (await owner.PostAsync("/v1/talent-pool", new JsonObject { ["applicationIds"] = new JsonArray(JsonValue.Create(application)) })).Expect(HttpStatusCode.OK);

        var result = await EraseApplicationsAsync(owner.OrganizationId, [application]);

        result.ApplicationsDeleted.Should().Be(0);
        result.CandidatesErased.Should().Be(0);
        result.Messages.Should().BeEmpty();
        (await owner.GetAsync($"/v1/candidates/{candidateId}")).Expect(HttpStatusCode.OK);
        (await owner.GetAsync($"/v1/applications/{application}")).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Another_organizations_applications_are_out_of_reach()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var application = (await other.CreateApplicationAsync(await OpenJobTitledAsync(other, "Platform Engineer"), person.Email, person.FullName)).Id();

        var result = await EraseApplicationsAsync(owner.OrganizationId, [application]);

        result.ApplicationsDeleted.Should().Be(0);
        (await other.GetAsync($"/v1/applications/{application}")).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Erasing_whole_without_a_transaction_is_refused()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        await using var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId);

        var erase = () => new CandidateErasure(new DatabaseApplicantDirectory(db))
            .EraseCandidateAsync(db, Guid.NewGuid(), null, null, DateTimeOffset.UtcNow, Ct);

        await erase.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task The_data_requests_table_is_invisible_to_another_organization_even_in_raw_sql()
    {
        using var owner = await fixture.SignedInWithOrganizationAsync();
        using var other = await fixture.SignedInWithOrganizationAsync();
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var application = (await owner.CreateApplicationAsync(await OpenJobTitledAsync(owner, "Platform Engineer"), person.Email, person.FullName)).Id();
        var candidateId = await CandidateOfAsync(owner, application);
        await using (var db = await VerificationSupport.TenantDbAsync(fixture, owner.OrganizationId))
        {
            db.DataRequests.Add(DataRequest.Open(owner.OrganizationId, candidateId, DataRequestKind.Copy, DateTimeOffset.UtcNow, TimeSpan.FromDays(30)));
            var settings = new RetentionSettings { OrganizationId = owner.OrganizationId };
            settings.Set(12, DateTimeOffset.UtcNow);
            db.RetentionSettings.Add(settings);
            await db.SaveChangesAsync(Ct);
        }

        await using var theirs = await VerificationSupport.TenantDbAsync(fixture, other.OrganizationId);
        (await theirs.DataRequests.CountAsync(Ct)).Should().Be(0);
        (await theirs.RetentionSettings.CountAsync(Ct)).Should().Be(0);
        await using var transaction = await theirs.Database.BeginTransactionAsync(Ct);
        (await theirs.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM data_requests").SingleAsync(Ct)).Should().Be(0);
        (await theirs.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM retention_settings").SingleAsync(Ct)).Should().Be(0);
    }

    // The retention run's shape: the handler's transaction, the service, one save, then the messages go out. Here they are
    // returned so a test can read them.
    private async Task<ApplicationErasure> EraseApplicationsAsync(Guid organizationId, IReadOnlyList<Guid> applicationIds)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var result = await new CandidateErasure(new DatabaseApplicantDirectory(db))
            .EraseApplicationsAsync(db, applicationIds, DateTimeOffset.UtcNow, Ct);
        await db.SaveChangesAsync(Ct);
        await transaction.CommitAsync(Ct);
        return result;
    }
}
