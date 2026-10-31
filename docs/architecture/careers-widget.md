---
type: Design
title: "Embeddable careers widget"
description: "The job list and the apply flow in a frame on a company's own site: the one-line embed code, the sites allowed to frame the careers pages and how they set frame-ancestors, the embed script and its postMessage sizing from the careers origin only, embed mode inside the frame, the embedding site recorded as the application's source site, cookie-free, and what a company's own policy needs."
tags: [careers, widget, csp, apply]
sources:
  - resource: apps/careers/src/embed/embed.js
  - resource: apps/careers/src/pages/[organization]/embed.js.ts
  - resource: apps/careers/src/lib/embed.ts
  - resource: apps/careers/src/components/EmbedFrame.astro
  - resource: apps/careers/src/middleware.ts
  - resource: apps/careers/src/env.d.ts
  - resource: src/ScreenHire.Domain/Organizations/EmbedOrigins.cs
  - resource: apps/api/Configuration/CareersEmbeddingOptions.cs
  - resource: apps/api/Endpoints/OrganizationEndpoints.cs
  - resource: apps/dashboard/src/routes/(app)/settings/organization/+page.svelte
  - resource: apps/dashboard/src/lib/components/code-block.svelte
  - resource: tests/ScreenHire.Testing/HostPage.cs
---
# Embeddable careers widget

Roadmap item 3. A company pastes one line where its jobs should go on its own site, and the job list and the whole
apply flow show there in a frame. The frame holds ScreenHire's own careers pages, from the careers origin, so the
notice, the telemetry and the Verification checks are the ones on the careers site, and the host page can neither read
nor forge them (ADR-0003). Branding applies inside the frame because the pages are the same.

## 1. The embed code

Settings > Organization, Careers page panel: "Sites that can embed it", then, once a site is on the list, the embed
code with Copy:

```html
<script src="https://brightwater.careers.example/embed.js" async></script>
```

The address is the organization's careers board (`careersUrl` on `GET /v1/organization`) plus `/embed.js`, on a tenant
host or with the organization in the path alike. One snippet frames the board; there is no per-job snippet.

## 2. The sites allowed to frame the careers pages

`Organization.EmbedOrigins` (`text[]`, empty by default) holds up to 20 origins. `PUT /v1/organization/embedding`
(`UpdateCareersEmbedding`, Edit settings, antiforgery) replaces the list; the panel adds and removes one site at a time
and sends the whole list. `EmbedOrigins.Normalize` keeps each as a browser writes `location.origin`:

- a bare name gets `https://`, and a page's address keeps only its site (`www.example.com/careers` saves as
  `https://www.example.com`); the host is lowercase, an IDN is punycode, a default port is dropped;
- https only. Plain http is accepted only for a site on this machine (`localhost`, `*.localhost`, `127.0.0.1`) and only
  with `ScreenHire:CareersEmbedding:AllowLoopback`, which Development and the test stacks turn on (the AppHost forwards
  `CareersEmbedding:*` to the API) and production never does;
- refused, in words keyed `origins[i]`: a `*`, a user name or password, an IP address (a CSP host source cannot say an
  IPv6 one), and more than 20 sites.

Each added and removed site is a line on the Organization page's History ("let **https://www.example.com** embed the
careers page", "stopped ... embedding the careers page"). The list is public: `PublicOrganization` and
`PublicJobBoard` carry it as `embedOrigins`, and the careers pages send it in a header anyway.

## 3. frame-ancestors

The careers middleware is the only writer of `frame-ancestors` (two policies would both be enforced, and `'self'` would
win). A page that knows its organization (the board, a job, the apply page, the confirmation, the privacy page) puts
`{ origins, embed }` in `Astro.locals` from its own frontmatter (`useEmbedding` in `src/lib/embed.ts`), which runs
before the middleware's `await next()` returns; locals survive the tenant-host rewrite. The policy is then:

- `'self'` plus the one site the page is embedded in, when its `embed` value is on the list, so the browser refuses any
  other parent and the site an application records is the real one;
- otherwise `'self'` plus every listed site;
- `'self'` alone on pages that set nothing: follow-up questionnaires, the talent pool link, an unknown organization.

Only entries that are plain origins reach the header, so nothing from the API can add a directive.

## 4. The script and its messages

`src/embed/embed.js` is plain ES2020 with no imports, kept readable so a company can audit what it pastes. The careers
server serves it at `{board}/embed.js` (`[organization]/embed.js.ts`, the same bytes for every organization, cached for
an hour). It:

1. finds its own tag (`document.currentScript`) and takes the board and the careers origin from its `src`;
2. adds `<iframe class="screenhire-careers" title="Jobs">` right after itself, at `{board}?embed={location.origin}`,
   full width, no border, 600px tall to start, set through the style object rather than a `style` attribute, so a host
   policy without `unsafe-inline` still works;
3. on `message`, accepts only `event.origin` equal to the careers origin and `event.source` equal to its own frame, with
   `{ type: 'screenhire:frame', height, title, page }` and a finite, positive height. It sets the frame's height (capped)
   and title, and when a new page loads above the viewport (`page: true`), scrolls the frame's top into view.

Inside the frame, `EmbedFrame.astro` (rendered only in embed mode, so the careers site ships no script for it) posts
that message to the embedding site's origin only: once on load and from a `ResizeObserver` on `<html>`. It also focuses
the error summary, because a browser ignores `autofocus` in a frame from another site.

## 5. Embed mode

A page is in embed mode when its `embed` query value is one of the organization's sites. Then:

- every link it renders keeps `embed=` (`withEmbed`): the board's jobs, Apply, the header, the privacy links, the
  confirmation's links;
- the step form posts to `?_action=apply&embed=...`, so every step stays in embed mode and the apply action can read the
  site from its own address;
- the body drops `min-h-screen` (inside a frame 100vh is the frame, which could then never shrink);
- a job description's links open in a new tab (`newTabLinks`), since another site would try to load in the frame.

The value is a claim, checked against the list on every request; with `frame-ancestors` narrowed to it, only that site
can show the page in a frame. Opened outside a frame, an `embed=` address behaves like the careers site but records the
claimed site, which can only be one of the organization's own.

## 6. The application's source site

The apply action passes the site to `applyStep`, which keeps it only while it is on the form's
`organization.embedOrigins`, and sends it as `embedOrigin`. `SubmitPublicApplication` stores it as
`JobApplication.SourceSite` when it equals one of the organization's sites, and drops anything else; it never refuses
an application over it. `Source` stays `CareersSite`: the observation, the telemetry binding and every Verification
check treat a widget application as any careers-page one, and the automation fact `application.source` keeps its two
values. `ApplicationSummary`, `ApplicationDetail` and the MCP server's application carry `sourceSite`; the dashboard
shows the site's host in the Source column and "Applied 2 hours ago via www.example.com" in the header. The privacy page
says the site is noted (notice version `2026-10-31.2`).

A draft saved inside the widget keeps no site: its emailed link opens on the careers site, and that application has no
source site. Reports still count widget applications under the careers page.

## 7. Cookie-free

Browsers block third-party cookies in a frame, and the careers pages set none anywhere: the apply state travels in one
hidden field, the confirmation is a POST-redirect-GET, and every form post, beacon and upload the framed page makes to
its own origin is `Sec-Fetch-Site: same-origin`, which Astro's origin check, `/telemetry` and `/uploads` accept. The
end-to-end test checks that no careers response in the embedded flow sets a cookie.

## 8. What a company's own site needs

- Its own content security policy, if it has one, allows the careers origin in `script-src` (the script) and
  `frame-src` (the frame).
- Its site is on the list. A site that is not sees the browser refuse the frame (Chromium logs a `frame-ancestors`
  error), and the frame stays empty at its starting height.

There are no owner actions beyond the careers domain itself.

## 9. Tests

- Unit: `EmbedOriginsTests` (normalizing and refusing sites), `SettingsChangesTests` (the history lines),
  `JobApplicationTests` (the source site).
- Vitest: `lib/embed.test.ts` (the helpers and the policy, hostile values included), `lib/embed-script.test.ts` (the
  script against a stand-in page: the frame, sizing, which messages it ignores), `apply-server.test.ts` (a delisted site
  is dropped); the dashboard's organization page and `CodeBlock`.
- Integration: `CareersEmbeddingTests` (saving, refusals, roles, the public responses, the source site and the
  observation).
- End to end: `EmbedTests` serves a company's page from `HostPage`, a loopback server, because Chromium's Local Network
  Access stops a routed page it cannot place on this machine from loading the careers pages here. A listed site embeds
  the board, branded and sized, the candidate applies inside the frame, and the dashboard shows the site; a site off the
  list is refused.
