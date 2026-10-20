using Npgsql;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Automations;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests.Automations;

/// <summary>
/// The workflow engine's tables in the database itself, as the <c>screenhire_app</c> role the API and worker use: each
/// organization sees only its own runs, actions and tasks, cannot write a row into another organization, and cannot
/// delete the audit record of what an automation did.
/// </summary>
public sealed class AutomationRowLevelSecurityTests(AppHostFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] Tables = ["automation_runs", "automation_action_runs", "application_tasks"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Another_organization_sees_none_of_the_runs_actions_or_tasks()
    {
        var mine = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var theirs = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var (application, _) = await SeedRunAsync(mine);

        foreach (var table in Tables)
        {
            (await CountAsync(mine.Id, $"SELECT count(*) FROM {table} WHERE organization_id = @org", ("org", mine.Id)))
                .Should().Be(1, $"the organization sees its own {table} row");
            (await CountAsync(theirs.Id, $"SELECT count(*) FROM {table} WHERE organization_id = @org", ("org", mine.Id)))
                .Should().Be(0, $"row-level security hides another organization's {table} rows");
        }

        using var other = await fixture.SignedInWithOrganizationAsync();
        (await other.GetAsync($"/v1/applications/{application.Id}/tasks")).Expect(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_row_cannot_be_written_into_another_organization()
    {
        var mine = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var theirs = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var (application, _) = await SeedRunAsync(mine);

        var insert = () => ExecuteAsync(theirs.Id,
            "INSERT INTO application_tasks (id, organization_id, application_id, kind, note, created_at) VALUES (@id, @org, @app, 'Task', 'x', now())",
            ("id", Guid.CreateVersion7()), ("org", mine.Id), ("app", application.Id));

        (await insert.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Theory]
    [InlineData("automation_runs")]
    [InlineData("automation_action_runs")]
    public async Task The_audit_record_of_a_run_cannot_be_deleted(string table)
    {
        var mine = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        await SeedRunAsync(mine);

        var delete = () => ExecuteAsync(mine.Id, $"DELETE FROM {table}");

        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await CountAsync(mine.Id, $"SELECT count(*) FROM {table}")).Should().Be(1);
    }

    // An application with one recorded run, one pending action and one task, written as the organization.
    private async Task<(JobApplication Application, AutomationRun Run)> SeedRunAsync(Organization organization)
    {
        var job = TestData.NewJob(organization, $"automated-{Guid.NewGuid():N}");
        var candidate = Candidate.Create(organization.Id, "Casey Morgan", "casey.morgan@example.com", null, Now);
        var (application, submitted) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Now, null);
        var automation = new Automation("tag", "Tag", new AutomationTrigger(TriggerType.Manual), null, [new AutomationAction(ActionType.AddTag, Tag: "x")]);
        var trigger = new AutomationTriggerEvent(organization.Id, application.Id, job.Id, TriggerType.Manual, null, Guid.CreateVersion7(), Now);
        var (run, actions) = AutomationRun.Record(
            trigger, automation, 1, AutomationFacts.None, AutomationEvaluator.Evaluate(automation, AutomationFacts.None, false, []), [], Now);

        var tenant = new TenantContext();
        tenant.SetOrganization(organization.Id);
        await using var db = await fixture.CreateDbContextAsync(tenant, Ct);
        db.Jobs.Add(job);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.ApplicationEvents.Add(submitted);
        db.AutomationRuns.Add(run);
        db.AutomationActionRuns.AddRange(actions);
        db.ApplicationTasks.Add(new ApplicationTask
        {
            OrganizationId = organization.Id, ApplicationId = application.Id, Kind = ApplicationTaskKind.Task, Note = "Call back", AutomationRunId = run.Id, CreatedAt = Now,
        });
        await db.SaveChangesAsync(Ct);
        return (application, run);
    }

    private async Task<long> CountAsync(Guid organizationId, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsTenantAsync(organizationId);
        await using var command = Command(connection, sql, parameters);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<int> ExecuteAsync(Guid organizationId, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsTenantAsync(organizationId);
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync(Ct);
    }

    // The application role with the organization as tenant for the session, as the API's interceptor sets it.
    private async Task<NpgsqlConnection> OpenAsTenantAsync(Guid organizationId)
    {
        var connection = new NpgsqlConnection(await fixture.GetAppConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, false)", connection);
        setTenant.Parameters.AddWithValue("tenant", organizationId.ToString());
        await setTenant.ExecuteNonQueryAsync(Ct);
        return connection;
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }
}
