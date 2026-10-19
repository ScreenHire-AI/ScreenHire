# ADR-0007: Host on Azure (bootstrap config), Cloudflare in front

- **Status:** Accepted — owner preference (2026-10-18)
- **Research:** `docs/research/hosting-bootstrap.md`, `docs/research/hosting.md`

## Context
Owner wants well under $250/mo now, predictable costs, room to grow into SOC 2 Type II, and prefers Azure (familiar
tooling: `az` CLI). The research's cheapest flat-rate option was DigitalOcean (~$44/mo; it also has a full CLI,
`doctl`). Azure's advantages for this stack: .NET-native, **Aspire's most mature deployment target is Azure Container
Apps**, managed identity (no stored secrets), Key Vault, broad compliance attestations (SOC 1/2/3, ISO 27001…), and
**Microsoft for Startups** credits (no funding requirement; up to $150k unlocked over time).

## Decision

| Component | Service | Est. monthly (pre-credits) |
|---|---|---|
| API | Container Apps (Consumption), min 1 replica, ~0.5 vCPU / 1 GiB | |
| Careers (Astro SSR) | Container Apps, min 1 replica, ~0.25 vCPU / 0.5 GiB | ~$20–40 for all three (mostly idle billing) |
| Worker (Wolverine) | Container Apps, min 1 replica, ~0.25 vCPU / 0.5 GiB | |
| Database | PostgreSQL Flexible Server **Burstable B1ms** + 32 GB, 7-day backups/PITR, `vector` extension | ~$19 (West Europe) |
| Object storage | Blob Storage (private, SAS/managed identity) behind an `IObjectStore` abstraction; Azurite locally | < $2 |
| Email | Azure Communication Services Email (price to verify) — SES as fallback | ~$1–4 |
| Secrets | Key Vault | < $1 |
| Images | GitHub Container Registry (free) | $0 |
| Marketing + dashboard (static) | Cloudflare Pages (free) | $0 |
| Edge | Cloudflare Free: DNS, CDN, WAF managed ruleset, DDoS, **Turnstile** | $0 |
| Observability | OpenTelemetry → Grafana Cloud Free (avoid App Insights ingestion cost) | $0 |
| **Total** | | **≈ $45–65/mo before credits** |

- **Region:** EU first (West Europe or North Europe); tenant `homeRegion` field from day one for a later US region.
- **Deploy:** Aspire app model → `azd` to Container Apps with generated Bicep committed to the repo; GitHub Actions CI.
- **Cost guardrails:** Azure budget alerts at $50 / $100 / $200; **avoid private endpoints / features that trigger
  Container Apps "Dedicated Plan Management" (~$104/mo) until a customer requires it**; scale-to-zero is *not* used for
  API/careers (cold starts would hurt the speed goal).
- **Postgres connectivity:** connect directly (not via built-in PgBouncer transaction mode) — Wolverine leader election
  and transaction-local tenant settings require it; TLS + Entra ID auth; firewall restricted.
- **Credits:** apply to Microsoft for Startups immediately; AWS Activate Founders ($1k) is not needed.

## Upgrade path
1. First paying customers: PG General Purpose or larger Burstable, more replicas.
2. SOC 2: Azure is a carved-out subservice org with its own reports; add Vanta/Drata (or self-hosted Comp AI) + auditor
   when a deal requires it. HA (zone-redundant PG, ~$310+/mo) only when a contract demands it.
3. US region: second Container Apps environment + PG server; tenants pinned by `homeRegion`.

## Fallback
If credits don't materialise and consumption billing exceeds budget: DigitalOcean (droplet + managed PG + Spaces,
~$44/mo, SOC 2 Type II, `doctl`). Containers + vanilla Postgres keep the move a migration, not a rewrite.

## To verify before provisioning
ACS Email per-message pricing; Azure free-account inclusions for PG Flexible; Container Apps idle-rate estimate against
actual usage after the first month; `azd` + Aspire 13.6 support status for the JavaScript (Astro) resource.
