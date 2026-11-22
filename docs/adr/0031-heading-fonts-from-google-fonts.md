---
type: Decision
title: "ADR-0031: Heading fonts come from the whole Google Fonts catalogue, and ScreenHire hosts each one"
description: "An employer's heading font is any Google Fonts family under the OFL or Apache 2.0, listed live from Fontsource with no key; the branding save downloads the family's files once, for every organization, into our storage with its licence, and the careers pages serve them on their own origin, so candidates' browsers never ask a font host for anything. The six bundled fonts stay."
tags: [adr, branding, careers, fonts]
---
# ADR-0031: Heading fonts come from the whole Google Fonts catalogue, and ScreenHire hosts each one

- **Status:** Accepted (2026-11-22)
- **Deciders:** Owner, from the fourth walk-through of the preview (2026-11-21, note 27)
- **Amends:** ADR-0018 decision 3 ("six heading fonts")
- **Detail:** `docs/architecture/branding.md` section 3, design system section 18

## Context

ADR-0018 gave employers six heading fonts, each an npm package in `packages/ui`. The owner found six too few. Google
Fonts has about 1,900 families, nearly all under the SIL Open Font License. Linking Google's stylesheet would send every
candidate's browser to Google before they apply, which non-negotiable 4 rules out: browsing collects only our own
server and security logs. Bundling 1,900 packages would make every install and build slower for fonts nobody uses.

Fontsource (fontsource.org) lists every Google Fonts family through a read-only API with no key. Its documentation
(fontsource.org/docs/api/introduction) says the API "can be accessed by any HTTP client". Its hard limit is 2,500
requests in 10 seconds, constant use near that can bring a temporary ban, and it reserves the right to throttle under
fair use. It asks API users to sponsor the project and sets no other condition. Each family has its licence's SPDX id,
its category and per-weight, per-subset WOFF2 files on jsDelivr. Each package carries its licence text.

jsDelivr's Terms of Use (github.com/jsdelivr/jsdelivr, "Terms of Use.md", effective 5 October 2026) make the CDN "free
for both personal and commercial use" with "no limits on bandwidth or number of requests". They prohibit using it as
general file hosting and "scraping large amounts of otherwise infrequently accessed content": "Requesting unique files
across hundreds of packages or repositories for more than a few minutes may be considered abuse." Content fetched
through it must conform to its source's terms, which here are the fonts' own licences.

## Decision

1. **The catalogue is live.** The API lists Fontsource's catalogue once a day and keeps it in memory. It offers the
   families Google publishes under the OFL or Apache 2.0 that have latin letters, and no others. When Fontsource cannot
   be reached, the six bundled fonts and every family already hosted stay pickable.
2. **The picker has Sans, Serif, Display, Rounded and Slab.** The first three come from Fontsource's category
   (handwriting counts as Display). Fontsource has no Rounded or Slab, so those two come from Google's own tags
   (`tags/all/families.csv` in github.com/google/fonts). A committed snapshot holds them, and
   `scripts/heading-font-styles.mjs` refreshes it.
3. **The branding save hosts the family, once, for everyone.** The first save that names a family downloads the weights
   the careers titles use (600, 700 and 800, or the nearest the family has), in latin and latin-ext, and its licence
   text. They go into the private `heading-fonts` container and one `hosted_fonts` row. Later saves of that family, by
   any organization, download nothing. The download runs in the save's own request rather than in the worker, so a save
   never names a font the careers pages cannot serve. A failed download refuses the save with a message and leaves the
   branding as it was.
4. **Candidates' browsers talk only to the careers site.** The careers pages serve the files at
   `{base}/brand/fonts/{id}/{file}`, proxied from the API, so `font-src 'self'` holds. The picker's previews come
   through the API too (`/v1/heading-fonts/{id}/preview`), so the dashboard calls no font host either.
5. **Only an id goes into CSS.** The careers stylesheet declares a hosted family under a face name built from its id
   (`ScreenHire heading {id}`). The id, the file names, the weights and the unicode ranges are each checked against the
   shape the API sends. The family name the API sends never reaches a stylesheet. The domain refuses an id or family
   name outside letters, digits, single spaces and single hyphens before anything is saved.
6. **The six bundled fonts stay.** Bricolage Grotesque (the default), Inter, Montserrat, Nunito, Fraunces and Playfair
   Display load from the careers bundle with no download. The migration maps each saved enum name to the same font's
   id, so nobody's choice changes.
7. **Locally and in tests the catalogue is a sandbox.** Twelve real OFL and Apache families are bundled in the API with
   their licence texts, plus one under another licence that hosting must refuse. They sit behind the same interface as
   Fontsource, so nothing needs the internet.
8. **The body font stays fixed.** Uploading an organization's own font is on the roadmap, not in this decision.

## Consequences

- Picking a new family makes the first save take a moment longer: a handful of files of 10 to 50 KB.
- `hosted_fonts` is a table with no organization and no row-level security. Font files are public, licensed for
  redistribution and the same for everyone, and the row records nothing about who picked the family.
- A hosted family is never replaced. Its files are cached for a year under their names, and a newer release of the
  font is not fetched.
- Production needs the `heading-fonts` container (the Azure definition creates it) and outbound HTTPS from the API to
  `api.fontsource.org` and `cdn.jsdelivr.net`.
- ScreenHire stays well inside both sets of terms. The catalogue is one request a day. A family is downloaded once,
  ever, as a handful of files. A preview is fetched once, ever, then served from our storage, and each person may open
  at most `PreviewsPerMinute` (120 by default) a minute, so nobody's browsing turns into a crawl of hundreds of packages.
- If Fontsource starts asking for a key or closes, the `IFontSource` interface takes another source, such as a
  committed snapshot of the catalogue, with no change to the picker or the careers pages.
