import { readFileSync, readdirSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

// Guards for the product non-negotiables in marketing copy (CLAUDE.md 1 and 2, ADR-0004): no number presented as a
// fraud or AI verdict, no claim that rejection never happens at all (knockout questions can reject; verification signals
// never do), and no pricing placeholder outside the pricing data.
const src = fileURLToPath(new URL('..', import.meta.url));

function files(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      return files(path);
    }

    return /\.(astro|md|yaml|ts)$/.test(entry.name) && !entry.name.endsWith('.test.ts') ? [path] : [];
  });
}

const copy = files(src).map((path) => ({ path: relative(src, path).replaceAll('\\', '/'), text: readFileSync(path, 'utf8') }));

const forbidden: [string, RegExp][] = [
  ['a percentage of AI-written text', /\d+\s*%\s*(of\s+\w+\s+)?AI/i],
  ['a numeric fraud score', /\b(fraud|risk)\s*(score|risk)?\s*[:=]\s*\d/i],
  ['an unscoped "never rejected automatically" claim', /nothing is rejected automatically|never rejects candidates automatically/i],
];

describe('marketing copy', () => {
  it.each(forbidden)('never shows %s', (_name, pattern) => {
    const hits = copy.filter((file) => pattern.test(file.text)).map((file) => file.path);
    expect(hits).toEqual([]);
  });

  it('keeps [YOUR ...] placeholders in the pricing data only', () => {
    const hits = copy.filter((file) => /\[YOUR [A-Z]+\]/.test(file.text) && file.path !== 'lib/pricing.ts').map((file) => file.path);
    expect(hits).toEqual([]);
  });

  it('reads the pages and content it guards', () => {
    expect(copy.map((file) => file.path)).toEqual(expect.arrayContaining(['pages/index.astro', 'content/blog/why-no-fraud-score.md']));
  });
});
