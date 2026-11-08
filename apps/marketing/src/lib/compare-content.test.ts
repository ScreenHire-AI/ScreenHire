import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

// Every claim about a competitor on a compare page keeps its source and the day its pages were read (docs/design/marketing-site.md:
// no claim about a competitor without a source). content.config.ts makes both required when the site builds; this reads the YAML
// as text, so the numbers a claim cites must also exist in the page's source list.
const directory = fileURLToPath(new URL('../content/comparisons/', import.meta.url));
const files = readdirSync(directory).filter((name) => name.endsWith('.yaml'));

/** The indented block under each `theirs:` key. */
function claimsIn(text: string): string[] {
  const lines = text.split(/\r?\n/);
  const claims: string[] = [];
  lines.forEach((line, index) => {
    if (!/^\s+theirs:\s*$/.test(line)) return;
    const indent = /^\s*/.exec(line)![0].length;
    const block: string[] = [];
    for (const next of lines.slice(index + 1)) {
      if (next.trim() !== '' && /^\s*/.exec(next)![0].length <= indent) break;
      block.push(next);
    }
    claims.push(block.join('\n'));
  });
  return claims;
}

function sourceCount(text: string): number {
  const lines = text.split(/\r?\n/);
  const start = lines.findIndex((line) => /^sources:\s*$/.test(line));
  if (start < 0) return 0;
  let count = 0;
  for (const line of lines.slice(start + 1)) {
    if (/^\S/.test(line)) break;
    if (/^\s+- label:/.test(line)) count += 1;
  }
  return count;
}

describe('comparison content', () => {
  it('finds the comparisons it guards', () => {
    expect(files).toContain('gohire-alternative.yaml');
  });

  it.each(files)('%s: every claim has sources that exist and a date', (name) => {
    const text = readFileSync(`${directory}${name}`, 'utf8');
    const claims = claimsIn(text);
    const sources = sourceCount(text);
    expect(claims.length).toBeGreaterThan(0);
    expect(sources).toBeGreaterThan(0);
    for (const claim of claims) {
      expect(claim, claim).toMatch(/^\s+asOf: '\d{4}-\d{2}-\d{2}'$/m);
      const refs = /^\s+sources: \[([\d, ]+)\]$/m.exec(claim);
      expect(refs, claim).not.toBeNull();
      for (const ref of refs![1]!.split(',').map((n) => Number(n.trim()))) {
        expect(ref, claim).toBeGreaterThanOrEqual(1);
        expect(ref, claim).toBeLessThanOrEqual(sources);
      }
    }
  });

  it.each(files)('%s: makes no claim the research could not confirm', (name) => {
    const text = readFileSync(`${directory}${name}`, 'utf8');
    // docs/design/marketing-site.md, "What is never said": GoHire's event triggers, its API and Pro-only stage actions.
    expect(text).not.toMatch(/no event triggers|no public api|none documented|pro-only/i);
  });
});
