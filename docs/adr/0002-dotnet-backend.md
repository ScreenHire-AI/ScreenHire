# ADR-0002: ASP.NET Core on .NET 10 LTS for API and workers

- **Status:** Accepted (2026-10-18)
- **Deciders:** Owner

## Context
The owner is highly fluent in C#/.NET. The backend must be fast, cheap to run, and support long-running durable work
(job-board sync with Indeed's 15-minute expectation, LLM analysis, scheduled workflow timers).

## Decision
- **ASP.NET Core on .NET 10 (LTS)** for the API and the worker. Track .NET 11 (STS, GA expected Nov 2026) but do not
  adopt until the next LTS unless a specific feature justifies it.
- **PostgreSQL** as the system of record (JSONB for workflow definitions and evidence; pgvector for near-duplicate
  detection). Valkey optional.
- **Object storage** for resumes and (later) media behind an `IObjectStore` abstraction (Azure Blob in production per
  ADR-0007; S3-compatible implementations remain possible for portability).
- Containers everywhere; no provider-specific runtime APIs, so hosting stays portable (ADR-0007).
- Library choices are made under a **licence policy**: prefer MIT/Apache/BSD; any library with a commercial threshold
  must be explicitly approved in ADR-0008.

## Consequences
- One backend language; strong performance per dollar.
- No shared C# types with the TS frontends — bridged by OpenAPI code generation (ADR-0001).
