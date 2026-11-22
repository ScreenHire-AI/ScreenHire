---
type: Design
title: "Employer branding"
description: "An organization's brand colour, second colour, heading font, logo, favicon and sharing image: the WCAG 2.2 AA check that refuses a failing colour with the nearest passing shade, heading fonts from the Google Fonts catalogue that ScreenHire downloads once and hosts, brand image uploads straight to storage with their type, size and malware scan, how the careers pages serve fonts and images on their own origin, the header at the top of every email to candidates, and what the embeddable widget reuses."
tags: [branding, careers, email, storage]
sources:
  - resource: src/ScreenHire.Domain/Organizations/BrandColorCheck.cs
  - resource: src/ScreenHire.Domain/Organizations/BrandPalette.cs
  - resource: src/ScreenHire.Domain/Organizations/BrandImage.cs
  - resource: src/ScreenHire.Domain/Organizations/HeadingFont.cs
  - resource: src/ScreenHire.Domain/Organizations/HostedFont.cs
  - resource: apps/api/Fonts/HeadingFontCatalogue.cs
  - resource: apps/api/Fonts/HeadingFontHosting.cs
  - resource: apps/api/Fonts/HeadingFontPreviews.cs
  - resource: apps/api/Fonts/FontsourceSource.cs
  - resource: apps/api/Fonts/FontDownloads.cs
  - resource: apps/api/Fonts/SandboxFontSource.cs
  - resource: apps/api/Fonts/FontsOptions.cs
  - resource: apps/api/Endpoints/HeadingFontEndpoints.cs
  - resource: scripts/heading-font-styles.mjs
  - resource: apps/careers/src/pages/[organization]/brand/fonts/[font]/[file].ts
  - resource: apps/dashboard/src/lib/components/heading-font-sheet.svelte
  - resource: apps/dashboard/src/lib/heading-font-faces.svelte.ts
  - resource: src/ScreenHire.Domain/Organizations/ImageDimensions.cs
  - resource: src/ScreenHire.Infrastructure/Email/EmailHeader.cs
  - resource: src/ScreenHire.Application/Branding/EmailHeaders.cs
  - resource: src/ScreenHire.Application/Branding/ScanBrandImage.cs
  - resource: apps/api/Branding/BrandImageUploads.cs
  - resource: apps/api/Branding/PublicBrandings.cs
  - resource: apps/api/Contracts/BrandingContracts.cs
  - resource: apps/api/Endpoints/OrganizationEndpoints.cs
  - resource: apps/careers/src/lib/brand.ts
  - resource: apps/careers/src/pages/[organization]/brand/images/[file].ts
  - resource: apps/careers/src/layouts/CareersLayout.astro
  - resource: packages/ui/src/heading-fonts.ts
  - resource: apps/dashboard/src/routes/(app)/settings/branding/+page.svelte
  - resource: apps/dashboard/src/lib/brand-images.svelte.ts
  - resource: src/ScreenHire.Application/Branding/BrandImageCleanup.cs
  - resource: apps/worker/BlobBrandImageStore.cs
---
# Employer branding

Settings, Careers site, Branding holds an organization's look: a brand colour, a second colour, a heading font, a logo, a favicon
and a social sharing image. Every part is optional and falls back to ScreenHire's. They apply to the careers pages
and the apply flow (`CareersLayout`), the embeddable widget (the same pages in a frame, so the same stylesheet and image
routes below; [careers widget](careers-widget.md)), and the top of every email to a candidate. The decisions are ADR-0018; how the screens look is the
design system, section 18.

## 1. Data

`Organization` holds `BrandColor`, `SecondColor` (both `#rrggbb`), the heading font (`HeadingFontId`, a Fontsource id
such as `roboto-slab` in the `heading_font` column, and `HeadingFontFamily`, its name for History; both null for
Bricolage Grotesque) and the three images in use, `LogoImageId`, `FaviconImageId` and `SharingImageId`. Each id names a
`BrandImage`, a tenant-owned row (`brand_images`, row-level security) with the blob's name, its type, size, pixel size
and malware scan status. The upload's file name is not a column: it is the last part of the blob name, and the API
returns it as `fileName` so the Branding tile can name the file. The image columns are not foreign keys: Postgres checks a foreign key without row-level
security, so a key would accept another organization's image. The branding save looks each id up within the tenant
instead. An image that was saved into the branding is never deleted, because emails already sent point at it, even after
a newer logo replaces it: `BrandImage.SavedAt` records the first save and never clears (section 4 has the clean-up of
the ones nobody saved).

`PUT /v1/organization/branding` replaces the whole branding in one save, so History records one change with a line
per part ("changed the heading font to **Roboto Slab**", "replaced the logo"; `SettingsChanges.Branding`). Branding was
already a tracked kind; the person who saves is the actor. A scan finishing never changes the organization, so the
worker never appears in this history.

## 2. The contrast check

A colour is used as it is given, and a colour that fails does not save (`BrandColorCheck`, WCAG 2.2 AA):

| Colour | Used for | Pair | Minimum |
|---|---|---|---|
| Brand colour | The Apply button and every filled button, links, focus rings | White or ink, whichever reads better, on it ("Text on buttons") | 4.5:1 |
| | | It on the page background `#fbf6ee` ("Links"; also covers white cards and the 3:1 for borders and focus) | 4.5:1 |
| Second colour | The careers header band, the email's top band | White or ink on it ("Text on a header") | 4.5:1 |

A failing colour's nearest passing shade moves its OKLab lightness and keeps its hue: down for the brand colour (links
need a dark colour on a light page), the nearer of up and down for the second colour. Ratios are cut down to one
decimal, so 4.47 reads 4.4:1, never 4.5:1. `GET /v1/organization/branding/contrast?brandColor=&secondColor=` runs the
check without saving, for the page to show as someone types; the save runs it again and answers 400 with each failing
pair in words and the shade. Everything in the careers header, its links and focus ring included, uses the header's
text colour, so the band needs no other pair.

Colours saved before the check still render through `BrandPalette.Derive`, which darkens a failing one until it
passes. A colour that passes derives a palette of itself.

## 3. Heading fonts

The heading font is any Google Fonts family under the SIL Open Font License or Apache 2.0 (ADR-0031). Candidates'
browsers never ask Google, Fontsource, jsDelivr or any other font host for anything: ScreenHire downloads the family
once and serves it from the careers origin (non-negotiable 4). The body font stays ScreenHire's. Emails load no web
font: a font request from a mail client would be one more thing that tells the sender an email was opened.

**The six bundled fonts.** Bricolage Grotesque (the default, which is no font at all), Inter, Montserrat, Nunito,
Fraunces and Playfair Display are `@fontsource-variable` packages in `packages/ui`, declared by `heading-fonts.css`, so
a page downloads a face only when it uses it. `heading-fonts.ts` keys them by their Fontsource ids, which
`HeadingFonts.Bundled` in the domain also lists. Picking one downloads nothing. The migration to ids
(`HostAnyHeadingFont`) mapped each enum name the column held to the same font, so no choice changed.

**The catalogue.** `IFontSource` is where families come from: `FontsourceSource` in production, through Fontsource's
read-only API, which needs no key (`ScreenHire:Fonts:Fontsource:*`); `SandboxFontSource` locally and in tests
(`ScreenHire:Fonts:Sandbox:Enabled`, on in `appsettings.Development.json` and the test stacks). The sandbox is twelve
real families bundled in the API (`apps/api/Fonts/Sandbox`, each WOFF2 file with its licence text), plus Ubuntu under its
own licence, which hosting refuses. `HeadingFontCatalogue` keeps a family only when Google publishes it, its licence is
OFL-1.1 or Apache-2.0, it has a latin subset, its category is not icons, and its id and name have the safe shape
(below). The six are always in the list, marked `bundled`. It is sorted by name and kept in memory for
`CatalogueLifetime` (a day). When the source cannot be reached, `GET /v1/heading-fonts` answers with the six and every
family already hosted, `complete: false`, and caches nothing.

Each family has the picker's styles: Sans, Serif and Display from Fontsource's category (handwriting is Display;
monospace is under no filter), and Rounded and Slab from Google's own tags, which Fontsource lacks.
`apps/api/Fonts/google-font-styles.json` holds them, generated by `node scripts/heading-font-styles.mjs` from
`tags/all/families.csv` in github.com/google/fonts (a family counts at a weight of 50 or more); run it when Google adds
families.

**Hosting on save.** `PUT /v1/organization/branding` takes `headingFont` as an id. After the colours and images pass,
`HeadingFontHosting` makes the font servable: a bundled one needs nothing, and a family already in `hosted_fonts` needs
nothing. Any other family must be in the catalogue, so a crafted id never makes the server fetch anything. The save then
downloads it, once, for every organization:

- the weights the careers titles use, 600, 700 and 800 (a title set at 650 is drawn with the 700 file), as the nearest
  each of 600, 700 and 800 the family has (`PickWeights`: 100 to 900 gives 600, 700 and 800; 400 and 700 gives 700; 400 alone
  gives 400);
- in latin and, where the family has it, latin-ext, upright only;
- each file checked to start with the WOFF2 signature and kept under 2 MB, at an address under
  `Fontsource:FilesUrl` only;
- its licence text (`LICENSE` from the package), without which the family is refused.

The files go into the private `heading-fonts` container (`ScreenHire:Fonts:Container`) as
`{id}/{version}/{subset}-{weight}.woff2` with `LICENSE.txt` beside them, then one `HostedFont` row records the family,
its category, its licence, the version and each file with its unicode range. The row is not tenant-owned and has no
row-level security. Font files are public, licensed for redistribution and the same for everyone, and nothing in the
row says who picked the family. Its key is the id, so two first saves at once insert it once. A row is never changed
and a hosted family is never fetched again. The download runs in the save's request, not the worker, so a save never
names a font the careers pages cannot serve. A failure refuses the save on `headingFont` ("We could not download this
font just now"). The organization keeps the id and family name, and History names the family.

**Only an id reaches CSS.** `PublicBranding.headingFont` gives the careers pages the id, the family, the category,
`bundled` and, for a hosted font, its files. The brand stylesheet (`brandStylesheet` in `apps/careers/src/lib/brand.ts`)
sets `--sh-font-display` to a bundled font's stack from `heading-fonts.ts`, or declares one `@font-face` per file under
`ScreenHire heading {id}` with `font-display: swap` and the file's `unicode-range`, so a page downloads only the
subsets and weights its titles draw. Its fallback is a serif for a serif and ScreenHire's sans otherwise. The id, file
names, weights and unicode ranges are checked against the shapes the API sends, and the stylesheet throws on anything
else. The family name the API sent never reaches a stylesheet. The domain refuses an id that is not lower-case words
joined by single hyphens, and a family that is not letters and digits in single-spaced words
(`HeadingFonts.IsValidId`, `IsValidFamily`). Every Google Fonts id and name has that shape. `CareersLayout` preloads the
hosted font's latin file nearest 700, so the title more often draws in it on first paint.

Measured on 2026-11-22 with mobile Lighthouse (performance only, median of three runs) on the production builds
(`ScreenHire:Frontends=Built`), on Brightwater's jobs list and a job page. With ScreenHire's font, both scored 100,
with LCP 1,058 and 1,057 ms. With hosted Merriweather they scored 100 again, with LCP 1,062 and 1,063 ms. CLS was 0 and
0.0015 both times. The hosted family added two files of 48 KB each (latin 700 and 800), both from the careers origin,
and no request left it.

**The picker.** Branding's Heading font panel offers the six and ScreenHire's, the catalogue font in use or just
picked, and More fonts, a sheet (`heading-font-sheet.svelte`) with a search, the five style filters and the whole
catalogue 40 at a time. Each name is drawn in its own face. A family's face loads from
`GET /v1/heading-fonts/{id}/preview` only when its row scrolls into view (`heading-font-faces.svelte.ts`): its latin file
nearest 700, downloaded the first time anyone opens it, kept in storage as `previews/{id}.woff2` and rate-limited per
person (`PreviewsPerMinute`, 120 a minute, which keeps browsing well inside jsDelivr's terms; ADR-0031). A preview gets
the same WOFF2 and 2 MB check as a hosted file, and a kept one is never fetched again, even after a new release of the
family: it only draws the name. A bundled font has no preview route, because the dashboard ships it. The Branding preview
draws a catalogue font in the same face. Nothing reaches candidates until Save.

## 4. Brand images

PNG or JPEG only, never SVG (it can carry script), WebP or GIF (some mail clients show neither). Limits, checked on
the stored bytes (`BrandImageRules`): a logo up to 1 MB of any shape; a favicon up to 256 KB, square, at least 32
pixels; a sharing image up to 5 MB and at least 600 by 315 pixels. No side is over 4,096 pixels. `ImageDimensions`
reads the pixel size from the PNG or JPEG header; nothing decodes or resizes an image.

An upload mirrors a candidate's (apply-flow.md section 3), so file bytes never pass through the API:

1. `POST /v1/organization/branding/images` with the kind, file name and size returns a create-only link into the
   private `brand-images` container (`ScreenHire:Branding:Container`), named `{organization}/{upload}/{kind}/{file}`.
2. The dashboard PUTs the file to the link.
3. `POST /v1/organization/branding/images/{id}/verify` reads the blob back, checks the type by its first bytes, the
   size and the pixel size, deletes it if it fails, and otherwise adds the `BrandImage` (Pending) and publishes
   `ScanBrandImage` in the same transaction.
4. The worker scans it (`IMalwareScanner`, given the container and the blob) and records the result.

The page asks `GET /v1/organization/branding/images/{id}` each second while the scan runs, and its Save waits. The
save refuses an image still being scanned or one the scan blocked. As for applicant files, Clean and NotScanned (no
scanner configured, as locally) are servable; Pending and Infected never are.

An upload nobody saves is removed a day later. `PurgeExpiredBrandImageUploads` runs daily (03:27 UTC) and fans out one
`PurgeOrganizationBrandImageUploads(OrganizationId, Cutoff)` per organization, as the draft sweep does
(`DraftRetention.cs`). For its organization it deletes each `BrandImage` with no `SavedAt` made on or before the cutoff,
the row first and only while it is still unsaved (so an owner saving at that moment keeps both), then its blob; it also
deletes blobs under `{organization}/` older than the cutoff that have no row, which are uploads nobody verified. This
is the worker's only write to storage: `IBrandImageStore` (a blob implementation in the worker, a no-op where no
storage is configured) lists and deletes in the brand images container and never reads or writes an image. Every image
uploaded before `SavedAt` existed was marked saved by its migration, because nothing recorded which old uploads had
been in use. Azurite has no lifecycle rules, which is why a Wolverine schedule does this and not a storage rule. A branding save
that names an upload the sweep removed a moment earlier is refused like any missing image, with a validation problem on
that slot (not a 500).

## 5. Serving

`GET /v1/public/organizations/{slug}/branding/images/{id}` streams a servable image of that organization, anonymous,
with its type, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'` and
`Cache-Control: public, max-age=31536000, immutable`: the bytes never change under an id. Careers pages serve it on
their own origin at `/brand/images/{id}.{png|jpg}` (`[organization]/brand/images/[file].ts`, organization from the
host as for the stylesheet), so the content security policy keeps `img-src 'self'`. Pages link the logo and the
favicon relative to the tenant base; `og:image`, the JobPosting's `hiringOrganization.logo` and emails use the
absolute careers address the API returns (`BrandImage.UrlUnder`, from `CareersBoardUrl`). Any servable upload is served, saved or not, so the
page's preview can show an image before Save; its id is random, and it is the organization's own image. The other
choice, serving only the images in use and previewing through a signed-in route, needs a second route for the same
bytes.

`PublicBranding` on every public organization and board response holds the palette, the header colours, the font and
the servable images, each null for ScreenHire's. `brandStylesheet` turns it into the hashed per-tenant stylesheet
(apply-flow.md section 5).

**Heading fonts.** `GET /v1/public/heading-fonts/{id}/{file}` streams a file of a hosted family, or its `LICENSE.txt`,
anonymous, with `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'` and a year's immutable
cache. Only a file the family's row names is served, so nothing else in the container can be reached. Careers pages serve
the files on their own origin at `{base}/brand/fonts/{id}/{file}` (`[organization]/brand/fonts/[font]/[file].ts`), which
the brand stylesheet links relative to itself, so the content security policy keeps `font-src 'self'`. An answer from the
API that is not the file (an outage, a missing name) is never cached: the careers route sends 502 for a server error and
404 otherwise, both with `Cache-Control: no-store`, so a moment's outage does not stick to a file that will be there.

## 6. Emails

`EmailTemplates.ToHtml` starts every email to a candidate with a header (`EmailHeader`, built by `EmailHeaders` in the
API and the worker): the logo, scaled down to 40 pixels tall and at most 240 wide, with the organization's name as its
alt text, or the name in bold, on a band of the second colour or on white. The logo is not a tracking pixel: its
address is the same for every recipient, with no query string and nothing that names the candidate, the application
or the email, it is cached for a year, and the route records nothing per request beyond the server's own logs. A
template's own markup still reads as text, so the header's logo is the only image an email carries. The plain-text
part has no header. Kept copies hold the HTML as it went.

## Owner actions

- Create the private `brand-images` container in production storage (Development creates it at start). The Azure
  definition creates it and `heading-fonts`, which the API writes the hosted fonts and previews to with its storage role.
- Let the API reach `api.fontsource.org` and `cdn.jsdelivr.net` over HTTPS. Container Apps allows outbound traffic by
  default; a network rule added later has to keep both, or a new family cannot be saved (the six and every hosted family
  keep working).
- Add the dashboard's origin to the storage account's CORS rule for PUT, beside the careers origin.
- Give the worker's identity in production storage permission to list and delete blobs in the `brand-images` container
  (Storage Blob Data Contributor on that container). Until then the daily clean-up fails and retries, and no image is lost.

## Not built

- A demo logo for Brightwater: the seed keeps its brand colour only.
