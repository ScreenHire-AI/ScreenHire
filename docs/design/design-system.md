# ScreenHire Design System — "Warm Grid"

> Status: **v0.1 (2026-10-18)** · Decision: ADR-0009 · Reference mockups: "ScreenHire Look & Feel" canvas, flavor **D**
> (dark theme derived from flavor **G**).
>
> Brief: **inviting, but techy/fast and easy to scan.** Recruiters get a precise, dense, keyboard-first tool on warm
> paper; candidates get a warm, honest, low-friction page.

## 1. Principles

1. **Scan first.** Every screen answers "what needs me?" in one glance: counts, a stacked integrity bar, status chips
   with shape + colour + label, mono metadata.
2. **Warm, not soft.** Paper and ink, generous type, friendly copy — but tight rows, crisp 1px rules, no decorative
   gradients or illustrations in the app.
3. **Evidence, not verdicts** (ADR-0004) is a visual rule: integrity UI shows reason codes, evidence and
   counter-evidence; there is no "reject" control inside integrity panels; AI-assistance is styled as *information*,
   never as a warning.
4. **Keyboard is first-class.** Shortcuts shown in mono next to the things they do (`G J`, `J / K`, `V`, `Ctrl K`).
5. **Fast by construction.** Self-hosted variable fonts, ~0 KB JS on public pages, no layout shift, no spinners where a
   skeleton row will do.
6. **Accessible as drawn.** WCAG 2.2 AA minimum; colour is never the only signal.

## 2. Colour tokens

Semantic names are the API; raw values live only in `packages/ui/tokens`. One source generates shadcn-svelte
variables, daisyUI theme variables and Tailwind 4 `@theme`.

### 2.1 Recruiter app — light (default)

| Token | Value | Use |
|---|---|---|
| `bg` | `#F7F5F0` | App background (paper) |
| `bg-sidebar` | `#FBFAF6` | Sidebar |
| `surface` | `#FFFFFF` | Cards, tables, panels |
| `surface-sunken` | `#FBFAF7` | Evidence lists, table footers, inputs |
| `surface-segment` | `#F4F1EB` | Segmented-control track, inline callouts |
| `border` | `#E6E1D8` | Card and panel borders |
| `border-subtle` | `#EFEBE4` | Row dividers, inner rules |
| `border-strong` | `#DDD7CC` | Buttons, inputs |
| `text` | `#1C1B19` | Primary text (ink) |
| `text-secondary` | `#3A3630` | Secondary text, nav |
| `text-muted` | `#5F5A52` | Metadata, captions (≥ 5.3:1 on `bg`) |
| `primary` | `#4338CA` | Primary actions, links, focus ring |
| `primary-hover` | `#3730A3` | |
| `primary-soft` | `#EEEBFB` | Active nav item background |
| `primary-soft-text` | `#2B2788` | Text on `primary-soft` |
| `row-selected` | `#F3F1FC` | Selected table row (+ `#E4E0F5` divider) |
| `danger-text` / `danger-border` | `#9A2F17` / `#E3B8A8` | Destructive buttons (e.g. "Close & remove everywhere") |

### 2.2 Integrity & status semantics (shape + colour + label, always)

| Meaning | Mark | Text | Soft bg | Mark colour |
|---|---|---|---|---|
| **No concerns** / Live | ● circle | `#0B5D4F` | `#E3F1EC` | `#0F766E` |
| **Review suggested** / Queued | ◆ diamond | `#7A4205` | `#FBEFD3` | `#C77700` |
| **Verification recommended** | ■ square | `#9A2F17` | `#F9E0D6` | `#B4461F` |
| **AI assistance** (informational) | none | `#2B2788` | `#ECEBFA` | — |

Rules: status colours are never used for brand or decoration; a chip always has its mark **and** its words; counts use
tabular figures; the stacked integrity bar is `aria-hidden` and always accompanied by the numeric legend.

### 2.3 Recruiter app — dark

| Token | Value | Token | Value |
|---|---|---|---|
| `bg` | `#141311` | `text` | `#EDEAE4` |
| `bg-sidebar` | `#181714` | `text-secondary` | `#CFCAC1` |
| `surface` | `#1C1B18` | `text-muted` | `#A39E95` |
| `surface-sunken` | `#181714` | `primary` | `#9B9CF8` (text on it: `#15132E`) |
| `row-selected` | `#2A2722` | `primary-soft` | `#26264A` / text `#C7C8FA` |
| `border` | `#2E2C27` | `border-subtle` | `#262420` |
| `border-strong` | `#3A3731` | `danger-text` | `#F7957A` |

Dark status: no concerns `#4FD1B4` on `#15302A`; review `#F6CB76` on `#3A2C10` (mark `#F2B84B`); verify `#F7957A` on
`#3D1A10` (mark `#F27A55`); AI `#C7C8FA` on `#26264A`.

### 2.4 Candidate & marketing — warm

| Token | Light | Dark |
|---|---|---|
| `c-bg` | `#FBF6EE` (cream) | `#1A1714` |
| `c-surface` | `#FFFFFF` | `#23201C` |
| `c-border` / `c-input-border` | `#EDE3D3` / `#D9CCB8` | `#332E28` / `#4A443B` |
| `c-text` / `c-text-muted` | `#231F1A` / `#6A5D4D` | `#F2EDE6` / `#B3ABA0` |
| `c-chip` | `#F3E7D6` (text `#5A4630`) | `#2E2A25` |
| `c-cta` (ScreenHire default) | `#C2462F` coral (white text, 4.98:1) | `#F08A6E` (text `#1A1310`) |

On tenant careers pages, `c-cta` and the header mark are replaced by the **employer brand tokens** (§7).

## 3. Typography

| Role | Family | Use |
|---|---|---|
| Display | **Bricolage Grotesque** (variable, opsz 12–96, 600–800) | Page titles, candidate names in panels, big numbers, marketing headlines |
| UI / body | **Hanken Grotesk** (400–700) | Everything else |
| Data | **JetBrains Mono** (400–600) | Reason codes, metadata strips, shortcuts, IDs, timestamps, table headers |

Scale (dashboard, base 13.5px / 1.45):

| Token | Size / weight | Example |
|---|---|---|
| `display-lg` | 30px / 700, −0.02em | Job title |
| `display-md` | 19–20px / 700 | Panel heading (candidate name) |
| `title` | 14px / 700 | Section headings ("Authenticity") |
| `body` | 13.5px / 400–600 | Table cells, copy |
| `caption` | 12–12.5px / 500 | Secondary lines |
| `mono-label` | 10.5–11.5px / 500, +0.06–0.08em, UPPERCASE | Table headers, kickers, metadata |

Candidate pages: base **15px**, display 30px, inputs 15–16px (avoids iOS zoom). Marketing: display up to 64px.

**Fonts are self-hosted** (`@fontsource-variable/*`, latin + latin-ext subsets), `font-display: swap`, preload only
Hanken Grotesk; Bricolage loaded for display text only.

## 4. Space, shape, elevation, density

- **Spacing:** 4px grid — 2, 4, 6, 8, 10, 12, 14, 16, 20, 24, 28, 32, 40.
- **Radius:** `sm` 6 (small chips, kbd) · `md` 8–9 (buttons, inputs, nav items) · `lg` 12 (cards, panels) ·
  `xl` 14–16 (candidate cards, CTA) · `full` (status pills, avatars).
- **Elevation:** borders over shadows. Shadows only for floating layers: segmented active `0 1px 2px rgb(28 27 25 / .14)`;
  popover/menu `0 8px 24px rgb(28 27 25 / .12)`; dialog `0 16px 48px rgb(28 27 25 / .18)`.
- **Density:** dashboard rows **40px** (8px vertical padding) default; *Comfortable* (48px) user setting. Controls
  32–36px in the app; **≥ 44px** on candidate pages.

## 5. Components (shadcn-svelte mapping)

| Pattern | Component | Notes |
|---|---|---|
| App shell | Sidebar (shadcn block) | Workspace switcher, nav with shortcut hints, saved views with status marks, Signal Network promo card |
| Command bar | Command (bits-ui) | `Ctrl K` / `⌘K`; jump to jobs, candidates, actions |
| Stage filter | Toggle Group styled as segmented control | Counts in mono |
| Candidate table | Data Table (TanStack v9 + Virtual) | 40px rows, mono metadata columns, selected-row tint, J/K navigation, footer of shortcuts |
| Integrity chip | Badge (custom variants) | `clear` / `review` / `verify` / `ai`; mark + label + optional count |
| Evidence list | Card + list | Reason code (mono) above plain-language sentence; counter-evidence row in success colour |
| Distribution strip | Custom | Board · status dot · mono state (`LIVE`, `QUEUED · ~4H`); never claims removal before confirmed |
| Detail panel | Resizable side panel / Sheet on small screens | |
| Forms | `field` + superforms 3 + Zod (generated by Orval) | Not shadcn `form` |
| Toasts | svelte-sonner | Undo for reversible actions |
| Workflow builder | @xyflow/svelte | Nodes styled with the same tokens |

## 6. Interaction, motion, accessibility

- **Focus:** 2px `primary` ring with 2px offset on every interactive element; never removed.
- **Motion:** 120–180ms ease-out for hover/press/popover; 200–240ms for panels; honour `prefers-reduced-motion`.
- **Keyboard:** documented shortcut map; every shortcut has a visible control equivalent.
- **Drag & drop** always has a non-drag alternative ("Move to stage…") — WCAG 2.5.7.
- **Targets:** ≥ 24×24px in the app (2.5.8), ≥ 44px on candidate pages.
- **Contrast:** text ≥ 4.5:1, large text and UI boundaries ≥ 3:1 — enforced by token tests in CI.
- **Testing:** axe in component tests + Playwright; manual NVDA and VoiceOver passes per release; external WCAG 2.2 audit
  before GA.

## 7. Employer branding (careers pages)

Tenants set a brand colour and logo. On save, the server derives and validates:

| Token | Purpose | Minimum |
|---|---|---|
| `brand` | Tenant colour | — |
| `on-brand` | Text on brand fills | 4.5:1 |
| `brand-text` | Brand-coloured text on page background | 4.5:1 |
| `brand-ui` | Borders, focus rings | 3:1 |

Failing colours are auto-adjusted in tone (OKLCH lightness) or rejected with an explanation. Shipped as a hashed
per-tenant stylesheet (strict CSP: no inline styles). Brand never overrides status colours, privacy notice styling or
the "Powered by ScreenHire" footer.

## 8. Voice & copy

- **Recruiters:** precise and neutral. "Review suggested", "Verification recommended", "3 reasons" — never "suspicious",
  "fake", "fraudster" or "AI-generated candidate". Every automated observation says what was observed, not what it means
  about the person. Footer line on integrity panels: *"No automated decision has been made. Every action is logged."*
- **Candidates:** warm, plain, honest. Explain what we look at and what we never do, before collection; offer the
  assistive-tech toggle; welcome declared AI use where the employer allows it. "A person reads every application."
- **Marketing:** confident and specific; cite sources for every statistic; no invented numbers.

## 9. Assets & icons

- Icons: **Lucide** (`@lucide/svelte`), 16 / 18 / 20px, 2px stroke; decorative icons `aria-hidden`.
- Logo mark: ink rounded square with "S" (placeholder until brand identity work).
- No stock photography in the product; marketing imagery = real product UI.
