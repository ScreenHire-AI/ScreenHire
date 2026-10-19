# Job Distribution Engine — In-House One-Click Post & Teardown

> Status: **Draft v0.1 (2026-10-18)**. Owner requirement: build multiposting **in-house as proprietary tech**;
> aggregators only as a fallback. Research: `docs/research/job-boards.md`.

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
