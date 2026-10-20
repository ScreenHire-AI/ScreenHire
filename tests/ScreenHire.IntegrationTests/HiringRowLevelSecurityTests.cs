using Microsoft.EntityFrameworkCore;
using Npgsql;
using ScreenHire.Domain.Applications;
using ScreenHire.Domain.Candidates;
using ScreenHire.Domain.Hiring;
using ScreenHire.Domain.Jobs;
using ScreenHire.Domain.Organizations;
using ScreenHire.Infrastructure.Persistence;
using ScreenHire.Infrastructure.Tenancy;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// The hiring model's data rules in the database itself, as the <c>screenhire_app</c> role the API and worker use: the
/// append-only tables, immutable published template versions, read-only system templates, and the keys and triggers that
/// stop a row from pointing into another organization. They hold even if an endpoint forgot its check.
/// </summary>
public sealed class HiringRowLevelSecurityTests(AppHostFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Job_versions_can_be_added_and_read_but_never_changed_or_deleted()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var job = TestData.NewJob(organization, "versioned");
        await using (var db = await ContextAsync(organization.Id))
        {
            db.Jobs.Add(job);
            db.JobVersions.Add(job.Publish(Now)!);
            await db.SaveChangesAsync(Ct);
        }

        var update = () => ExecuteAsync(organization.Id, "UPDATE job_versions SET version = 99");
        var delete = () => ExecuteAsync(organization.Id, "DELETE FROM job_versions");

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        await using var check = await ContextAsync(organization.Id);
        (await check.JobVersions.Select(v => v.Version).ToListAsync(Ct)).Should().Equal(1);
    }

    [Fact]
    public async Task A_publish_that_loaded_the_job_before_it_was_closed_cannot_reopen_it()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var job = TestData.NewJob(organization, "closed-meanwhile");
        await using (var db = await ContextAsync(organization.Id))
        {
            db.Jobs.Add(job);
            await db.SaveChangesAsync(Ct);
        }

        await using var publisher = await ContextAsync(organization.Id);
        var stale = await publisher.Jobs.SingleAsync(j => j.Id == job.Id, Ct);
        await using (var closer = await ContextAsync(organization.Id))
        {
            (await closer.Jobs.SingleAsync(j => j.Id == job.Id, Ct)).Close(Now).Should().BeTrue();
            await closer.SaveChangesAsync(Ct);
        }

        publisher.JobVersions.Add(stale.Publish(Now)!);
        var publish = () => publisher.SaveChangesAsync(Ct);

        await publish.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var check = await ContextAsync(organization.Id);
        (await check.Jobs.SingleAsync(j => j.Id == job.Id, Ct)).Status.Should().Be(JobStatus.Closed);
        (await check.JobVersions.CountAsync(v => v.JobId == job.Id, Ct)).Should().Be(0);
    }

    [Fact]
    public async Task A_second_publish_of_one_draft_matches_no_row_and_changes_nothing()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var template = await CreateTemplateAsync(organization.Id, "Published twice");
        await using var second = await ContextAsync(organization.Id);
        var stale = await second.HiringTemplates.Include(t => t.Versions).SingleAsync(t => t.Id == template.Id, Ct);
        await using (var first = await ContextAsync(organization.Id))
        {
            var fresh = await first.HiringTemplates.Include(t => t.Versions).SingleAsync(t => t.Id == template.Id, Ct);
            fresh.Publish(fresh.Versions[0], Now, null);
            await first.SaveChangesAsync(Ct);
        }

        stale.Publish(stale.Versions[0], Now.AddMinutes(1), null);
        var publish = () => second.SaveChangesAsync(Ct);

        await publish.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var check = await ContextAsync(organization.Id);
        (await check.HiringTemplateVersions.SingleAsync(v => v.TemplateId == template.Id, Ct)).PublishedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Application_events_can_be_added_and_read_but_never_changed_or_deleted()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var job = TestData.NewJob(organization, "applied-to");
        var candidate = Candidate.Create(organization.Id, "Casey Morgan", "casey.morgan@example.com", null, Now);
        var (application, submitted) = JobApplication.Submit(job, candidate, false, AiUseDeclaration.NotProvided, null, Now, null);
        await using (var db = await ContextAsync(organization.Id))
        {
            db.Jobs.Add(job);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);
            db.ApplicationEvents.Add(submitted);
            await db.SaveChangesAsync(Ct);
        }

        var update = () => ExecuteAsync(organization.Id, "UPDATE application_events SET occurred_at = now()");
        var delete = () => ExecuteAsync(organization.Id, "DELETE FROM application_events");

        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        await using var check = await ContextAsync(organization.Id);
        (await check.ApplicationEvents.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task A_template_version_can_change_while_it_is_a_draft_and_never_after_it_is_published()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        Guid versionId;
        await using (var db = await ContextAsync(organization.Id))
        {
            var template = HiringTemplate.Create(organization.Id, "Immutable once published", null, HiringTemplateDefinition.Default, Now, null);
            db.HiringTemplates.Add(template);
            await db.SaveChangesAsync(Ct);
            versionId = template.Versions[0].Id;
        }

        var touch = () => ExecuteAsync(organization.Id, "UPDATE hiring_template_versions SET created_at = created_at WHERE id = @id", ("id", versionId));
        var remove = () => ExecuteAsync(organization.Id, "DELETE FROM hiring_template_versions WHERE id = @id", ("id", versionId));
        (await touch()).Should().Be(1, "a draft can be updated");

        await using (var db = await ContextAsync(organization.Id))
        {
            var template = await db.HiringTemplates.Include(t => t.Versions).SingleAsync(t => t.OrganizationId == organization.Id, Ct);
            template.Publish(template.Versions[0], Now, null);
            await db.SaveChangesAsync(Ct);
        }

        (await touch()).Should().Be(0, "no policy lets a published version be updated");
        (await remove()).Should().Be(0, "no policy lets a published version be deleted");
        await using var check = await ContextAsync(organization.Id);
        (await check.HiringTemplateVersions.CountAsync(v => v.Id == versionId, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task A_draft_template_version_can_be_deleted()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        Guid versionId;
        await using (var db = await ContextAsync(organization.Id))
        {
            var template = HiringTemplate.Create(organization.Id, "Draft only", null, HiringTemplateDefinition.Default, Now, null);
            db.HiringTemplates.Add(template);
            await db.SaveChangesAsync(Ct);
            versionId = template.Versions[0].Id;
        }

        (await ExecuteAsync(organization.Id, "DELETE FROM hiring_template_versions WHERE id = @id", ("id", versionId))).Should().Be(1);
    }

    [Fact]
    public async Task A_template_cannot_be_deleted_because_that_would_cascade_to_its_published_versions()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        Guid templateId;
        await using (var db = await ContextAsync(organization.Id))
        {
            var template = HiringTemplate.Create(organization.Id, "Permanent", null, HiringTemplateDefinition.Default, Now, null);
            db.HiringTemplates.Add(template);
            await db.SaveChangesAsync(Ct);
            templateId = template.Id;
        }

        var delete = () => ExecuteAsync(organization.Id, "DELETE FROM hiring_templates WHERE id = @id", ("id", templateId));

        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        await using var check = await ContextAsync(organization.Id);
        (await check.HiringTemplates.CountAsync(t => t.Id == templateId, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task System_templates_and_their_versions_cannot_be_changed_or_deleted_by_a_tenant()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var template = ("id", (object)SystemTemplates.SoftwareEngineer);
        var version = ("id", (object)SystemTemplates.SoftwareEngineerVersion1);

        var renamed = await ExecuteAsync(organization.Id, "UPDATE hiring_templates SET name = 'Taken over' WHERE id = @id", template);
        var versionChanged = await ExecuteAsync(
            organization.Id, "UPDATE hiring_template_versions SET created_at = created_at WHERE id = @id", version);
        var versionDeleted = await ExecuteAsync(organization.Id, "DELETE FROM hiring_template_versions WHERE id = @id", version);
        var deleteTemplate = () => ExecuteAsync(organization.Id, "DELETE FROM hiring_templates WHERE id = @id", template);

        renamed.Should().Be(0);
        versionChanged.Should().Be(0);
        versionDeleted.Should().Be(0);
        (await deleteTemplate.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        await using var check = await ContextAsync(organization.Id);
        (await check.HiringTemplates.Where(t => t.Id == SystemTemplates.SoftwareEngineer).Select(t => t.Name).SingleAsync(Ct))
            .Should().Be("Software engineer");
        (await check.HiringTemplateVersions.CountAsync(v => v.Id == SystemTemplates.SoftwareEngineerVersion1, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task System_templates_are_readable_by_every_tenant()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);

        await using var db = await ContextAsync(organization.Id);

        (await db.HiringTemplates.Where(t => t.OrganizationId == null).CountAsync(Ct)).Should().Be(2);
        (await db.HiringTemplateVersions.Where(v => v.OrganizationId == null).CountAsync(Ct)).Should().Be(3, "two system templates, one of them with a second version");
    }

    [Fact]
    public async Task A_tenant_cannot_add_a_version_to_a_system_template()
    {
        var organization = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        await using var db = await ContextAsync(organization.Id);
        db.HiringTemplateVersions.Add(new HiringTemplateVersion
        {
            OrganizationId = organization.Id,
            TemplateId = SystemTemplates.SoftwareEngineer,
            Version = 2,
            Definition = HiringTemplateDefinition.Default,
            CreatedAt = Now,
        });

        var save = () => db.SaveChangesAsync(Ct);

        (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation, "a version must belong to its template's organization");
    }

    [Fact]
    public async Task A_tenant_cannot_add_a_version_to_another_organizations_template()
    {
        var mine = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var theirs = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var theirTemplate = await CreateTemplateAsync(theirs.Id, "Theirs");
        await using var db = await ContextAsync(mine.Id);
        db.HiringTemplateVersions.Add(new HiringTemplateVersion
        {
            OrganizationId = mine.Id,
            TemplateId = theirTemplate.Id,
            Version = 2,
            Definition = HiringTemplateDefinition.Default,
            CreatedAt = Now,
        });

        var save = () => db.SaveChangesAsync(Ct);

        (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task A_job_can_use_a_system_template_version_but_not_another_organizations()
    {
        var mine = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var theirs = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var theirTemplate = await CreateTemplateAsync(theirs.Id, "Theirs", publish: true);
        HiringTemplateVersion theirVersion;
        await using (var theirDb = await ContextAsync(theirs.Id))
        {
            theirVersion = await theirDb.HiringTemplateVersions.AsNoTracking().SingleAsync(v => v.TemplateId == theirTemplate.Id, Ct);
        }

        await using var db = await ContextAsync(mine.Id);
        var systemVersion = await db.HiringTemplateVersions.AsNoTracking().SingleAsync(v => v.Id == SystemTemplates.SoftwareEngineerVersion1, Ct);
        var fromSystem = TestData.NewJob(mine, "from-system");
        fromSystem.UseTemplate(systemVersion);
        db.Jobs.Add(fromSystem);
        await db.SaveChangesAsync(Ct);

        var fromTheirs = TestData.NewJob(mine, "from-theirs");
        fromTheirs.UseTemplate(theirVersion);
        db.Jobs.Add(fromTheirs);
        var save = () => db.SaveChangesAsync(Ct);

        (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation, "the job's template source must be a system version or its own");
        db.ChangeTracker.Clear();
        (await db.Jobs.Select(j => j.Slug).ToListAsync(Ct)).Should().Equal("from-system");
    }

    [Fact]
    public async Task An_application_cannot_point_at_another_organizations_job()
    {
        var mine = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var theirs = await fixture.CreateOrganizationAsync(org => [TestData.NewJob(org, "theirs")], Ct);
        var candidate = Candidate.Create(mine.Id, "Casey Morgan", "casey.morgan@example.com", null, Now);
        await using var db = await ContextAsync(mine.Id);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync(Ct);
        await using var theirDb = await ContextAsync(theirs.Id);
        var theirJobId = await theirDb.Jobs.Select(j => j.Id).SingleAsync(Ct);

        // A job object that carries the other organization's id but this organization's id as owner, so the application
        // names (my organization, their job). Only the application is saved.
        var stand = new Job { Id = theirJobId, OrganizationId = mine.Id, Title = "Stand-in", Slug = "stand-in" };
        var (application, _) = JobApplication.Submit(stand, candidate, false, AiUseDeclaration.NotProvided, null, Now, null);
        db.Applications.Add(application);
        var save = () => db.SaveChangesAsync(Ct);

        var failure = (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().Which;
        failure.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        failure.ConstraintName.Should().Contain("jobs", "the composite (organization_id, job_id) key is the one that failed");
    }

    [Fact]
    public async Task An_application_cannot_point_at_another_organizations_candidate()
    {
        var mine = await fixture.CreateOrganizationAsync(org => [TestData.NewJob(org, "mine")], Ct);
        var theirs = await fixture.CreateOrganizationAsync(cancellationToken: Ct);
        var theirCandidate = Candidate.Create(theirs.Id, "Sam Taylor", "sam.taylor@example.com", null, Now);
        await using (var theirDb = await ContextAsync(theirs.Id))
        {
            theirDb.Candidates.Add(theirCandidate);
            await theirDb.SaveChangesAsync(Ct);
        }

        await using var db = await ContextAsync(mine.Id);
        var job = await db.Jobs.AsNoTracking().SingleAsync(Ct);
        var stand = new Candidate { Id = theirCandidate.Id, OrganizationId = mine.Id, FullName = "Stand-in", Email = "stand-in@example.com" };
        var (application, _) = JobApplication.Submit(job, stand, false, AiUseDeclaration.NotProvided, null, Now, null);
        db.Applications.Add(application);
        var save = () => db.SaveChangesAsync(Ct);

        var failure = (await save.Should().ThrowAsync<DbUpdateException>()).WithInnerException<PostgresException>().Which;
        failure.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        failure.ConstraintName.Should().Contain("candidates");
    }

    private async Task<ScreenHireDbContext> ContextAsync(Guid organizationId)
    {
        var tenant = new TenantContext();
        tenant.SetOrganization(organizationId);
        return await fixture.CreateDbContextAsync(tenant, Ct);
    }

    private async Task<HiringTemplate> CreateTemplateAsync(Guid organizationId, string name, bool publish = false)
    {
        await using var db = await ContextAsync(organizationId);
        var template = HiringTemplate.Create(organizationId, name, null, HiringTemplateDefinition.Default, Now, null);
        if (publish)
        {
            template.Publish(template.Versions[0], Now, null);
        }

        db.HiringTemplates.Add(template);
        await db.SaveChangesAsync(Ct);
        return template;
    }

    // One statement as the application role with the organization as tenant, in a transaction like the API's.
    private async Task<int> ExecuteAsync(Guid organizationId, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(await fixture.GetAppConnectionStringAsync(Ct));
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, true)", connection, transaction))
        {
            setTenant.Parameters.AddWithValue("tenant", organizationId.ToString());
            await setTenant.ExecuteNonQueryAsync(Ct);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var affected = await command.ExecuteNonQueryAsync(Ct);
        await transaction.CommitAsync(Ct);
        return affected;
    }
}
