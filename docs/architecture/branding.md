---
type: Design
title: "Employer branding"
description: "An organization's brand colour, second colour, heading font, logo, favicon and sharing image: the WCAG 2.2 AA check that refuses a failing colour with the nearest passing shade, brand image uploads straight to storage with their type, size and malware scan, how the careers pages serve them on their own origin, the header at the top of every email to candidates, and what the embeddable widget reuses."
tags: [branding, careers, email, storage]
sources:
  - resource: src/ScreenHire.Domain/Organizations/BrandColorCheck.cs
  - resource: src/ScreenHire.Domain/Organizations/BrandPalette.cs
  - resource: src/ScreenHire.Domain/Organizations/BrandImage.cs
  - resource: src/ScreenHire.Domain/Organizations/HeadingFont.cs
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
---
# Employer branding

Settings, Branding holds an organization's look: a brand colour, a second colour, a heading font, a logo, a favicon
and a social sharing image. Every part is optional and falls back to ScreenHire's. They apply to the careers pages
and the apply flow (`CareersLayout`), the embeddable widget when it lands (task 73 reuses the stylesheet and image
routes below), and the top of every email to a candidate. The decisions are ADR-0018; how the screens look is the
design system, section 18.

## 1. Data

`Organization` holds `BrandColor`, `SecondColor` (both `#rrggbb`), `HeadingFont` (an enum, null for Bricolage
Grotesque) and the three images in use, `LogoImageId`, `FaviconImageId` and `SharingImageId`. Each id names a
`BrandImage`, a tenant-owned row (`brand_images`, row-level security) with the blob's name, its type, size, pixel size
and malware scan status. The image columns are not foreign keys: Postgres checks a foreign key without row-level
security, so a key would accept another organization's image. The branding save looks each id up within the tenant
instead, and brand images are never deleted, because emails already sent point at them.

`PUT /v1/organization/branding` replaces the whole branding in one save, so History records one change with a line
per part ("changed the heading font to **Fraunces**", "replaced the logo"; `SettingsChanges.Branding`). Branding was
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

ScreenHire hosts six fonts under the SIL Open Font License (ADR-0009 admits OFL fonts): Bricolage Grotesque, the
default, and Inter, Montserrat, Nunito, Fraunces and Playfair Display, each an `@fontsource-variable` package in
`packages/ui`. `heading-fonts.css` declares the five; a browser downloads a face only when a page uses it.
`heading-fonts.ts` maps the API's `HeadingFont` names to their families, and a Vitest keeps the two lists equal. The
careers brand stylesheet sets `--sh-font-display` to the chosen family, taken from that table and never from the
API's string, so every title that uses `font-display` takes it. Emails load no web font: a font request from a mail
client would be one more thing that tells the sender an email was opened.

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

## 5. Serving

`GET /v1/public/organizations/{slug}/branding/images/{id}` streams a servable image of that organization, anonymous,
with its type, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'` and
`Cache-Control: public, max-age=31536000, immutable`: the bytes never change under an id. Careers pages serve it on
their own origin at `/brand/images/{id}.{png|jpg}` (`[organization]/brand/images/[file].ts`, organization from the
host as for the stylesheet), so the content security policy keeps `img-src 'self'`. Pages link the logo and the
favicon relative to the tenant base; `og:image`, the JobPosting's `hiringOrganization.logo` and emails use the
absolute careers address the API returns (`BrandImage.UrlUnder`, from `CareersBoardUrl`).

`PublicBranding` on every public organization and board response holds the palette, the header colours, the font and
the servable images, each null for ScreenHire's. `brandStylesheet` turns it into the hashed per-tenant stylesheet
(apply-flow.md section 5).

## 6. Emails

`EmailTemplates.ToHtml` starts every email to a candidate with a header (`EmailHeader`, built by `EmailHeaders` in the
API and the worker): the logo, scaled down to 40 pixels tall and at most 240 wide, with the organization's name as its
alt text, or the name in bold, on a band of the second colour or on white. The logo is not a tracking pixel: its
address is the same for every recipient, with no query string and nothing that names the candidate, the application
or the email, it is cached for a year, and the route records nothing per request beyond the server's own logs. A
template's own markup still reads as text, so the header's logo is the only image an email carries. The plain-text
part has no header. Kept copies hold the HTML as it went.

## Owner actions

- Create the private `brand-images` container in production storage (Development creates it at start).
- Add the dashboard's origin to the storage account's CORS rule for PUT, beside the careers origin.

## Not built

- Cleanup of uploads nobody saved. Only Owners and Admins upload, and each is a small file.
- A demo logo for Brightwater: the seed keeps its brand colour only.
