---
type: Decision
title: "ADR-0018: Employer branding refuses failing colours, hosts six heading fonts and serves images on the careers origin"
description: "Branding v1: a colour is used as given and refused with the nearest passing shade when it fails WCAG 2.2 AA, the second colour fills the header band with white or ink on it, six self-hosted OFL heading fonts, PNG and JPEG images only, served same-origin at one address per image, and an email logo that can never be a tracking pixel."
tags: [adr, branding, design-system]
---
# ADR-0018: Employer branding refuses failing colours, hosts six heading fonts and serves images on the careers origin

- **Status:** Accepted (2026-10-30)
- **Deciders:** Owner, through roadmap item 6
- **Amends:** ADR-0009's employer branding ("computed and contrast-checked server-side")
- **Detail:** `docs/architecture/branding.md`, design system section 18

## Context

Branding was one colour, which the server darkened in silence until text on it passed. People saw a different colour
from the one they picked, and the page could only say it had been changed. Roadmap item 6 adds a second colour, a
heading font, a logo, a favicon and a sharing image, used on the careers pages, the apply flow, the embeddable widget
and every email to a candidate, and asks that colours be checked before they save, naming the pair that fails and
the nearest shade that passes.

## Decision

1. **A colour is used as it is given, and a colour that fails does not save.** The brand colour needs white or ink at
   4.5:1 on it (its buttons) and 4.5:1 itself on the page background (its links); the second colour needs white or ink
   at 4.5:1 on it. A refused colour gets each failing pair in words and the nearest passing shade, found by moving
   its OKLab lightness and keeping its hue. Colours saved before keep rendering through the old darkening, as a
   fallback only.
2. **The second colour is the header band** of the careers pages and the email's top band. Everything inside the
   careers header, its focus ring included, uses the band's text colour, so the band needs only its one pair.
3. **Six heading fonts, all SIL Open Font License, self-hosted** in `packages/ui` as `@fontsource-variable` packages:
   Bricolage Grotesque (ScreenHire's, the default), Inter, Montserrat, Nunito, Fraunces and Playfair Display. Never a
   font CDN. Emails load no web font.
4. **Images are PNG or JPEG only,** checked by their first bytes, with a size and pixel limit per kind. Never SVG,
   which can carry script.
5. **Images are served on the careers origin,** at one address per image whose bytes never change, so careers pages
   keep `img-src 'self'` and every page, crawler and email uses the same cached address.
6. **The email logo is never a tracking pixel:** the same address for every recipient, no query string, nothing that
   names a person, an application or an email, and nothing recorded per request.
7. **An image goes live only through a person's Save,** after its malware scan, so History names who changed it.

## Consequences

- The colour a person picks is the colour candidates see; a pale brand colour has to be darker, or become the second
  colour, which can be pale.
- The careers pages, the apply flow and the widget share one stylesheet and one image route.
- Production needs the `brand-images` container and the dashboard's origin in the storage CORS rule (owner actions in
  `docs/architecture/branding.md`).
