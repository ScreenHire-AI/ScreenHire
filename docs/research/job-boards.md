# Job-Board Multiposting — Integration Landscape

> Research snapshot: **2026-10-18**. Goal: in-house, proprietary one-click post + teardown across major boards.

## Bottom line

- **Only truly self-serve channel: Google for Jobs** (JobPosting JSON-LD + Indexing API).
- **Indeed and LinkedIn are partner-gated.** Indeed rejects any ATS "in Beta" and jobs from free-trial clients.
  LinkedIn's Job Posting API is closed to new partners.
- **Most EU/UK paid boards** can be integrated directly, but each uses the *customer's* own credentials, contract and
  per-ad fee.
- **Real-time teardown exists only at a few API boards** (SEEK, StepStone, Monster, ZipRecruiter, Indeed close).
  Everywhere else removal waits for a feed re-crawl: 4–24 h, or days for Google without the Indexing API.

## 1. Traffic ranking (Semrush/Similarweb, Sept 2026; LinkedIn/XING/Arbeitsagentur are whole-site figures)

- **US:** LinkedIn, Indeed (195M), ZipRecruiter (42M), Handshake (22M), Glassdoor (20M), Talent.com, USAJOBS, FlexJobs,
  Monster, Jobcase. Niche: Wellfound, Dice, JobGet/Snagajob, We Work Remotely, Built In.
- **UK:** LinkedIn, Indeed (30M), Totaljobs (5–7M), Glassdoor, Civil Service Jobs, Reed, NHS Jobs, CV-Library, Jooble,
  Adzuna. Graduate: Bright Network, Gradcracker.
- **DE:** Arbeitsagentur (31M), Indeed (14M), StepStone (11M), XING, meinestadt, Jooble, HeyJobs, Jobware.
- **FR:** LinkedIn, France Travail (27–34M), Indeed (17M), HelloWork (11–13M), Welcome to the Jungle, Meteojob, APEC,
  Cadremploi.
- **NL:** Indeed (4.8M), UWV/Werk.nl, Werkzoeken, Nationale Vacaturebank, Werken voor Nederland, YoungCapital.

## 2. Direct integration paths

(S) = stated by the board; (I) = secondary source / inferred. Approval lead times are almost never published.

| Board | Method | Access | Removal latency | Build/review | Cost |
|---|---|---|---|---|---|
| **Google for Jobs** | JSON-LD + Indexing API (`URL_UPDATED` / `URL_DELETED`) | Self-serve. Search Console must verify each host. **200 publish calls/day/project** (updates + deletes share it); more on request. Multiple accounts to dodge quota → revoked | Indexing API: crawl "sooner" (no SLA). Sitemaps: "several days" (S) | days | Free |
| **Indeed** (Glassdoor listings come from Indeed) | Job Sync API (GraphQL), Indeed Apply, Disposition Sync, Sponsored Jobs API | Partner agreement (§3) | Explicit expire call; only returns `ACCEPTED`, latency unstated. Indeed expects updates within **15 min** | ~6 weeks build + review (S) | Organic free *only via integration*; sponsored = employer CPC |
| **LinkedIn** | Job Posting API (closed) → Apply Connect; **Basic Jobs XML feed** via LL-BD@linkedin.com | Partner form (§3) | ATS XML feeds scraped every 6 h; edits within ~24 h (S) | XML build 2–8 weeks (S) | Basic Jobs free; promoted uses customer's Job Slots |
| **ZipRecruiter** | Jobs API (create/update/close), XML Feed Import, Apply Webhook | Partner agreement; eligibility unverified | API close ≤30 min (I); feed ~4×/day (I) | unpublished | Organic free; sponsored paid |
| **Monster + CareerBuilder** (BOLD acquired; closed 2025-08-01) | Real Time Posting (SOAP/XML) | Credentials via Monster rep | Delete immediate; search reindex ~90 min (S) | unpublished | Paid, from $8/day |
| **StepStone.de / Totaljobs** | Push API (`INSERT`/`UPDATE`/`OFFLINE`) or pulled feed | Questionnaire → per-customer logins; customer must hold a paid product | Push: near-real-time (S) | unpublished | Per ad (30 days) |
| **SEEK (AU)** | GraphQL posting; Apply with SEEK must be built first | Partner program → Playground → certification → pilot | Close ~1 min (S) | unpublished | Hirer pays per ad; no refund on close |
| **Bundesagentur für Arbeit** | HR-BA-XML upload with client certificate | Cooperation agreement + certificate per customer | Minutes (I) | — | Free |
| **APEC (FR)** | ADEP API (live since 2026-01-28) | Recruiter supplies APEC contact number | unstated | — | Free |
| **Careerjet / Talent.com / Adzuna / Jooble** | XML feed | Contact to onboard (Jooble replies within 48 h) | Careerjet daily (S); Talent.com ~4 h (I) | days | Organic free + CPC |
| **Dice, CV-Library, HelloWork, Guardian** | Per-customer feed or credentials | Account manager | unverified | — | Paid |
| **Wellfound / Handshake / Built In** | They *pull* from named ATSs | Wellfound: Greenhouse, Lever, Ashby, Workable only. Handshake: native integrations only. Built In: list unpublished | Wellfound approval ≤12 h | — | Mostly free |
| **Craigslist** | Bulk Posting Interface | Paid account per employer | No bulk delete | — | Paid |
| **We Work Remotely / Remotive / Remote OK** | Manual only | — | — | — | ~$299/listing |

**Dead / rerouted:** Glassdoor ATS integrations now go through Indeed; Hired → LHH; Otta → Welcome to the Jungle.
**Unavailable / unverified:** Reed legacy posting API deprecated; France Travail public API is read-only; Werk.nl had
no vacancy API (as of 2020); XING and Nationale Vacaturebank unverified.

## 3. Becoming an approved partner

**Indeed:** Developer Agreement → partner request → questionnaire (go-live date, client/job volume, employer vs
staffing) → client-name review, sandbox build, screenshot documentation → approval email. No published customer
minimum or separate security review. **Hard gates:**
- no Beta ATS; no trial clients;
- must submit **every public job of every client** (flag opted-out clients);
- clients can't run another ATS on their career site;
- full request/response logs; verify Indeed HMAC signatures.

**LinkedIn:** form asks customer count, 5 largest customers, daily active jobs, regions. Warns of a "significant
volume of inquiries" and "cannot promise" an agreement. Apply Connect requires Job Posting first. Customers must accept
LinkedIn Jobs T&C (keep records). Can't market as a "free way to post to LinkedIn". Lead time: "3–6 months" (third-party
estimate, unverified).

**Day one with no approval:** Google only. Careerjet, Talent.com, Adzuna, Jooble need a contact, not a contract. Paid
boards work as soon as a customer brings credentials.

## 4. Fallbacks, unified APIs, competitors

**Aggregators (fallback only):** VONQ HAPI (API + widgets, 2,500+ boards, pricing not public; used by Jobtoolz);
Broadbean (partner API, 7,000+ boards, no sandbox); idibu (Posting API, PAYG or whitelabel); JobTarget (revenue share,
"no added cost" to ATS); Appcast / Joveo (programmatic CPC, employer-contracted, fed from ATS feed); eQuest (no public
API found). **From 2026-12-31 Indeed stops showing integrated-ATS jobs arriving via agency feeds.**

**Unified APIs** (Merge, Kombo, Apideck) sync ATS data; none pushes postings to boards. Useful in reverse: boards use
Kombo to *pull* from ATSs (CV-Library case study; Reed and Welcome to the Jungle among Kombo logos). Getting listed as a
Kombo/Merge connector + publishing a Greenhouse-style public job-board API gets our jobs pulled. Connector onboarding
process unpublished.

**Competitors:** GoHire, Breezy, Workable enable free feed boards by default. GoHire's help centre (2026-03-22) admits
disabled boards "won't be removed immediately"; Breezy sends within one business day; Teamtailor free channels take
24–72 h. Paid: Workable/Breezy sell premium boards from an in-app cart (Workable: 30 days); Teamtailor and GoHire resell
Indeed Sponsored; Recruitee uses VONQ.

## 5. Architecture note (in-house multiposting engine)

- **Canonical job model** — superset of schema.org `JobPosting` (salary, locations, remote type, `validThrough`,
  screener questions, per-board company IDs).
- **Per-board adapter interface** — `Map/Validate`, `Publish`, `Update`, `Close`, `GetStatus`.
- **Capability flags per adapter** — push vs pull; close supported; per-board unpublish allowed (Indeed appears to
  forbid it — teardown on Indeed = close the job); sponsorship; quota.
- **Pull boards** — generated full-snapshot feeds.
- **Push boards** — transactional outbox + .NET hosted workers (Indeed's 15-min rule rules out batch); idempotency key
  `(job, board, version)`; per-board state machine `Queued → Submitted → Live | Rejected → Closing → Closed`;
  reconciliation polling (Indeed `ACCEPTED` doesn't confirm removal).
- **Credentials** — per-tenant vault for board keys/certs (Reed key, StepStone login, BA `.p12`, SEEK hirer link).
- **Apply-flow return** — signed webhook receivers (Indeed Apply, ZipRecruiter, Apply Connect, SEEK) normalised into
  one `Application` model, plus disposition sync back.
- **Google quota** — one GCP project with a quota increase; **tenant subdomains** (each custom domain needs its own
  Search Console verification).
- **Product flag** — exclude trial tenants from Indeed sync.

## 6. Phased plan

1. **Weeks 0–4:** career pages with JSON-LD, sitemaps, Indexing API; public XML/JSON feed + public job-board API;
   onboard Careerjet, Talent.com, Adzuna, Jooble. Teardown = page returns `410`, `validThrough` set to past,
   `URL_DELETED` sent.
2. **With paying customers (months 2–4):** Indeed partner request (~6 weeks build); apply to ZipRecruiter. **Send the
   LinkedIn form and LL-BD XML request immediately** given lead time.
3. **Months 4–9 (customer-credential boards):** StepStone/Totaljobs push, Monster RTP, BA HR-BA-XML, APEC; SEEK if AU.
4. **Later:** Kombo/Merge connector listings, Wellfound, Handshake; aggregator (VONQ or idibu) for long-tail paid boards.

## Not verified

Indeed 4-year candidate-data obligation; ZipRecruiter eligibility and latency figures; most partner approval timelines
(LinkedIn "3–6 months" is third-party); "Limited Listings" = old name for Basic Jobs (inferred); Adzuna $600/mo minimum
(conflicts with Adzuna's page); UK DWP bulk upload status; VONQ/Broadbean/idibu/eQuest pricing.

## Sources (accessed 2026-10-18)

- docs.indeed.com: /legal-terms/job-sync, /job-sync-api/job-sync-api-guide, /job-sync-api/integrate-with-job-sync-api (2026-10-07), /job-sync-api/faq (2026-10-06), /indeed-apply/ats, /sponsored-jobs-api/ats-integrations/integration-guide-for-ats-partners (2026-10-07)
- indeed.com/help/employers/articles/single-source-feed-policy-effective-march-31-2026
- learn.microsoft.com/en-us/linkedin/talent/job-postings/api/overview (2026-06-03), /xml-feeds, /xml-feeds-faq (2025-06-18)
- business.linkedin.com/talent-solutions/ats-partners/partner-application
- jobspipe.dev/blog/linkedin-api-post-job (2026-07-25)
- developers.google.com/search/apis/indexing-api/v3/quota-pricing, /quickstart (2026-10-16)
- developers.google.com/search/docs/appearance/structured-data/job-posting (2026-09-08)
- ziprecruiter.com/partner/documentation
- partner.monster.com/real-time-posting-devguide
- prweb.com/releases/bold-completes-acquisition-of-careerbuilder--monster-job-boards-302519807.html
- api.stepstone.com/knowledge-base (2026-10-16)
- developer.seek.com
- arbeitsagentur.de/unternehmen/arbeitskraefte/hr-ba-xml-schnittstelle
- careerjet.co.uk/mediakit/ats · talent.com/integrations · adzuna.co.uk/hire/products/job-listings · jooble.org/ats
- glassdoor.com/about/onelogin · help.wellfound.com/article/746 (2025-06-11)
- kombo.dev/solutions/job-boards · vonq.com/hapi-product-bundle · docs.idibu.com/docs/v2 · jobtarget.com/ats-hris-partner-integration
- help.gohire.io/en/articles/3385202 (2026-03-22) · help.workable.com · help.breezy.hr/en/articles/5297503 · support.teamtailor.com/en/articles/6200102
- semrush.com/website/top/{country}/jobs · similarweb.com/top-websites/{country}/jobs-and-career (2026-10-01)
