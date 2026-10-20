import type { APIRoute } from 'astro';
import { absoluteUrl } from '../lib/seo';
import { siteIndex, type IndexedPage } from '../lib/site-index';

// llms.txt (https://llmstxt.org) for AI assistants: what ScreenHire is, its principles, and a link to every page.
// Generated from the site index at build time, so it lists new pages without a hand-edited file.
const intro = `# ScreenHire

> ScreenHire is an applicant tracking system (ATS) for small hiring teams. It detects bots, impersonators and
> AI-generated or copy-paste job applications, and explains every flag in plain language with a suggested next step.
> Integrity signals never reject a candidate; a person decides.

ScreenHire is in development. This file and the site describe the product as designed, not as generally available.

## What ScreenHire does

- Applicant tracking with per-job hiring workflows built from reusable, versioned templates.
- Applicant integrity checks at the application stage, in two separate assessments: authenticity (is this a real,
  unique person?) and AI assistance (how much looks generated?). Each is a level with reason codes, evidence,
  counter-evidence and a suggested next step, never a numeric score.
- One-click posting to a careers page, Google for Jobs and job boards, and one-click removal with per-board status.
- A public API for integrations.

## Principles

- Evidence, not verdicts: a person decides on every integrity flag, and integrity signals never reject anyone.
- Candidates are told what is checked before any application data or device checks are collected, and can switch
  off typing signals if they use assistive technology, with no negative inference.
- No emotion, intent or personality inference, no biometrics in this release, and no scraping of LinkedIn or other
  sites.
`;

export const GET: APIRoute = async ({ site }) => {
  if (!site) {
    throw new Error('llms.txt needs the site URL (SITE_URL, astro.config.mjs).');
  }

  const entries = await siteIndex();
  const sections = new Map<IndexedPage['section'], IndexedPage[]>();
  for (const entry of entries) {
    sections.set(entry.section, [...(sections.get(entry.section) ?? []), entry]);
  }

  const lists = [...sections].map(
    ([section, items]) =>
      `## ${section}\n\n${items.map((item) => `- [${item.title}](${absoluteUrl(item.path, site)}): ${item.description}`).join('\n')}\n`,
  );

  return new Response([intro, ...lists].join('\n'), { headers: { 'Content-Type': 'text/plain; charset=utf-8' } });
};
