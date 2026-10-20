import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { JS_BUDGET_BYTES, checkDist, fileForPath, internalReferences, pageScriptBytes } from './check-dist.mjs';

const origin = 'https://marketing.example.com';
const page = (head, body = '') => `<!doctype html><html><head>${head}</head><body>${body}</body></html>`;
const jsonLd = '<script type="application/ld+json">{"@context":"https://schema.org"}</script>';

describe('pageScriptBytes', () => {
  it('does not count JSON-LD as JavaScript', () => {
    expect(pageScriptBytes(page(jsonLd), () => null)).toBe(0);
  });

  it('counts inline scripts and same-site script files', () => {
    const html = page(`<script>console.log(1)</script><script type="module" src="/_astro/a.js"></script>`);
    const bytes = pageScriptBytes(html, (src) => (src === '/_astro/a.js' ? Buffer.from('export const a = 1;') : null));
    expect(bytes).toBeGreaterThan(0);
  });
});

describe('internalReferences', () => {
  it('collects same-site links and the og:image, ignoring other hosts and fragments', () => {
    const html = page(
      `<meta property="og:image" content="${origin}/og/pricing.png"><link rel="canonical" href="${origin}/pricing/">`,
      '<a href="/product/#integrity">x</a><a href="https://example.org/">y</a><a href="#main">z</a><a href="//cdn.example.net/x">w</a>',
    );
    expect(internalReferences(html, origin).sort()).toEqual(['/og/pricing.png', '/product/']);
  });
});

describe('fileForPath', () => {
  it('maps a directory path to its index.html', () => {
    expect(fileForPath('/dist', '/pricing/')).toBe(join('/dist', 'pricing', 'index.html'));
    expect(fileForPath('/dist', '/rss.xml')).toBe(join('/dist', 'rss.xml'));
  });
});

describe('checkDist', () => {
  let dist;
  afterEach(() => rmSync(dist, { recursive: true, force: true }));

  function build(files) {
    dist = mkdtempSync(join(tmpdir(), 'check-dist-'));
    for (const [path, content] of Object.entries(files)) {
      mkdirSync(join(dist, path, '..'), { recursive: true });
      writeFileSync(join(dist, path), content);
    }
  }

  it('passes a site with JSON-LD only and working links', () => {
    build({ 'index.html': page(jsonLd, '<a href="/pricing/">Pricing</a>'), 'pricing/index.html': page(jsonLd) });
    expect(checkDist(dist, origin)).toEqual({ pages: 2, failures: [] });
  });

  it('reports a broken internal link', () => {
    build({ 'index.html': page(jsonLd, '<a href="/missing/">Missing</a>') });
    expect(checkDist(dist, origin).failures).toEqual(['/index.html: links to /missing/, which the build did not produce']);
  });

  it('reports a page over the JavaScript budget', () => {
    // Random bytes do not compress, so the brotli size stays above the budget.
    const script = Buffer.from(Array.from({ length: JS_BUDGET_BYTES * 2 }, () => Math.floor(Math.random() * 256))).toString('base64');
    build({ 'index.html': page(`<script>${script}</script>`) });
    expect(checkDist(dist, origin).failures).toEqual([expect.stringMatching(/^\/index\.html: \d+ bytes of brotli JavaScript/)]);
  });
});
