import { readFileSync, readdirSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

// Guards for the product non-negotiables in marketing copy (CLAUDE.md 1 and 2, ADR-0004): no number presented as a
// fraud or AI verdict, no claim that rejection never happens at all (knockout questions can reject; verification signals
// never do), and no pricing placeholder anywhere (an unpriced plan shows its name and limit and no amount).
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
  // The copy rules in docs/design/marketing-site.md: the word AI appears only where it names a feature, and none of these.
  ['a claim that the AI costs nothing extra', /nothing is metered/i],
  ['an AI fluff word', /\bAI-powered\b|\bsupercharg\w*|\brevolutioni[sz]\w*|\bseamless\w*|\bunlock\w*|\beffortless\w*|\bnext-generation\b/i],
];

describe('marketing copy', () => {
  it.each(forbidden)('never shows %s', (_name, pattern) => {
    const hits = copy.filter((file) => pattern.test(file.text)).map((file) => file.path);
    expect(hits).toEqual([]);
  });

  it('never shows a [YOUR ...] placeholder, in pricing or anywhere else', () => {
    const hits = copy.filter((file) => /\[YOUR [A-Z]+\]/.test(file.text)).map((file) => file.path);
    expect(hits).toEqual([]);
  });

  it('reads the pages and content it guards', () => {
    expect(copy.map((file) => file.path)).toEqual(expect.arrayContaining(['pages/index.astro', 'content/blog/why-no-fraud-score.md']));
  });
});

describe('typography', () => {
  it('renders Markdown pages with straight quotes, as the hand-written pages have (writing-style rule)', async () => {
    // The site's own Markdown processor from astro.config.mjs, so a misnamed option or a dropped processor fails here.
    const { default: config } = await import('../../astro.config.mjs');
    const renderer = await config.markdown!.processor!.createRenderer({ syntaxHighlight: false });
    const { code } = await renderer.render('"Real people," she said. It\'s the team\'s call.');
    expect(code).toContain('"Real people," she said. It\'s the team\'s call.');
    expect(code).not.toMatch(/[‘’“”]|&[lr][sd]quo;/);
  });

  it('keeps the paragraph measure at 60ch, and long-form text and the law pages\' column on that token', () => {
    const styles = readFileSync(join(src, 'styles/global.css'), 'utf8');
    expect(styles).toMatch(/--container-measure:\s*60ch/);
    expect(styles).toMatch(/\.prose\s*\{[^}]*max-width:\s*var\(--container-measure\)/);
    const law = readFileSync(join(src, 'pages/hiring-ai-laws/[slug].astro'), 'utf8');
    expect(law).toContain('minmax(0,var(--container-measure))');
  });
});
