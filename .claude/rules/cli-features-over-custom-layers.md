# Rule: use the platform's own feature before writing a layer

Every tool in this stack already ships the feature most tasks are tempted to build: Aspire wires ports, connection
strings and containers; ASP.NET Core has problem details, OpenAPI, rate limiting and authorization; EF Core has
named query filters and migrations; Orval generates the client; Claude Code has skills, agents, rules and hooks. A
custom layer on top of a working native feature is code to maintain, and it drifts from the feature it shadows.

## The rule

- Reach for the native feature first, and write a layer only when the native feature cannot do the job. Say why in
  a comment when you do.
- Configuration flows through Aspire (`WithReference`, `WithEnvironment`, parameters) and `IConfiguration`. No
  hand-rolled `.env` loaders, port tables or service-discovery shims.
- No hand-written `fetch` wrappers for API routes: call the generated function from `@screenhire/api-client`.
- No custom process supervisors, background loops or retry wrappers. Background work is Wolverine (CLAUDE.md), HTTP
  resilience is `Microsoft.Extensions.Http.Resilience` (ADR-0008).
- Claude Code tooling uses Claude Code's own mechanisms (a skill, an agent, a `paths:`-scoped rule, a hook) rather
  than scripts that imitate them.
- **Never keep a list of model names or ids** for an LLM integration. Model ids come from configuration, read at
  runtime. A stale list offers models the provider no longer serves and hides the ones it does. The same goes for
  any per-model table, such as context-window sizes.

## Enforcement

- **Review.** `/task-review`'s conventions dimension flags a layer that duplicates a native feature.

## Scope

All code and tooling in the repository.
