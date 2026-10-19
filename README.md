# ScreenHire

An affordable applicant tracking system for small hiring teams that knows who is really applying: it detects bots,
impersonators and AI-generated applications and explains every signal in plain language — evidence, never verdicts.

- **Docs:** [`docs/README.md`](docs/README.md) — decision log (ADRs), architecture, design system, research.
- **Agent guide:** [`CLAUDE.md`](CLAUDE.md).

## Quick start

Prerequisites: .NET SDK 10.0.400+, Node 22.22+, pnpm 10, Docker.

```bash
pnpm install   # once, at the repo root
pnpm dev       # runs the Aspire AppHost: Postgres (pgvector), Azurite, API, worker and all frontends
```

| Service | URL |
|---|---|
| Marketing site | http://localhost:4321 |
| Careers pages (demo tenant) | http://localhost:4322/brightwater |
| Recruiter dashboard | http://localhost:4323 |
| API + Scalar reference | http://localhost:5080/scalar |
| Aspire dashboard | printed in the console on start |

## Stack

ASP.NET Core (.NET 10) · PostgreSQL · .NET Aspire · Astro 7 · SvelteKit 3 + Svelte 5 · shadcn-svelte · Tailwind 4 ·
Orval · Azure (Container Apps, PostgreSQL Flexible Server, Blob Storage) behind Cloudflare.
