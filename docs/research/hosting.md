---
type: Research
title: "Hosting: AWS vs DigitalOcean vs Hetzner"
description: "AWS, DigitalOcean and Hetzner compared for a small EU production setup: the 2026 price changes, managed platforms, GDPR regions, compliance attestations and hybrid options."
tags: [research, hosting]
---
# Hosting: AWS vs DigitalOcean vs Hetzner

> Research snapshot: **2026-10-18**. USD, excl. VAT, 730 h/month. Priority: price-to-performance and **predictable**
> monthly cost, with a path to enterprise-friendly compliance.

## Three 2026 changes that reshape the comparison

1. **Hetzner raised prices and is rationing cloud capacity.**
   - **15 Jun 2026** (verified on Hetzner's price-adjustment page): new orders and rescales only.
     CPX32 €13.99 → €35.49; CX33 €6.49 → €8.49; CCX23 €31.49 → €85.99. Legacy-priced servers may switch to current
     pricing after "certain changes" (per Hetzner FAQ).
   - An earlier **1 Apr 2026** increase of ~30–37% that reportedly also hit existing servers is cited by heise
     (not re-verified against Hetzner's own page).
   - Hetzner status page: **"Limited availability of cloud instances"**, open since **2026-06-26**, no end date
     (verified). The notice does not say whether new orders/customers are blocked; the research agent reported product
     pages showing plans as "currently unavailable" — **test an order before relying on Hetzner.**
2. **DigitalOcean managed DB plan changes** (verified on DO release notes):
   - From **15 Oct 2026** for accounts that have *never* created a PG/MySQL cluster, and from **30 Nov 2026** for
     *all* accounts: new Standard Edition clusters (1/2/4 GiB) **cannot add standby or read-only nodes**. HA requires
     Advanced Edition (≈ $130/node per research; DO's page links to pricing rather than stating it).
   - **Grandfathering:** clusters created before the restriction applies to your account keep full capability
     (standby, read replicas, resize, fork, restore) indefinitely, with advance notice promised before any change.
3. **AWS egress can now be capped.** CloudFront flat-rate "Pro": $15/month incl. 50 TB + 10M requests.
   App Runner closed to new customers since 2026-04-30 (AWS points to ECS Express Mode).

## Cost table — small production config, EU region

Config: 3 × ~4 vCPU/8 GB (2 API + 1 worker), managed Postgres ~4 GB (HA where available), Valkey/Redis ~1 GB,
500 GB object storage, 2 TB egress (worst case: all from object storage), load balancer.

| Line item | Hetzner (FSN/NBG/HEL) | DigitalOcean (FRA1/AMS3) | AWS Frankfurt (on-demand) |
|---|---|---|---|
| 3 × ~4 vCPU / 8 GB | CPX32 shared AMD: **$125.97**; CX33 $29.97 *if orderable*; CCX23 dedicated 16 GB: $304 | Basic shared: **$144**; CPU-Optimized dedicated: $252 | c7g.xlarge Graviton: **$361** |
| IPv4 / disks | $1.80 | included | ~$27 (5 IPv4 + gp3; disk rate est.) |
| Postgres ~4 GB | No managed offering. Ubicloud standard-2: $78 single / **~$156 HA** (est.). Self-managed on 2 × CPX22: $46 | Standard 4 GiB single node: **$60.90**. HA via Advanced: **≥ $260** | db.t4g.medium Multi-AZ + 50 GB: **$122** |
| Valkey ~1 GB | Not offered (run on worker): $0 | $15 | cache.t4g.small: $21 |
| 500 GB storage + 2 TB egress | $7.99 base (1 TB + 1 TB) + €1/extra TB = **$9.19** | $5 + $5 storage + ~$10 egress = **~$20** | S3 $12.25 + egress $171 direct, or **$15 via CloudFront Pro** |
| Load balancer | LB11: $7.89 | $12 | ALB ~$27 |
| Included compute bandwidth | 20 TB/server (EU) | 15 TB pooled; $0.01/GiB overage | none |
| **Monthly total** | **~$191** (self-managed DB) / **~$301** (Ubicloud HA) | **~$252** (no HA) / **~$451** (HA) | **~$585** w/ CloudFront; **~$435** w/ 1-yr Savings Plan + RDS RI (est.) |

AWS us-east-1 is ~10–15% cheaper than Frankfurt (c7g.xlarge $105.85/mo there).

**Price-to-performance (dedicated CPU):** CCX23 $101.49 (16 GB) · DO CPU-Optimized $84 · c7g $105.85 us-east-1
(~$70 with 1-yr commitment). Hetzner now leads mainly on shared CX/CAX plans (if orderable), bandwidth and object storage.

## Managed platforms

- **Hetzner:** no managed Postgres, Redis or Kubernetes in 2026. Common patterns: k3s (hetzner-k3s), Coolify,
  CloudNativePG/Patroni, Ubicloud (runs on Hetzner hardware, claims SOC 2 Type 2; raised prices 26% then 25% in 2026).
- **DigitalOcean:** DOKS control plane free (HA control plane $40); App Platform $50 for shared 2 vCPU / 4 GiB.
- **AWS:** ECS on EC2 or Fargate; ECS Express Mode.

## Regions & GDPR

- **Hetzner US** (Ashburn, Hillsboro): older CPX/CCX only; CPX31 $73.49 with 3 TB traffic (vs 20 TB EU); **no object
  storage in US**; ISO 27001 does not cover US sites.
- **DO and AWS:** full EU and US regions.

## Compliance attestations

| Provider | Attestations | GDPR DPA |
|---|---|---|
| AWS | SOC 1/2/3, ISO 27001/27017/27018/27701, C5 | Yes |
| DigitalOcean | SOC 2 Type II + SOC 3 (Schellman), CSA STAR L1; ISO 27001 at colocation level only | Yes, incl. DPF + SCCs (updated 2026-10-14) |
| Hetzner | ISO 27001:2022 (Nuremberg, Falkenstein, Helsinki only); **no SOC 2** | Standard in-account DPA; no custom DPAs |
| Cloudflare | SOC 2 Type II, ISO 27001/27701/27018; R2 EU jurisdiction available | — |

**Reliability:** Hetzner includes free DDoS filtering but has no managed HA services, restricted provisioning and an
urgent core-router maintenance on 2026-10-15. AWS: multi-AZ RDS. DO: HA only via Advanced Edition for new clusters.

## Hybrid options

- **Hetzner + Cloudflare + R2:** R2 $0.015/GB, free egress (500 GB ≈ $7). Cloudflare terms require video to go through
  paid developer services or Stream (R2 presumably covered, not named). Proxying Hetzner-hosted video through the CDN
  risks restriction. Cheapest; weakest compliance (no SOC 2 on compute).
- **DO compute + managed DB/Valkey + Spaces or R2:** simple, predictable; HA is expensive for new clusters.

## Research recommendation

1. **Don't build on Hetzner now** — restricted provisioning, two 2026 increases, no managed data tier, no SOC 2,
   weak US. Revisit only if a test order succeeds, and then for batch workers.
2. **Launch on DigitalOcean (~$252/mo)** with single-node Postgres + PITR if restore-time recovery is acceptable
   initially. Cheapest SOC 2 Type II host with flat bills and 15 TB pooled bandwidth. **If the account has ever created
   a PG/MySQL cluster, create a Standard 4 GiB cluster with standby (~$121.80) before 2026-11-30** — it stays
   grandfathered.
3. **Move to AWS when HA or enterprise deals arrive.** With HA, AWS on commitments (~$435) undercuts DO Advanced HA
   (~$451) and has the strongest attestations: Graviton, RDS Multi-AZ, S3 + CloudFront flat-rate, Savings Plans after
   1–2 months of baseline. If enterprise deals are expected within ~6 months, start on AWS.
4. **Stay portable:** containers, vanilla Postgres, S3 API, Valkey. Our own SOC 2 is separate, but an attested host
   shortens buyer security reviews.

## Not verified

Whether Hetzner provisions for a new account today; Ubicloud standby pricing; DO Advanced Edition included storage;
AWS Savings Plan/RI ratios (from instances.vantage.sh), Frankfurt ALB and gp3 rates; CloudFront flat-rate signed-URL
support for private video; RDS t4g CPU-credit surcharges; support tiers/SLAs.

## Sources

- Hetzner: https://docs.hetzner.com/general/infrastructure-and-availability/price-adjustment/ · https://heise.de/-11185981 · https://heise.de/-11333037 · https://status.hetzner.com/ · https://www.hetzner.com/cloud/regular-performance/ · https://www.hetzner.com/storage/object-storage/ · https://docs.hetzner.com/storage/object-storage/overview · https://www.hetzner.com/cloud/load-balancer/ · https://www.hetzner.com/unternehmen/zertifizierung/ · https://docs.hetzner.com/general/company-and-policy/data-protection-at-hetzner · https://www.hetzner.com/unternehmen/ddos-schutz/
- Ubicloud: https://www.ubicloud.com/docs/about/pricing · https://ubicloud.com/docs/llms-full.txt
- DigitalOcean: https://www.digitalocean.com/pricing/droplets · https://www.digitalocean.com/pricing/managed-databases · https://docs.digitalocean.com/products/databases/postgresql/details/pricing/ · https://docs.digitalocean.com/release-notes/upcoming/dbaas-plan-changes/ · https://www.digitalocean.com/pricing/spaces-object-storage · https://docs.digitalocean.com/platform/billing/bandwidth/ · https://www.digitalocean.com/pricing/load-balancers · https://www.digitalocean.com/pricing/kubernetes · https://www.digitalocean.com/pricing/app-platform · https://www.digitalocean.com/trust/certification-reports · https://www.digitalocean.com/legal/data-processing-agreement
- AWS: https://aws.amazon.com/ec2/pricing/on-demand/ · https://aws.amazon.com/rds/postgresql/pricing/ · https://aws.amazon.com/s3/pricing/ · https://aws.amazon.com/cloudfront/pricing/ · https://aws.amazon.com/elasticloadbalancing/pricing/ · https://aws.amazon.com/compliance/programs/ · https://docs.aws.amazon.com/apprunner/latest/dg/apprunner-availability-change.html
- Cloudflare: https://developers.cloudflare.com/r2/pricing/ · https://developers.cloudflare.com/r2/reference/data-location/ · https://www.cloudflare.com/service-specific-terms-application-services/ · https://cloudflare.com/trust-hub/compliance-resources/iso-certifications
