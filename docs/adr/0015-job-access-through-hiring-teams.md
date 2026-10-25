---
type: Decision
title: "ADR-0015: Job access through each job's hiring team"
description: "A member sees all jobs, or only the jobs whose hiring team they are on; the scope is a transaction-local setting that restrictive row-level security policies and a JobScope query filter both apply; Owners, Admins, API keys and background work see every job; live updates go to per-job groups."
tags: [adr, roles, permissions, tenancy, realtime]
---
# ADR-0015: Job access through each job's hiring team

- **Status:** Accepted (2026-10-25)
- **Deciders:** Owner
- **Builds on:** ADR-0008 (named query filters and row-level security), ADR-0011 (API keys), ADR-0014 (permissions and custom roles)
- **Detail:** `docs/architecture/sign-in-and-tenancy.md`, "Job access", and `docs/architecture/realtime.md`

## Context

ADR-0014 decides what a person may do. Every member could still see every job. A hiring manager or an agency
recruiter should answer candidates for their own jobs without reading the others, and the MCP server (roadmap item 9)
must inherit the same limit without code of its own. The limit therefore belongs where tenant isolation lives: in the
query filters and row-level security that every read passes through.

## Decision

1. **A job has a hiring team; a member has a job access.** `jobs.hiring_team` is a `uuid[]` of organization member ids.
   `organization_members.job_access` is `All` (the default, so existing members keep every job) or `HiringTeamOnly`.
   Built-in Owners and Admins see all jobs whatever it says; every other role, custom ones included, follows it.
   Invitations carry a job access too, `All` unless the inviter picks Only jobs they're on.
2. **The team is a column on the job, not a table.** `jobs` returns `xmin` on insert, and Postgres checks a `RETURNING`
   row against the table's SELECT policies. With the creator's id in the row, a member on Only jobs they're on can
   create a job and see it, in one insert. A team table would also need a policy that reads `jobs`, whose own policy
   reads the team table: a policy cycle. Ids are membership ids, not user ids, so someone who leaves and rejoins gets a
   new id and no old team counts for them again. Ids of people who left are ignored on read, never cleaned up.
3. **The scope is a transaction-local setting.** `TenantSessionInterceptor` sets `app.job_scope` (`all` or `team`) and
   `app.member_id` beside `app.tenant_id`. With no user (an API key, a worker handler, the careers pages, the seeders)
   it is `all`: an API key has no member and stays organization-wide, and background work runs organization-wide. With
   a user it is `team` until the request resolves their membership (`Memberships`), so a path that forgets fails
   closed and sees no jobs. A public route (careers, feeds, the public board) acts for the organization in its URL,
   never for a signed-in visitor (`TenantContext.SetPublicOrganization`).
4. **Row-level security narrows with restrictive policies.** Migration `AddJobAccess` adds a `job_scope` policy, `AS
   RESTRICTIVE` and `TO screenhire_app`, to `jobs` and to every table under a job or an application, through
   `app_sees_job` and `app_sees_application`. Restrictive policies are ANDed with `tenant_isolation`, so they can only
   narrow it. The owner, who runs migrations, is bound by the tenant policy alone.
5. **The EF filter mirrors it.** A second named filter, `JobScope`, sits beside `Tenant` on jobs, every `IJobScoped` and
   `IApplicationScoped` entity, note mentions, automation action runs, indexing notifications and candidates.
6. **Two tables keep tenant-only row-level security.** Candidates: one person who applied to two jobs is one row, found
   by email when they apply again, so `ApplicationSubmission` looks them up past `JobScope` (by name, keeping `Tenant`)
   and the EF filter hides them otherwise. A member on Only jobs they're on who adds someone already a candidate only on
   jobs they cannot see gets 409, naming nobody: attaching them would hand over that person's stored details. `change_records`: its subject is a kind and an id with no foreign key, a new
   job's history row is saved ahead of the job, and a job's history is read only through the job, which is a 404 when
   hidden.
7. **Nobody gives more than they see.** `TeamPolicy.MayGiveJobAccess` extends ADR-0014's subset rule: a member on Only
   jobs they're on cannot give All jobs, invite with it, or give the Owner or Admin role. Manage API keys and Manage
   webhooks need all jobs (`RolePermissions.NeedsAllJobs`), because keys and webhooks reach every job. A member on Only
   jobs they're on cannot take themselves off a team.
8. **Teams change through the job, and land in the membership log.** `PUT /v1/jobs/{jobId}/hiring-team` (Edit jobs)
   sets the team, optionally copying another visible job's. A hiring template's default team
   (`hiring_templates.default_hiring_team`, Edit hiring templates) is where a new job's team starts, with its creator
   added. Both are session only: who sees a job's candidates is not an API key's to change. Each person added or
   removed, and each change of job access, writes a membership log entry; the log leaves out entries about jobs the
   reader cannot see.
9. **Live updates go to per-job groups.** Every event that carries a job id implements `IJobEvent` and goes to
   `org:{org}:all-jobs` and `org:{org}:team:{jobId}`. A connection joins one of the two kinds when it opens. Joining a
   team adds the job's group to the person's open connections; leaving one, or a change of job access or of a role
   across Admin, closes their connections, so they reconnect into the right groups and refetch. Filtering each event
   per connection was rejected: it costs a database read per event, and the connection list is per process.

## Consequences

- The dashboard, the API and the MCP server inherit the scope with no code of their own, because it is applied in the
  filters and the database.
- Mentions name only people who can see the candidate, and the mention email is skipped for someone who no longer
  can. Tasks, saved views, search and notifications follow the scope because they read through the same filters or
  arrive through the same groups.
- A member on Only jobs they're on cannot see the slugs of other jobs, so a job they create gets a slug ending in part
  of its id. It can change until the job is published.
- Two answers still say that something hidden exists, without naming it: the 409 for a candidate only on hidden jobs
  (decision 6), and the 409 for a slug they typed that a hidden job already has. We accept both. Each tells a member
  something about their own organization, never its contents.
- A hiring team save replaces the whole team, and the last save wins. Two people editing the same team at once can
  undo each other's change; both changes are in the membership log.
- A task an automation assigns to someone not on the job's team stays hidden from them until they join it.
- Hiring team changes are not in a job's change history; their home is the membership log.
