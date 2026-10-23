# ScreenHire

An affordable applicant tracking system for small hiring teams that knows who is really applying: it detects bots,
impersonators and AI-generated applications and explains every signal in plain language. Evidence, never verdicts.

- **Docs:** [`docs/index.md`](docs/index.md) is the map; [`docs/README.md`](docs/README.md) says where to start. The MVP status, decisions (ADRs), architecture, design system and research are all listed there.
- **Agent guide:** [`CLAUDE.md`](CLAUDE.md).

## Quick start

Prerequisites: .NET SDK 10.0.400+, Node 22.22+, pnpm 10, Docker running, and a trusted .NET development certificate
(`dotnet dev-certs https --trust`). On Windows, clone to a short path such as `C:\src\ScreenHire`.

```bash
pnpm install   # once, at the repo root
pnpm dev       # runs the Aspire AppHost: Postgres (pgvector), Azurite, Mailpit, Dev SSO, API, worker and frontends
```

In Claude Code, `/preview` runs it through the Aspire CLI (`dotnet tool restore` once, then `dotnet aspire start`),
waits until the demo data is in and reports the URLs and logins; `/preview fresh` gives a clean demo and
`/preview stop` stops it ([docs/operations/local-demo.md](docs/operations/local-demo.md)).

| Service | URL |
|---|---|
| Recruiter dashboard | http://localhost:4323 |
| Careers pages (demo employer) | http://localhost:4322/brightwater |
| Marketing site | http://localhost:4321 |
| API + Scalar reference | http://localhost:5080/scalar |
| Mailpit (every email the stack sends) | http://localhost:8025 |
| Dev SSO (local sign-in provider) | http://localhost:5090 |
| Aspire dashboard | https://localhost:17113 (the console prints the link with its login token) |

Sign in to the dashboard with **Dev SSO**: "Avery Quinn" owns the seeded Brightwater Studio, with open jobs on their
boards, a draft to publish, a closed job, candidates in every pipeline stage and synthetic applicants at every
integrity level. Any other email address starts a new account. Nothing needs a real account or key: email, sign-in
providers, the LLM check, job boards and IP data all have local stand-ins.

The demo script, logins, what is faked and how to reset the data: [`docs/operations/local-demo.md`](docs/operations/local-demo.md).
What is built and what the owner still has to do before real use: [MVP status](docs/status.md).

Tests and the gate: `pnpm e2e:install` once per machine, then `pnpm verify`
([`docs/process/testing.md`](docs/process/testing.md)).

## Stack

ASP.NET Core (.NET 10) · PostgreSQL · .NET Aspire · Astro 7 · SvelteKit 3 + Svelte 5 · shadcn-svelte · Tailwind 4 ·
Orval · Azure (Container Apps, PostgreSQL Flexible Server, Blob Storage) behind Cloudflare.
