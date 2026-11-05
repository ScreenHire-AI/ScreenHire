using Microsoft.EntityFrameworkCore;
using ScreenHire.Application.Verification;
using Npgsql;
using ScreenHire.Domain.Privacy;
using static ScreenHire.IntegrationTests.CandidateTestSupport;

namespace ScreenHire.IntegrationTests.DataRequests;

/// <summary>Shared steps for the tests of the team's side of data requests and record retention.</summary>
internal static class DataRequestSupport
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>An organization with one person who applied to a job of theirs. Disposing it signs the owner out.</summary>
    public sealed record World(Tenant Owner, string Slug, Guid JobId, SyntheticApplicant Person, Guid ApplicationId, Guid CandidateId) : IDisposable
    {
        public Guid OrganizationId => Owner.OrganizationId;

        public void Dispose() => Owner.Dispose();
    }

    public static async Task<World> NewWorldAsync(this AppHostFixture fixture, string jobTitle = "Platform Engineer")
    {
        var owner = await fixture.SignedInWithOrganizationAsync();
        var job = await OpenJobTitledAsync(owner, jobTitle);
        var person = SyntheticApplicants.GenuineHuman.AsNewPerson();
        var application = (await owner.CreateApplicationAsync(job, person.Email, person.FullName)).Id();
        return new World(owner, await SlugAsync(owner), job, person, application, await CandidateOfAsync(owner, application));
    }

    /// <summary>Opens a request for a candidate as the public route would have, without its email and event.</summary>
    public static async Task<Guid> OpenRequestAsync(
        this AppHostFixture fixture, Guid organizationId, Guid candidateId, DataRequestKind kind, TimeSpan? dueIn = null)
    {
        await using var db = await VerificationSupport.TenantDbAsync(fixture, organizationId);
        var request = DataRequest.Open(organizationId, candidateId, kind, DateTimeOffset.UtcNow, dueIn ?? TimeSpan.FromDays(30));
        db.DataRequests.Add(request);
        await db.SaveChangesAsync(Ct);
        return request.Id;
    }

    public static async Task OwnerSqlAsync(this AppHostFixture fixture, string sql, params (string Name, object Value)[] parameters)
    {
        await using var owner = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await owner.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, owner);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    public static async Task<T?> OwnerScalarAsync<T>(this AppHostFixture fixture, string sql, params (string Name, object Value)[] parameters)
    {
        await using var owner = new NpgsqlConnection(await fixture.GetOwnerConnectionStringAsync(Ct));
        await owner.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, owner);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var value1 = await command.ExecuteScalarAsync(Ct);
        return value1 is null or DBNull ? default : (T)value1;
    }

    /// <summary>The request as the list shows it, or null when the list does not have it.</summary>
    public static async Task<System.Text.Json.JsonElement?> ListedAsync(this Tenant tenant, Guid requestId)
    {
        var list = (await tenant.GetAsync("/v1/data-requests")).Expect(HttpStatusCode.OK);
        foreach (var item in list.At("items").EnumerateArray())
        {
            if (item.GetProperty("id").GetGuid() == requestId)
            {
                return item;
            }
        }

        return null;
    }
}
