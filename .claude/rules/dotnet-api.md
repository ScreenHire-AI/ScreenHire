---
paths:
  - "apps/api/**"
  - "apps/worker/**"
  - "apps/apphost/**"
  - "src/**"
  - "tests/**"
  - "Directory.Packages.props"
---
# Rule: .NET backend conventions

CLAUDE.md lists the backend rules in one line each. This file is the detail behind them. The step-by-step
procedures are the `/add-endpoint` and `/add-migration` skills.

## Projects and dependencies

- .NET 10, nullable on, analyzers at `latest-recommended`, and `pnpm verify:dotnet` builds with warnings as errors
  (`Directory.Build.props`). Fix a warning; do not suppress it unless it is a known false positive, with a comment
  saying why.
- `src/ScreenHire.Domain` has no framework dependencies: no EF Core, no ASP.NET, no `TimeProvider`. Persistence
  mapping lives in `src/ScreenHire.Infrastructure/Persistence/Configurations`, one `IEntityTypeConfiguration` per
  entity.
- Package versions live only in `Directory.Packages.props` (central package management); project files reference
  packages without a version. Every new package passes the `licence-check` agent against ADR-0008 (CLAUDE.md keeps
  the list of banned packages).

## API

- Minimal APIs. One static class per area under `apps/api/Endpoints` with one `Map<Area>Endpoints` extension, mapped
  from `Program.cs`. Handlers are private static methods returning `TypedResults` (`Results<Ok<T>, NotFound>` when
  there is more than one outcome). Every route has `.WithName(...)`, which becomes the generated client's function
  name.
- DTOs are `public sealed record` types in `apps/api/Contracts/<Area>Contracts.cs`. Never return domain entities.
  Mapping is a hand-written extension method next to the DTO, or Mapperly once mappings grow (ADR-0008).
  The exception is domain value records that are themselves the published contract: the hiring definition records
  in `src/ScreenHire.Domain/Hiring/HiringWorkflow.cs` (the API serves their JSON Schema) and the job value records
  `JobLocation`, `JobSalary` and `JobSnapshot`. DTOs embed those as they are; entities never appear in a DTO.
- Enums serialize as strings and `JsonNumberHandling.Strict` is on. Avoid polymorphic DTOs and `object` members:
  they produce OpenAPI that Orval cannot type.
- Errors are problem details: `TypedResults.ValidationProblem` for bad input (validated with FluentValidation,
  ADR-0008), `NotFound` for a missing row or another tenant's row (never 403, which leaks existence), and
  unhandled exceptions become 500s through `UseExceptionHandler`. Never put exception text in a response.
- Every route requires a signed-in user (a fallback policy) unless it calls `.AllowAnonymous()`. Public routes do;
  dashboard groups also say `.RequireAuthorization()`, for the reader. Cookie-authenticated mutations add
  `.RequireAntiforgeryToken()`. Details: `docs/architecture/sign-in-and-tenancy.md` and `/add-endpoint`.
- After any endpoint or contract change: `pnpm generate:api`, then commit `openapi.json` and `src/generated`.

## Data

- EF Core with Npgsql, snake_case names (`UseScreenHireConventions()`), enums stored as text with a max length.
- Every tenant-owned entity implements `ITenantOwned` (so it gets the named query filter `Tenant`), has an index that
  starts with `OrganizationId`, and a Postgres RLS policy keyed on `app.tenant_id`, which `TenantSessionInterceptor`
  sets transaction-locally with `set_config('app.tenant_id', ..., true)`. Handlers do not filter by organization
  themselves. The API and worker connect as `screenhire_app`, bound by RLS; only migrations run as the owner.
- Inside an explicit transaction, do not stream a query (`AsAsyncEnumerable`) while running another: the interceptor
  sends its own command first, and Npgsql allows one open reader per connection.
- Never call `IgnoreQueryFilters()` in an endpoint. Only the audited, feature-flagged Signal Network path (ADR-0005)
  reads across organizations.
- Times are `DateTimeOffset` from `TimeProvider` (`.claude/rules/utc-timestamps.md`).
- Reads use `AsNoTracking()`. Pass the request's `CancellationToken` to every async EF call. Load what the response
  needs in one query; never query inside a loop. The exception is a system message that walks every organization
  in turn (`ReportOpenJobs`): row-level security gives the worker no cross-tenant read, so it sets each tenant and
  queries once per organization.
- Connect to Postgres directly, never through transaction-mode PgBouncer: Wolverine leader election and
  transaction-local tenant settings need a real session.

## Background work and integrations

- Background and asynchronous work is Wolverine with the Postgres outbox, so a message commits in the same
  transaction as the data. No `BackgroundService` polling loops for product work. Contracts and handlers live in
  `src/ScreenHire.Application`; the API publishes through `IDbContextOutbox<ScreenHireDbContext>` and never handles;
  the worker handles. Procedure: `/add-message`. Design and idempotency: `docs/architecture/messaging.md`.
- Handlers are idempotent: a state check, unique index or upsert for the effect, plus `[Deduplicated]` on an
  idempotency key when one intent can arrive in two envelopes. Never swallow an exception in a handler; the retry
  policy and the dead-letter table depend on it.
- Every external integration (email, OIDC, LLMs, job boards, IP intelligence) sits behind an interface with a local
  adapter: a container in the AppHost or an in-process fake. A provider with no configuration is hidden, never an
  error, and no test needs a key or the internet.
- Hosts and domains come from configuration (`ScreenHire:Domains:*` through `DomainOptions`, ADR-0003). Never
  hard-code a `screenhire.*` host. Email goes through `IEmailSender`; check it is registered before offering a
  feature that sends mail.
- Logs carry ids, never personal data: no names, emails, resume text, answers or raw IP addresses in log messages.

## Tests

- Unit tests (xUnit v3 + AwesomeAssertions) for domain logic and pure functions. Aspire integration tests in
  `tests/ScreenHire.IntegrationTests` for every endpoint and data rule. Tier choice and fixtures are the
  `test-builder` agent's job; `docs/process/testing.md` describes the harness (shared `AppHostFixture`, data isolation,
  running one test).
