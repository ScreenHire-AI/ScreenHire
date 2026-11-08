import { readFileSync, readdirSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

// The public site says that checks happen and what they look at in plain words, never how they work (owner's decision,
// docs/design/marketing-site.md, "What is never said"). This guards the pages and content restyled so far: collection content, the
// compare, law and blog pages, the 404 page and the market facts. Product, how-detection-works and home are guarded as their
// tasks restyle them.
const src = fileURLToPath(new URL('..', import.meta.url));

function files(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return files(path);
    return /\.(astro|md|yaml)$/.test(entry.name) ? [path] : [];
  });
}

const guarded = [
  ...files(join(src, 'content')),
  ...files(join(src, 'pages/compare')),
  ...files(join(src, 'pages/hiring-ai-laws')),
  ...files(join(src, 'pages/blog')),
  join(src, 'pages/404.astro'),
  join(src, 'lib/market.ts'),
].map((path) => ({ path: relative(src, path).split('\\').join('/'), text: readFileSync(path, 'utf8') }));

// Words that describe how a check works: a reason code or rule name, a threshold, or the kind of network, phone line, email
// address or answer a check looks at. "rule" stays out of the list: the law pages use it for statutes.
const how: [string, RegExp][] = [
  ['a reason code', /reason codes?/i],
  ['a hosting provider, data centre, proxy, VPN or VoIP line', /hosting[- ]provider|data ?cent(?:er|re)|\bvpn\b|\bvoip\b|proxy (?:server|network|service)|residential proxy|mobile line/i],
  ['a disposable or new email address', /disposable|email domain|domain age|email age/i],
  ['a count of matching answers', /near-identical|matches? four|matching answers/i],
  ['signal categories or a threshold', /independent categories|at least two|threshold|ruleset|versioned rules/i],
  ['a signal named as a signal', /(?:typing|paste|network|identity|behaviour|content) signals?/i],
];

describe('public copy', () => {
  it('reads the files it guards', () => {
    expect(guarded.map((file) => file.path)).toEqual(
      expect.arrayContaining(['content/blog/why-no-fraud-score.md', 'content/laws/eu-ai-act.md', 'pages/404.astro', 'pages/compare/[slug].astro']),
    );
  });

  it.each(how)('never shows %s', (_name, pattern) => {
    expect(guarded.filter((file) => pattern.test(file.text)).map((file) => file.path)).toEqual([]);
  });
});
