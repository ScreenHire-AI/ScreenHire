import type { APIRoute } from 'astro';
import { absoluteUrl } from '../lib/seo';
import { siteIndex, type IndexedPage } from '../lib/site-index';

// llms.txt (https://llmstxt.org) for AI assistants: what ScreenHire is, its principles, and a link to every page.
// Generated from the site index at build time, so it lists new pages without a hand-edited file.
const intro = `# ScreenHire

> ScreenHire is an applicant tracking system (ATS) for small hiring teams. It checks every application for things that
> do not add up, including bots, impersonators and signs of AI help, and explains what it finds in plain language with
> a suggested next step.
> A check never rejects a candidate; a person decides.

ScreenHire is in development. This file and the site describe the product as designed, not as generally available.

## What ScreenHire does

- Applicant tracking with per-job hiring workflows built from reusable, versioned templates.
- Applicant verification at the application stage, in two separate assessments: authenticity (is this a real,
  unique person?) and AI assistance (are there signs of AI help?). Each is a level with its evidence, counter-evidence
  and a suggested next step, never a numeric score.
- A pipeline that shows where each candidate is and who spoke to them last, with two-way email kept on the application,
  notes, tasks, reports, and a page where each candidate sees their own steps and messages.
- Automations on every stage and on a whole workflow, tested on a sample application first, and interview scheduling
  against members' calendars.
- One-click posting to a careers page, Google for Jobs and job boards, and one-click removal with per-board status.
- A public API and webhooks, Slack, Google and Outlook calendars, and an MCP server for AI assistants.
- Stages, questionnaires, scorecards, email templates and branding that a team sets up its own way, with roles and
  access to each job.
- Pricing by how many jobs are open at once, with unlimited users and every feature on every plan.

## Principles

- Evidence, not verdicts: a person decides on every flag, and a check never rejects anyone.
- Candidates are told what is collected before they start an application, and can switch a setting off if they use
  assistive technology, with no negative inference.
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
