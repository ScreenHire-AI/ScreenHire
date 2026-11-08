import { defineCollection } from 'astro:content';
import { glob } from 'astro/loaders';
import { z } from 'astro/zod';

// Calendar dates stay ISO strings (`YYYY-MM-DD`): they have no time zone, and a Date would shift by the build
// machine's offset. src/lib/seo.ts formats them for display.
const isoDate = z.iso.date();

const source = z.object({ label: z.string(), url: z.url() });
const faq = z.object({ question: z.string(), answer: z.string() });

// Hiring-AI law tracker entries (/hiring-ai-laws/<id>/). Facts come from docs/research/legal.md and its sources.
const laws = defineCollection({
  loader: glob({ pattern: '**/*.md', base: './src/content/laws' }),
  schema: z.object({
    title: z.string().max(60),
    description: z.string().max(160),
    /** Short name for cards and the home page chips, e.g. "NYC LL144". */
    name: z.string(),
    jurisdiction: z.string(),
    /** One line on where things stand, e.g. "In force since 1 January 2026". */
    status: z.string(),
    /** The date this entry first went up; a re-review changes lastReviewed, never this. */
    published: isoDate,
    /** The date this entry was last checked against its sources. */
    lastReviewed: isoDate,
    keyDates: z.array(z.object({ date: z.string(), event: z.string() })).default([]),
    sources: z.array(source).min(1),
    faqs: z.array(faq).default([]),
    order: z.number(),
  }),
});

const blog = defineCollection({
  loader: glob({ pattern: '**/*.md', base: './src/content/blog' }),
  schema: z.object({
    title: z.string().max(60),
    description: z.string().max(160),
    published: isoDate,
    updated: isoDate.optional(),
    draft: z.boolean().default(false),
  }),
});

// Comparison pages (/compare/<id>/) are data rendered by one template, so "X alternative" pages stay consistent. Every
// claim about the competitor is a `claim`: its words, the numbers of the `sources` it rests on, and the date those pages
// were read. A claim without a source or a date does not build.
const claim = z.object({
  text: z.string(),
  /** 1-based positions in the page's `sources` list. */
  sources: z.array(z.number().int().positive()).min(1),
  /** The date the competitor's own pages were read for this claim. */
  asOf: isoDate,
});

const comparisons = defineCollection({
  loader: glob({ pattern: '**/*.yaml', base: './src/content/comparisons' }),
  schema: z.object({
    competitor: z.string(),
    title: z.string().max(60),
    description: z.string().max(160),
    heading: z.string(),
    intro: z.string(),
    /** What the competitor leaves open, led by one of the five selling points (lib/features.ts). */
    leftOpen: z
      .array(
        z.object({
          point: z.enum(['real-people', 'never-guessing', 'automate-anything', 'integrations', 'built-your-way']),
          theirs: claim,
          /** What ScreenHire adds to the selling point's promise on this topic. */
          ours: z.string(),
        }),
      )
      .min(1),
    /** The other topics, side by side. */
    rows: z.array(z.object({ topic: z.string(), theirs: claim, ours: z.string() })).min(1),
    whenCompetitorFits: z.array(z.string()).min(1),
    whenScreenHireFits: z.array(z.string()).min(1),
    sources: z.array(source).min(1),
    faqs: z.array(faq).default([]),
  }),
});

export const collections = { laws, blog, comparisons };
