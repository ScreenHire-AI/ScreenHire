---
description: Add or change a minimal-API endpoint in apps/api - a Map*Endpoints area, TypedResults, DTO records in Contracts/, validation, authorization, an Aspire integration test, and the regenerated TypeScript client. Use for any new route or any change to a request or response shape.
argument-hint: [METHOD /v1/route - what it returns]
---

# Add an endpoint

The API is the product's contract (ADR-0001). Every route is described in `packages/api-client/openapi.json`, which
the API build writes and Orval turns into the TypeScript client the frontends import. So an endpoint is not done
until the client is regenerated and committed, and `pnpm verify:contract` is clean.

**Request:** $ARGUMENTS

Read `.claude/rules/dotnet-api.md` first. The closest existing examples are
`apps/api/Endpoints/PublicJobEndpoints.cs` (public, `Results<Ok<T>, NotFound>`) and
`apps/api/Endpoints/JobEndpoints.cs` (dashboard group).

## 1. Contract

Add the request and response DTOs as `public sealed record` types in `apps/api/Contracts/<Area>Contracts.cs`, one
file per area, with a `/// <summary>` on each record. Mapping from domain types goes in an `internal static class
<Area>Mappings` in the same file (`job.ToSummary()` in `JobContracts.cs` is the pattern).

- Never return a domain entity or an EF type. The DTO is the contract; the entity can change underneath it.
- Enums serialize as strings and numbers are strict (`Program.cs`), so `decimal`, `int` and enum members come out
  as clean OpenAPI types. Avoid `object`, `JsonElement`, `dynamic` and polymorphic DTOs (ADR-0008).
- Times are `DateTimeOffset` in UTC (`.claude/rules/utc-timestamps.md`).

## 2. Endpoint area

One static class per area in `apps/api/Endpoints/<Area>Endpoints.cs` with a single `Map<Area>Endpoints` extension:

```csharp
internal static class MetaEndpoints
{
    public static IEndpointRouteBuilder MapMetaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/meta").WithTags("Meta");
        group.MapGet("/", GetMeta).WithName("GetMeta");
        return app;
    }

    private static Ok<ApiMeta> GetMeta(IHostEnvironment environment) => TypedResults.Ok(...);
}
```

- Handlers are private static methods, not inline lambdas, so the route table stays readable.
- Return `TypedResults`. With more than one outcome, declare them: `Results<Ok<T>, NotFound, ValidationProblem>`.
  The OpenAPI document reads the response types from that signature.
- `.WithName("VerbNoun")` on every route. Orval names the generated function after it (`GetMeta` becomes
  `getMeta()`), so renaming it is a breaking change for every frontend.
- Take `CancellationToken cancellationToken` and pass it to every EF call. Use `AsNoTracking()` for reads.
- Inject `TimeProvider` for "now" (it is registered in `Program.cs`), never `DateTime.UtcNow`.
- Then add one line to `apps/api/Program.cs` next to the others: `app.Map<Area>Endpoints();`. Keep the edit to that
  line; `Program.cs` is a file every task touches.

## 3. Authorization

- **Public route** (careers pages, metadata): add `.AllowAnonymous()` to the group, so it stays public when sign-in
  adds a fallback policy.
- **Dashboard route:** once sign-in is on `main` (an `AddAuthorization` call in `Program.cs`), add
  `.RequireAuthorization()` to the group. Until then keep the placeholder comment `JobEndpoints.cs` carries and
  filter by organization explicitly; the route must not be deployed.
- Every query on a tenant-owned table is scoped to one organization. Before tenancy lands that is an explicit
  `OrganizationId` filter; after it, the named query filter does it and an explicit filter is redundant, not wrong.

## 4. Validation

Request bodies and query parameters are validated with FluentValidation (ADR-0008) and a failure returns
`TypedResults.ValidationProblem(errors)` (HTTP 400 with a problem-details body). If FluentValidation is not in
`Directory.Packages.props` yet, the first endpoint that needs it adds it: add the version there, reference it from
`apps/api/ScreenHire.Api.csproj`, and run the `licence-check` agent. Route-only endpoints like `GET /v1/meta` need
none.

Bad input is a 400, a missing or other-tenant row is a 404 (never 403, which confirms the row exists), and anything
unexpected throws and becomes a 500 problem-details response through `UseExceptionHandler`.

## 5. Integration test

Integration tests boot the whole AppHost with Docker (`docker info` must succeed). Add a test class per area in
`tests/ScreenHire.IntegrationTests/<Area>Tests.cs`:

- If the project has a shared Aspire fixture (see `docs/process/testing.md`), use it. Otherwise copy the boot sequence
  of `PublicJobsTests.cs` exactly: `DistributedApplicationTestingBuilder.CreateAsync<Projects.ScreenHire_AppHost>`
  with `--ScreenHire:PersistData=false` and `--ScreenHire:PinPorts=false`, wait for `api` to be healthy, then
  `app.CreateHttpClient("api", "http")`.
- Assert the status code and the JSON body. Cover the error paths you declared (404, 400), not only the happy path.
- Name tests as sentences: `Unknown_organization_returns_not_found`.

Run only that class while you work:

```
dotnet test --project tests/ScreenHire.IntegrationTests --filter-class "*MetaTests"
```

## 6. Regenerate the client

```
pnpm generate:api
git add packages/api-client
pnpm verify:contract
```

`generate:api` builds the API (which rewrites `openapi.json`) and runs Orval into `packages/api-client/src/generated`.
Never edit the generated files by hand. Every build of the API rewrites `openapi.json`, including test runs, so run
this step last, after any experiment such as unmapping a route to see a test fail, and stage what it produces. Read the `openapi.json` diff: it should show your new path and schemas and
nothing else. `verify:contract` regenerates again and fails if anything differs from what is staged.

Also add a request to `apps/api/ScreenHire.Api.http` so the route can be tried from the editor.

## 7. Done when

- `pnpm verify:dotnet` passes (warnings are errors).
- Your test class passes, and `pnpm verify:contract` is clean with the generated files committed.
- A frontend that calls the route imports the generated function from `@screenhire/api-client`, never a hand-written
  `fetch`.
