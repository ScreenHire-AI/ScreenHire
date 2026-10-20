// Post-build checks on apps/marketing/dist, run by `pnpm build` so `pnpm verify` enforces them (ADR-0003):
//  - JavaScript budget: at most 15 KB of brotli-compressed JS per page, counting script files and inline scripts.
//    JSON-LD is data, not JavaScript, and does not count.
//  - Internal links: every same-site href and src, and every og:image, points at a file the build produced.
// Deterministic and fast (no browser); the Lighthouse run in scripts/lighthouse.mjs covers LCP and CLS.
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { brotliCompressSync } from 'node:zlib';

export const JS_BUDGET_BYTES = 15 * 1024;

const scriptPattern = /<script\b([^>]*)>([\s\S]*?)<\/script>/gi;
const attributePattern = (name) => new RegExp(`\\b${name}\\s*=\\s*"([^"]*)"`, 'i');

/** Brotli size of the JavaScript a page loads. `readScript(src)` returns a same-site script's bytes, or null. */
export function pageScriptBytes(html, readScript) {
  let total = 0;
  for (const [, attributes, body] of html.matchAll(scriptPattern)) {
    const type = attributePattern('type').exec(attributes)?.[1]?.toLowerCase();
    if (type === 'application/ld+json') {
      continue;
    }

    const src = attributePattern('src').exec(attributes)?.[1];
    const bytes = src === undefined ? Buffer.from(body) : readScript(src);
    // A third-party script (the opt-in analytics) cannot be measured offline; Lighthouse measures it.
    if (bytes !== null) {
      total += brotliCompressSync(bytes).length;
    }
  }

  return total;
}

/** Same-site URLs a page refers to: links, sources and its Open Graph image (made relative to the site root). */
export function internalReferences(html, siteOrigin) {
  const references = new Set();
  for (const [, url] of html.matchAll(/\b(?:href|src)\s*=\s*"([^"#?]*)[^"]*"/gi)) {
    if (url.startsWith('/') && !url.startsWith('//')) {
      references.add(url);
    }
  }

  const ogImage = /<meta property="og:image" content="([^"]+)"/i.exec(html)?.[1];
  if (ogImage?.startsWith(siteOrigin)) {
    references.add(new URL(ogImage).pathname);
  }

  return [...references];
}

/** The file a static host serves for a site path: `/pricing/` is `pricing/index.html`. */
export function fileForPath(distDir, path) {
  const decoded = decodeURIComponent(path);
  return decoded.endsWith('/') ? join(distDir, decoded, 'index.html') : join(distDir, decoded);
}

function htmlFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      return htmlFiles(path);
    }

    return entry.name.endsWith('.html') ? [path] : [];
  });
}

export function checkDist(distDir, siteOrigin) {
  const failures = [];
  const pages = htmlFiles(distDir);
  for (const file of pages) {
    const page = `/${relative(distDir, file).split(sep).join('/')}`;
    const html = readFileSync(file, 'utf8');

    const jsBytes = pageScriptBytes(html, (src) => {
      if (!src.startsWith('/') || src.startsWith('//')) {
        return null;
      }

      const path = fileForPath(distDir, src);
      return existsSync(path) ? readFileSync(path) : null;
    });
    if (jsBytes > JS_BUDGET_BYTES) {
      failures.push(`${page}: ${jsBytes} bytes of brotli JavaScript, over the ${JS_BUDGET_BYTES}-byte budget`);
    }

    for (const reference of internalReferences(html, siteOrigin)) {
      if (!existsSync(fileForPath(distDir, reference))) {
        failures.push(`${page}: links to ${reference}, which the build did not produce`);
      }
    }
  }

  return { pages: pages.length, failures };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const distDir = fileURLToPath(new URL('../dist', import.meta.url));
  const siteOrigin = new URL(process.env.SITE_URL ?? 'http://localhost:4321').origin;
  const { pages, failures } = checkDist(distDir, siteOrigin);
  if (failures.length > 0) {
    console.error(`check-dist: ${failures.length} problem(s) in ${pages} pages:\n${failures.map((f) => `  - ${f}`).join('\n')}`);
    process.exit(1);
  }

  console.log(`check-dist: ${pages} pages within the ${JS_BUDGET_BYTES / 1024} KB JavaScript budget, no broken internal links.`);
}
