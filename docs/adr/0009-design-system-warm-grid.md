# ADR-0009: "Warm Grid" design direction, shadcn-svelte + daisyUI, one token source

- **Status:** Accepted (2026-10-18)
- **Deciders:** Owner
- **Research:** `docs/research/component-libraries.md` · **Mockups:** "ScreenHire Look & Feel" canvas (flavor D)
- **Spec:** `docs/design/design-system.md`

## Context
Seven look-and-feel flavors were mocked on identical content (recruiter pipeline + mobile apply page). The owner
preferred Signal Grid (C) and Warm Craft (A), asked for blends, and chose **D · Warm Grid**: Signal Grid's dense,
keyboard-first structure (sidebar with saved views and shortcuts, distribution strip, stacked integrity bar, reason
codes, J/K footer) in Warm Craft's paper/ink/indigo palette, with a warm cream/coral candidate side. Brief: *inviting,
but techy/fast and easy to scan*.

## Decision
- **Design direction:** Warm Grid (light) + a warm dark theme derived from flavor G.
- **Typography:** Bricolage Grotesque (display), Hanken Grotesk (UI/body), JetBrains Mono (data, codes, shortcuts) —
  all SIL OFL, **self-hosted** (no Google Fonts requests: privacy/GDPR and performance).
- **Dashboard components:** **shadcn-svelte on bits-ui** (≥ 2.19.5), copied into `packages/ui`, themed from our tokens.
  Conditions from research: `field` + superforms 3 (not `form`), Sheet instead of Drawer, patch bits-ui ARIA gaps
  (#2134, #2173) in our wrappers, Ark UI per-component only where bits-ui is weak.
- **Candidate + marketing pages:** native HTML controls + **daisyUI 5 as a CSS-only layer**; Svelte islands only where
  behaviour is required (upload, validation).
- **One token source** (`packages/ui/tokens`) generates shadcn CSS variables, daisyUI theme variables and Tailwind 4
  `@theme` — never two hand-maintained vocabularies.
- **Employer branding:** per-tenant `brand` / `on-brand` / `brand-text` / `brand-ui` tokens computed and contrast-checked
  server-side, shipped as a per-tenant stylesheet (never inline styles).

## Consequences
- Consistent look across marketing, careers and dashboard with ~0 KB framework JS on public pages.
- We own component source (bus-factor mitigation) and must maintain our patches.
- Accessibility is our responsibility: axe in CI, manual NVDA/VoiceOver passes, external WCAG 2.2 audit before GA.
