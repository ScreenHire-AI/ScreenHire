# Job Distribution Engine — In-House One-Click Post & Teardown

> Status: **v1 built (2026-10-20)**. Section 5 describes what exists; sections 1 to 4 are the design it follows.
> Owner requirement: build multiposting **in-house as proprietary tech**; aggregators only as a fallback. Research:
> `docs/research/job-boards.md`.

## 1. Reality check (what "one-click teardown" can honestly mean)

| Board class | Post | Teardown | What we promise in UI |
|---|---|---|---|
| Our careers pages + Google for Jobs | Instant page + Indexing API `URL_UPDATED` | Page → `410 Gone`, `validThrough` in past, `URL_DELETED` | "Removed from your site now; Google notified" |
| Push API boards (StepStone/Totaljobs, Monster, ZipRecruiter, SEEK, Indeed Job Sync) | API call | API close/offline call; seconds–90 min | "Closed on board" once confirmed by reconciliation |
| Pull/feed boards (LinkedIn Basic Jobs, Careerjet, Talent.com, Adzuna, Jooble) | Appears on next crawl (4–24 h) | Disappears on next crawl | "Removal requested — board refreshes every ~X h" with per-board ETA |
| Pull-from-ATS boards (Wellfound, Handshake, Kombo/Merge consumers) | Via our public job-board API | Same | Same as feed boards |

The UI must show **per-board status and ETA**, never a fake "removed everywhere" checkmark. That honesty itself beats
GoHire (whose help centre admits disabled boards "won't be removed immediately").

## 2. Architecture

```
Job (canonical, schema.org JobPosting superset)
  └─ JobVersion (immutable snapshot used for publishing)
        │
  Distribution service
   ├─ Adapter registry  ── per-board adapter implements:
   │      Validate(JobVersion) → issues[]        (required fields, salary rules, category maps)
   │      Map(JobVersion)      → board payload
   │      Publish / Update / Close / GetStatus
   │      Capabilities { mode: Push|Pull|PullFromAts, canClose, canUnpublishPerBoard,
   │                     sponsorship, quota, credentialType, removalEta }
   ├─ Posting state machine (per job × board)
   │      Draft → Queued → Submitted → Live | Rejected → Closing → Closed  (+ Error with retry)
   ├─ Outbox + durable workers   idempotency key (jobId, boardId, jobVersion)
   ├─ Reconciler                 polls GetStatus; confirms live/closed; detects drift
   ├─ Feed generator             per-board full-snapshot XML/JSON feeds, cached at CDN
   ├─ Credential vault           per-tenant board credentials (API keys, logins, .p12 certs), encrypted
   └─ Inbound apply webhooks     Indeed Apply, ZipRecruiter, LinkedIn Apply Connect, SEEK → normalised Application
                                 + disposition sync back to boards
```

- **Job versions (built)**: `POST /v1/jobs/{id}/publish` freezes the job's posting content as a numbered `JobVersion`
  (`job_versions`, append-only) and raises `JobPublished` with that version. Editing an open job changes the live job
  and the careers page at once but not the boards: the job reports `hasUnpublishedChanges` until someone publishes
  again, which freezes the next version. Publishing unchanged content creates no version and no event. Adapters
  publish from a version, never from the live job.
- **Single source of truth**: the canonical job. Board-specific fields (category IDs, company IDs, sponsorship budget)
  live in `posting.boardOptions`.
- **Validation before publish**: the "Post" dialog runs every selected adapter's `Validate` and shows fixable issues
  ("Indeed requires salary for this location"), so posting is *accurate* — an explicit owner requirement.
- **Teardown** = one action that transitions every posting for the job to `Closing`, fires each adapter's `Close`,
  flips careers page to `410`, sends `URL_DELETED`, and tracks each to `Closed` with ETA.
- **Auto-expiry**: `validThrough` drives scheduled teardown; closed jobs stop counting as billable immediately (a
  GoHire pain point).

## 3. Constraints baked into the design

- **Google Indexing API**: 200 publish calls/day/project shared by all tenants → request a quota increase early; batch
  and prioritise (deletes first). Careers pages on **tenant subdomains** under one verified Search Console domain
  property; custom domains need per-domain verification (premium feature).
- **Indeed partner gates**: no beta ATS, no trial clients, must send *every* public job of a participating client →
  `org.indeedEligible` flag; trial tenants excluded; full request/response logging; HMAC verification on inbound calls.
  Indeed is **not** promised in the MVP.
- **Indeed single-source policy**: from 2026-12-31 integrated-ATS jobs arriving via agency feeds are suppressed → we
  must be the direct source.
- **LinkedIn**: apply for partner programme + Basic Jobs XML feed early (long lead time). Never market as "free
  LinkedIn posting".
- **Per-board unpublish**: some boards (Indeed) treat removal as closing the job → capability flag drives UI.

## 4. Phased rollout

1. **MVP (weeks 0–4 of distribution work)**: careers pages + JSON-LD + sitemaps + Indexing API; public XML/JSON feed and
   public job-board API; Careerjet, Talent.com, Adzuna, Jooble feed onboarding.
2. **First paying customers**: Indeed partner request (~6 weeks build/review), ZipRecruiter; LinkedIn form + XML
   request sent as soon as the company exists.
3. **Months 4–9**: customer-credential push boards — StepStone/Totaljobs, Monster RTP, Bundesagentur HR-BA-XML, APEC.
4. **Later**: Kombo/Merge connector listings, Wellfound/Handshake, an aggregator (VONQ/idibu) for the long tail of paid
   boards.

## 5. Built: distribution v1

The MVP channels from §4 phase 1, plus a local sandbox push board, so publish and teardown run end to end with no
third party. The code is in `src/ScreenHire.Application/Distribution`, the state machine is
`src/ScreenHire.Domain/Distribution/JobPosting.cs`, and the endpoints are `apps/api/Endpoints/JobEndpoints.cs`,
`DistributionEndpoints.cs` and `PublicDistributionEndpoints.cs`.

### Boards

| Id | Board | Mode | Live confirmed | Removal confirmed | Estimate (`expectedBy`) | Hidden unless |
|---|---|---|---|---|---|---|
| `careers` | Careers page (JSON-LD, sitemap) | Hosted | yes, at once | yes, at once (410) | none needed | never hidden |
| `google` | Google for Jobs (Indexing API) | Notify | no | no | none: Google publishes no timing | `GoogleIndexing:Mode` is `Local`, or `Google` with a key |
| `job-board-api` | Public job-board API | PullFromAts | no | no | unknown: readers poll on their own schedule | never hidden |
| `xml-feed` | Generic XML feed | Pull | no | no | unknown | never hidden |
| `careerjet` | Careerjet feed | Pull | no | no | 1 day, Careerjet's stated refresh, plus 5 min cache | never hidden |
| `talent` | Talent.com feed | Pull | no | no | 4 h (secondary source), plus 5 min cache | never hidden |
| `adzuna` | Adzuna feed | Pull | no | no | 1 day, Adzuna's stated refresh, plus 5 min cache | never hidden |
| `jooble` | Jooble feed | Pull | no | no | unknown | never hidden |
| `sandbox` | Sandbox board (local fake) | Push | yes, after its acceptance delay | yes, after its removal delay | the delays | `Sandbox:Enabled` |

Each adapter implements `IJobBoardAdapter` (`Validate`, `PublishAsync`, `UpdateAsync`, `CloseAsync`,
`GetStatusAsync`, and `Map` through `IJobBoardAdapter<TPayload>`) and declares `BoardCapabilities`: the mode,
`CanClose`, `CanUnpublishPerBoard`, `ConfirmsLive`, `ConfirmsRemoval`, the estimates, `DailyQuota` and `PartnerGated`.
`BoardRegistry` keeps only configured adapters, so a hidden board is never listed, offered or posted to.

### What each state means

`JobPosting` is one job on one board: `Draft → Queued → Submitted → Live | Rejected → Closing → Closed`, plus `Error`.

| State | Meaning |
|---|---|
| Draft | Selected, but the board's `Validate` found issues (`issues`), such as Adzuna's 100-character description minimum |
| Queued | Waiting for the worker to call a push board, or for Google's quota |
| Submitted | The board took it and has not confirmed it shows. Pull boards and Google stay here: they never confirm |
| Live | Confirmed showing, by the board's API or by our own server |
| Rejected | The board refused it (`lastError` has its reason) |
| Closing | Removal requested and not confirmed. Pull boards and Google stay here after a close |
| Closed | Confirmed gone, or it never reached the board |
| Error | A push board did not answer after `MaxResends` resends. Publishing again retries it |

Nothing moves a posting to Live or Closed on a timer. A board that never reports back keeps its posting in Submitted
or Closing for good, with `expectedBy` as its own estimate. The UI says "removal requested; Careerjet refreshes
daily", never "removed". `GET /v1/boards` returns `confirmsLive` and `confirmsRemoval` for exactly that.

Every transition takes the time and returns whether it changed anything. A stale version, a repeated close, or a
publish answer that arrives after a close is a no-op. `JobPostingTests` covers the table.

### One-click publish and teardown

- **Publish:** `POST /v1/jobs/{jobId}/publish`, with an optional body `{ "boards": [...] }`. In one transaction it
  freezes a new `JobVersion` (as before), creates or updates the postings, and runs the adapters that change only what
  we serve (hosted, pull, notify). It publishes `SyncJobPosting` for each push board and one `JobPostingsChanged`. The
  response is `JobDetail` with `postings`, each with its state and estimate. With no `boards`, the first publish goes to
  every board in `GET /v1/boards`; a later one updates the boards the job is already on. A board the job was taken down
  from stays down until a request names it. An unknown board is a 400 on `boards`.
- **Teardown:** `POST /v1/jobs/{jobId}/close` closes the job and takes it down everywhere in the same transaction. The
  careers page serves 410 at once, so its posting is Closed. Feeds and the job-board API drop the job at once, and
  their postings go to Closing. Google's `URL_DELETED` is queued. Each push board gets a `SyncJobPosting` close.
- **One board:** `POST /v1/jobs/{jobId}/postings/{boardId}/close` takes one board down while the job stays open. The
  careers page and Google cannot do that (`canUnpublishPerBoard` false: Google would re-index the live page), so the
  call is a 409.
- **Status:** `GET /v1/jobs/{jobId}/postings` and `GET /v1/jobs/{jobId}` (`postings`). `GET /v1/boards` lists the
  boards an organization may use.

### Worker, idempotency and reconciliation

- `SyncJobPosting` (`ITenantMessage`) carries `(jobId, boardId, jobVersion)`, the action and the posting's request
  `Sequence`. Its `IdempotencyKey` is all five, and the handler is `[Deduplicated]` on it: the worker records a claim
  per key in `wolverine.wolverine_deduplication_hashed`. The same request delivered twice runs once. A later request for the same version, such as posting again after a close or a resend, is new.
- The handler takes a row lock on the posting (`SELECT ... FOR UPDATE` in the handler's transaction), so a publish, a
  close and a status check for one posting never interleave around a board call. It acts only while the posting still
  wants exactly that request, which makes the effect idempotent after the deduplication window too.
- When a board answers "not yet" with a time, the handler schedules `ReconcileJobPosting` for that time. That message
  calls `GetStatus`. With no time given, it backs off from `StatusPollInterval` (1 min) up to an hour.
- `ReconcileJobPostings` runs every five minutes in the worker. It walks the organizations, as `ReportOpenJobs` does,
  and sends `ReconcileJobPosting` for push postings whose check is overdue or whose request never got through in
  `ResendAfter` (10 min). That covers a lost schedule or a dead-lettered call. A Queued posting goes to Error after
  `MaxResends` (5). A Closing one is resent for as long as it takes, because a board that may still show the job is
  never given up on.
- `JobPostingsChanged(organizationId, jobId, changedAt)` is the tenant event. The API raises one per publish or
  close, however many boards it touched, and the worker raises one per board answer. Dashboards refetch the job's
  postings on it. One event per change, not per board, keeps the queues quiet: a per-board event put nine events on
  the `realtime` and `worker` queues for every publish, and the integration suite's live-update tests timed out
  behind them.

### Feeds

`GET /v1/public/organizations/{slug}/feeds/{boardId}.xml` is a full snapshot of the organization's active postings
(Queued, Submitted, Live) on that board, rendered from each posting's frozen `JobVersion`, never the live job. The
response carries `Cache-Control: public, max-age=300`, an `ETag` and `Last-Modified`, and answers a matching
`If-None-Match` with 304. The body's build time is the board's last posting change, not the request time, so an
unchanged feed keeps its ETag. Content goes in CDATA, and characters XML cannot carry are dropped. Feeds are per
tenant. One ATS-wide feed across tenants would need a materialized snapshot built by the worker, because row-level
security gives the API no cross-tenant read.

| Board | Format | Source (accessed 2026-10-20) |
|---|---|---|
| `xml-feed` | `<source>` with publisher details, then `<job>` with every structured field (all locations, applicant countries, salary range, version) | ours; `tests/ScreenHire.UnitTests/Distribution/Schemas/xml-feed.xsd` is its definition |
| `careerjet` | `<jobs><job>` with a nested `<location>`, `contract_type`, `working_hours`, a salary line | careerjet.co.uk/docs/feeds/xml (format and sample) |
| `talent` | `<source>` with publisher details, `<job>` with `referencenumber`, city, state, country, `isremote`, nested salary | talent.com/integrations (element list) |
| `adzuna` | `<jobs><job>` with `location`, ISO country (`UK` for GB, as Adzuna's list says), `salary_min`, `salary_max`, `salary_frequency`, `contract_type`, `contract_time` | Adzuna's XML specification, read through a mirror (docs.qhse-professionals.nl/job-feeds/xml-feeds/adzuna); Adzuna's page refused automated reads |
| `jooble` | `<jobs><job id>` with `link`, `name`, `region`, dates as `dd.MM.yyyy` | a live feed Jooble indexes (qhse-professionals.nl/em-jobfeed/jooble). Jooble's specification PDF could not be read as text, so the date format is unverified |

`FeedFormatTests` validates every feed, for a spread of jobs, against a schema written from each source in
`tests/ScreenHire.UnitTests/Distribution/Schemas` (each file names its source). Where a board publishes no XSD, the
schema is derived from its description or sample, which the file says. Boards' own validators were not run.

### Public job-board API

`GET /v1/public/boards/{slug}`, `/jobs` (with `?content=true` for descriptions) and `/jobs/{jobId}` mirror
Greenhouse's job-board API: snake_case fields such as `absolute_url`, `updated_at`, `first_published`, `location.name`,
`departments`, `offices` and `pay_input_ranges` (in minor units), plus `meta.total`. Tools built for Greenhouse boards
read it. Ids are our UUIDs, and `content` is sanitized HTML, which Greenhouse escapes. It lists postings on the
`job-board-api` board, with the feeds' five-minute cache.

### Careers page and Google for Jobs

- The job page renders complete `JobPosting` JSON-LD from `apps/careers/src/lib/job-posting.ts`: title,
  description, `identifier`, `url`, `datePosted`, `validThrough`, Google's `employmentType` values (`CONTRACTOR`,
  `INTERN`, not our enum names), `hiringOrganization` with `sameAs`, a location or `TELECOMMUTE` with
  `applicantLocationRequirements`, and `baseSalary`, also when only a minimum or a maximum is set. It also has a
  canonical link.
- A closed job that was published is `410 Gone` from `GET /v1/public/organizations/{slug}/jobs/{jobSlug}`, with its
  last content and `validThrough` set to when it closed. The careers page serves 410 with `JobUnavailable.astro`, which
  carries that expired JSON-LD in its head and has no apply link. A draft, or a job closed before it was ever published,
  stays 404.
- `/{slug}/sitemap.xml` lists the board page and each open job with `lastmod`.
- Every URL (canonical, sitemap, feeds, job-board API, Google notifications) is `DomainOptions.CareersJobUrl`, built
  from `ScreenHire:Domains:CareersHostPattern` and `CareersDomain`. The AppHost points `CareersDomain` at the careers
  app's real endpoint. The careers app reads the same `ScreenHire:Domains:*` keys, and on a tenant host its middleware
  maps the host onto the path routes (`apps/careers/src/lib/tenancy.ts`), so `https://acme.careers.example.com/jobs/x`
  serves `/acme/jobs/x`. Without a host pattern, as in local development and the tests, routing stays by path.
- **Google Indexing API:** `ScreenHire:Distribution:GoogleIndexing:Mode` is `Off` (default, board hidden), `Local`
  (an in-process fake that accepts everything; the tests use it) or `Google` (`ServiceAccountKeyJson`, through
  `Google.Apis.Auth`, POST `urlNotifications:publish`). Publish and close queue an `IndexingNotification`, and a newer
  one for the same posting supersedes one still waiting, so an update queued before a close is never sent. Every
  minute `DispatchIndexingNotifications` walks the organizations and spends what is left of the day's quota
  (`DailyQuota`, default 200, counted per UTC day in `indexing_quota_days` for the whole deployment), deletes first
  and then oldest (`IndexingPlan`). It hands each notification to `SendIndexingNotification`, which runs as its
  organization. An accepted `URL_UPDATED` moves the posting to Submitted. An accepted `URL_DELETED` leaves it Closing.

### Sandbox board

`ScreenHire:Distribution:Sandbox` (`Enabled`, `AcceptanceDelay`, `RemovalDelay`) turns on a push board whose "remote"
storage is the `sandbox_listings` table. It accepts a posting after the acceptance delay and removes it after the
removal delay. Only then does `GetStatus` report Live or Closed. `GET /v1/public/sandbox-board/{slug}/listings` shows
what it lists now, and is a 404 while it is off. The AppHost's `appsettings.Development.json` enables it (20 s and
30 s), so it never reaches real tenants; the test stack enables it with 4 s (`AppHostFixture.SandboxDelay`).

### Partner boards

`Organization.IsTrial` and `Organization.IndeedEligible` (set by us, never by the tenant) model Indeed's gates. A board
with `PartnerGated` is offered only when `MayUsePartnerBoards` (eligible and not a trial). No partner board exists in
the MVP.

### Configuration

All under `ScreenHire:Distribution` (`DistributionOptions`). The AppHost forwards every key it is given to both the
API and the worker, so they agree on which boards exist: `Sandbox:*`, `GoogleIndexing:Mode` and `DailyQuota`,
`StatusPollInterval`, `ResendAfter` and `MaxResends`. The one exception is the Google service account key. With
`GoogleIndexing:Mode=Google` the AppHost declares the secret parameter `google-indexing-service-account-key` (user
secrets locally, the secret store in a deployment) and passes it to both as `GoogleIndexing:ServiceAccountKeyJson`.

### Not built yet

- Auto-expiry: nothing closes a job when its `validThrough` passes yet.
- One ATS-wide feed across tenants, and the CDN in front of feeds.
- Per-domain Search Console verification, and custom tenant domains (host routing covers `{tenant}.{CareersDomain}` only).
- Credential vault, inbound apply webhooks, and every partner or paid board (§4 phases 2 to 4).
- The distribution UI (the recruiter-dashboard task builds it on these endpoints).

### Tests

| Test | Proves |
|---|---|
| `JobPostingTests` (unit) | every transition, issues keeping a posting in Draft, stale versions, no Closed without confirmation, resends and Error |
| `DistributionPlannerTests`, `IndexingPlanTests` (unit) | the default boards, per-board states and estimates on publish and close, one event per change, one command per push board, the idempotency key, partner gating, hidden boards; deletes first within the quota |
| `FeedFormatTests` (unit) | every feed validates against its board's schema; CDATA, `]]>` and control characters; per-board field mapping; validation issues |
| `DistributionTests` (integration) | publish puts the job in every feed, the job-board API, the Indexing queue and the sandbox; close serves 410, drops it from the feeds, queues `URL_DELETED`, and the sandbox reports Closed only after its delay; per-board teardown; new versions; cache headers and 304; tenant isolation; the dispatcher sends queued notifications and Google never reaches Closed; a duplicate board command leaves one claim for its key and changes nothing |
| `CareersDistributionTests` (end-to-end) | the published job's page carries complete JSON-LD and a canonical link and is in the sitemap; closed, it serves 410 with a past `validThrough` and leaves the sitemap |
| `job-posting.test.ts` (Vitest) | the JSON-LD builder and the sitemap |
