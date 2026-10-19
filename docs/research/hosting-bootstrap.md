# Bootstrap Hosting (< $250/mo) with a Path to SOC 2

> Research snapshot: **2026-10-18**. Owner constraint: well under $250/mo now; room to grow into SOC 2 Type II.
> Prices from official pages / Azure Retail Prices API unless marked **[U]** (unverified). Supersedes the
> "small production" sizing in `hosting.md` for the bootstrap phase.

## Configs compared

| # | Config | Monthly | Vendor SOC 2 | Adding Postgres HA |
|---|---|---|---|---|
| 1 | **DigitalOcean** — Basic droplet 2 vCPU/4 GiB (4 TB transfer) $24 + Managed PG 1 GiB $15.15 + Spaces $5 (250 GiB, 1 TiB egress) | **$44.15** (+$4.80 weekly droplet backups) | **Yes**, Type II (Schellman); Vanta integration checks droplets/DB encryption & backups | New accounts: Advanced Edition from $130/node + standby → **≥ $260** |
| 2 | **AWS Lightsail** — 4 GB/2 vCPU $24 + 2 GB DB $30 + object storage $3 (100 GB, 250 GB transfer) | **$57** | **Lightsail is NOT on AWS's SOC scope list** (2026-08-11) | Encrypted HA from $60 → $87 |
| 3 | **Hetzner** — CX33 €8.49 + self-managed PG + Object Storage | ~€15–20 ex-VAT | None | Build/run it yourself |
| 4 | **Azure** — Container Apps consumption (API + Astro min 1 replica, worker as job) + PG Flexible B1ms 32 GB + Cloudflare R2 | ~$45–100, **~$50 likely** | Yes | Burstable has no zone-redundant HA; GP 2-vCore ≈ $155/node → **~$310+** |
| 5 | **Fly.io** — 1 GB + 2 × 512 MB machines + Managed Postgres Basic $38 (HA included) | **~$55** + Tigris | SOC 2 Type 2 | Included. EU region availability **[U]** (prices shown are US-east) |

**Notes**
- **DigitalOcean:** 1 GiB DB is $15.15 on the pricing page, $15.00 in docs; included storage unclear. Daily backups,
  **7-day PITR** and LUKS encryption at rest included. **Advanced Edition has blocked `CREATE EXTENSION` since Sep 30**
  — confirm pgvector is available on the Standard plan we use.
- **Azure compute:** West Europe $0.000034/vCPU-s active, $0.000004 idle, $0.000004/GiB-s; free grant 180k vCPU-s,
  360k GiB-s, 2M requests/month. API 0.5 vCPU/1 GiB + Astro 0.25 vCPU/0.5 GiB ≈ $22 mostly idle, ~$75 always active.
  B1ms $14.53 + $4.38 storage (West Europe). **Traps:** Blob isn't S3-compatible (use R2); private endpoints/planned
  maintenance trigger "Dedicated Plan Management" ≈ $104/mo even on Consumption.
- **Lightsail:** the $15/$30 1 GB DB plans are marked **"No data encryption"** on the pricing card (FAQ contradicts) →
  ruled out for resumes. 7-day PITR free. EU overage $0.09/GB; inbound counts. Migration to ECS/RDS: VPC peering only
  to the default VPC (temporary bridge); DB must move by pg_dump/logical replication; PG 12–16 only.
- **Hetzner:** cloud pages currently show Cost-Optimized "not available" and every Regular Performance plan "currently
  unavailable" → **may not be orderable today**.
- **Others:** Render Pro workspace $25/mo (SOC 2 Type 2, ISO 27001; instance/PG prices [U]). Railway Hobby $5 / Pro $20
  incl. usage (SOC 2 [U]). Neon SOC 2 only on Scale ($0.222/compute-h). Supabase SOC 2 only on Team ($599); PITR $100/mo.
  Cheap tiers often mean **no vendor SOC 2 report for your auditor**.

## Startup credits

| Program | Amount | Bootstrapped founder? |
|---|---|---|
| AWS Activate Founders | $1,000 (up to $5,000 for selected) | **Yes — realistic.** Portfolio (≤ $200k) needs accelerator/VC Org ID |
| **Microsoft for Startups** | Credits start immediately; up to $150k unlocked over time with verified progress + Azure usage (initial $1k→$5k amounts [U]) | **Yes — most realistic sizeable program; no funding requirement** |
| DigitalOcean Hatch | Varies; ≤ $10M raised; needs live website + company-domain email; prioritises AI-native | Plausible |
| Google for Startups (Start) | ~$2k pre-funded [U] | Possibly |
| Cloudflare for Startups | Tier 3 $10k for < $1M raised (criteria also say "funded within 12 months" — contradictory) | Unclear; covers Workers/R2, not plan fees |

## Cloudflare Free in front

- Fine for proxying Astro SSR: WAF (Free Managed Ruleset), unmetered DDoS, 70 rules, basic bot mitigation.
- Limits: **100 MB request bodies** (upload video directly to storage with presigned URLs); **125 s proxy timeout**
  (keep LLM calls async); no video via CDN on non-Enterprise without Stream/Images.
- **Turnstile Free:** 20 widgets, 10 hostnames per widget, unlimited challenges. Any-hostname widgets are
  Enterprise-only → keep the apply form on **our** careers domain (customer custom domains embed it via iframe), which
  ADR-0003 already does.

## Transactional email (≈ 5–15k emails/month)

- **Amazon SES:** $0.10 per 1,000 → ~$1–2/mo; in AWS SOC scope.
- Postmark: $15 for 10k, then $1.80/1,000. Resend Pro: $20 for 50k (SOC 2 Type II).

## SOC 2 Type II path

- **Tooling (third-party estimates [U]):** Vanta ~$10–25k/yr; Drata from ~$7.5k (typ. $13–22k); Secureframe median
  ~$20k; Comp AI (open-source AGPL core, self-hostable, docs incomplete) and Oneleet unpublished.
- **Auditor:** Type I ~$5–20k; Type II from ~$7k; 3–12-month observation window.
- **Hosting effect:** auditor carves out the host and relies on its SOC 2 report. Managed DB covers backups, encryption,
  patching evidence; self-managed Postgres means producing it all yourself. Lightsail, Hetzner, cheap Neon/Supabase give
  no host report.
- **SOC 2 does not require HA.** Backups, tested restores, access control, logging and change management suffice; HA only
  for the optional Availability criterion or customer SLAs.
- **Timing:** free controls from day one — MFA/SSO everywhere, infrastructure as code, branch protection, centralised
  logs, encrypted backups with restore drills, policy templates, vendor list. Buy a tool + Type I when a real deal or
  security questionnaire needs it; start ~6 months before you need the Type II report. First-year cost ~$15–35k.

## Research recommendation

Start on **Config 1 — DigitalOcean (~$45–50/mo)** with Cloudflare Free + Turnstile in front, the dashboard SPA on
Cloudflare Pages (or Spaces), and SES for email. Apply to **Microsoft for Startups** in parallel; if it grants ≥ $5k,
consider Config 4 (Azure) — .NET-native and potentially ~$0 for year one.

1. **Bootstrap:** ~$45–65/mo.
2. **First paying customers:** PG 2 GiB ($30.45) + droplet 4 vCPU/8 GiB ($48) + droplet backups; start SOC 2 controls →
   ~$90–110/mo.
3. **SOC 2:** add tool + auditor; keep single-node PG with PITR; add HA only when a contract demands it (DO Advanced
   ≥ $260, or Azure/AWS funded by credits).

## Sources

1. https://www.digitalocean.com/pricing/droplets
2. https://www.digitalocean.com/pricing/managed-databases
3. https://www.digitalocean.com/pricing/spaces-object-storage
4. https://www.digitalocean.com/trust/certification-reports
5. https://www.vanta.com/integrations/digitalocean
6. https://docs.digitalocean.com/products/databases/postgresql/details/pricing/ · https://docs.digitalocean.com/release-notes/upcoming/dbaas-plan-changes/
7. https://aws.amazon.com/lightsail/pricing/
8. https://aws.amazon.com/compliance/services-in-scope/SOC/
9. https://docs.hetzner.com/general/infrastructure-and-availability/price-adjustment/ · https://www.hetzner.com/cloud/ · https://www.hetzner.com/cloud/regular-performance
10. https://learn.microsoft.com/en-us/azure/postgresql/high-availability/concepts-high-availability
11. https://docs.fly.io/about/pricing
12. https://docs.fly.io/mpg
13. https://fly.io/compliance
14. https://docs.digitalocean.com/products/databases/postgresql/details/features/
15. https://prices.azure.com/api/retail/prices
16. https://learn.microsoft.com/en-us/azure/container-apps/billing
17. https://developers.cloudflare.com/r2/pricing/
18. https://render.com/articles/how-much-does-cloud-application-hosting-cost-for-small-businesses
19. https://render.com/security
20. https://railway.com/pricing
21. https://neon.com/pricing
22. https://supabase.com/pricing
23. https://aws.amazon.com/lightsail/faq/
24. https://docs.aws.amazon.com/lightsail/latest/userguide/lightsail-how-to-set-up-vpc-peering-with-aws-resources.html
25. https://docs.aws.amazon.com/lightsail/latest/userguide/amazon-lightsail-choosing-a-database.html
26. https://docs.digitalocean.com/release-notes/
27. https://aws.amazon.com/startups/credits
28. https://learn.microsoft.com/en-us/microsoft-for-startups/
29. https://www.digitalocean.com/hatch
30. https://www.cloudflare.com/forstartups/
31. https://www.cloudflare.com/plans/network-cdn.md
32. https://developers.cloudflare.com/workers/platform/limits/#request-limits
33. https://developers.cloudflare.com/support/troubleshooting/http-status-codes/cloudflare-5xx-errors/error-524/
34. https://www.cloudflare.com/service-specific-terms-application-services/
35. https://developers.cloudflare.com/turnstile/plans/
36. https://aws.amazon.com/ses/pricing/
37. https://postmarkapp.com/pricing
38. https://resend.com/pricing
39. https://trycomp.ai/pricing
40. https://www.oneleet.com/pricing
41. https://github.com/trycompai/comp
42. https://secureframe.com/hub/soc-2/audit-cost
