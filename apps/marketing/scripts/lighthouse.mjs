// Lighthouse performance budgets for the marketing site (ADR-0003): mobile LCP under 1.8 s, CLS under 0.05 and at
// most 15 KB of JavaScript per page. Runs headless against the production build served by Astro's own preview server.
//
//   pnpm --filter @screenhire/marketing build
//   pnpm --filter @screenhire/marketing lighthouse              # every page in the sitemap
//   pnpm --filter @screenhire/marketing lighthouse / /pricing/  # just these paths
//
// Why not Lighthouse CI (@lhci/cli): it serves dist with its own static server and takes its URL list from config.
// This script uses Astro's own preview server (what the end-to-end tests serve) and reads the built sitemap, so every
// new page is measured without a list to keep in step, and it adds no second copy of Lighthouse to the tree.
//
// Browser: CHROME_PATH if set; otherwise the Chromium that `pnpm e2e:install` puts in Playwright's cache (so the gate
// needs nothing the end-to-end tests do not already need); otherwise an installed Chrome found by chrome-launcher.
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { createServer } from 'node:net';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { preview } from 'astro';
import * as chromeLauncher from 'chrome-launcher';
import lighthouse from 'lighthouse';

const BUDGETS = { lcpMs: 1800, cls: 0.05, scriptBytes: 15 * 1024 };

const root = fileURLToPath(new URL('..', import.meta.url));
const dist = fileURLToPath(new URL('../dist', import.meta.url));

function freePort() {
  return new Promise((resolvePort, reject) => {
    const server = createServer();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolvePort(port));
    });
  });
}

/** Paths from the built sitemap, so every indexable page is measured. */
function sitemapPaths() {
  const xml = readFileSync(`${dist}/sitemap-0.xml`, 'utf8');
  return [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map(([, loc]) => new URL(loc).pathname);
}

/** The newest full Chromium in Playwright's browser cache (same locations as scripts/install-browsers.mjs). */
function playwrightChromium() {
  const cache =
    process.env.PLAYWRIGHT_BROWSERS_PATH ??
    (process.platform === 'win32'
      ? join(process.env.LOCALAPPDATA ?? join(homedir(), 'AppData', 'Local'), 'ms-playwright')
      : process.platform === 'darwin'
        ? join(homedir(), 'Library', 'Caches', 'ms-playwright')
        : join(homedir(), '.cache', 'ms-playwright'));
  if (!existsSync(cache)) {
    return undefined;
  }

  const executables = [
    ['chrome-win64', 'chrome.exe'],
    ['chrome-win', 'chrome.exe'],
    ['chrome-linux64', 'chrome'],
    ['chrome-linux', 'chrome'],
    ['chrome-mac-arm64', 'Google Chrome for Testing.app', 'Contents', 'MacOS', 'Google Chrome for Testing'],
    ['chrome-mac', 'Google Chrome for Testing.app', 'Contents', 'MacOS', 'Google Chrome for Testing'],
    ['chrome-mac', 'Chromium.app', 'Contents', 'MacOS', 'Chromium'],
  ];
  const revisions = readdirSync(cache)
    .filter((name) => /^chromium-\d+$/.test(name))
    .sort((a, b) => Number(b.slice(9)) - Number(a.slice(9)));
  for (const revision of revisions) {
    const executable = executables.map((parts) => join(cache, revision, ...parts)).find((path) => existsSync(path));
    if (executable) {
      return executable;
    }
  }

  return undefined;
}

function scriptTransferBytes(lhr) {
  const requests = lhr.audits['network-requests']?.details?.items ?? [];
  return requests.filter((request) => request.resourceType === 'Script').reduce((sum, request) => sum + (request.transferSize ?? 0), 0);
}

if (!existsSync(`${dist}/sitemap-0.xml`)) {
  console.error('lighthouse: no build output. Run `pnpm --filter @screenhire/marketing build` first.');
  process.exit(1);
}

const paths = process.argv.slice(2).length > 0 ? process.argv.slice(2) : sitemapPaths();
const invalid = paths.filter((path) => !path.startsWith('/'));
if (invalid.length > 0) {
  // Git Bash rewrites "/pricing/" into a Windows path; MSYS_NO_PATHCONV=1 stops it.
  console.error(`lighthouse: paths must start with "/": ${invalid.join(', ')}. In Git Bash, set MSYS_NO_PATHCONV=1.`);
  process.exit(1);
}
const port = await freePort();
const server = await preview({ root, logLevel: 'error', server: { host: '127.0.0.1', port } });
let chrome;
const failures = [];
try {
  const chromePath = process.env.CHROME_PATH ?? playwrightChromium();
  chrome = await chromeLauncher.launch({ chromeFlags: ['--headless=new', '--no-first-run'], chromePath });
  console.log(`lighthouse: ${paths.length} page(s), mobile, ${chromePath ?? 'installed Chrome'}`);
  for (const path of paths) {
    const url = `http://127.0.0.1:${port}${path}`;
    // Lighthouse's defaults: mobile form factor with simulated slow 4G and 4x CPU slowdown.
    const result = await lighthouse(url, { port: chrome.port, output: 'json', logLevel: 'error', onlyCategories: ['performance'] });
    if (!result) {
      failures.push(`${path}: Lighthouse returned no result`);
      continue;
    }

    const { lhr } = result;
    if (lhr.runtimeError) {
      failures.push(`${path}: ${lhr.runtimeError.message}`);
      continue;
    }

    const lcp = lhr.audits['largest-contentful-paint'].numericValue;
    const cls = lhr.audits['cumulative-layout-shift'].numericValue;
    const js = scriptTransferBytes(lhr);
    const score = Math.round((lhr.categories.performance.score ?? 0) * 100);
    console.log(`${path.padEnd(48)} score ${score}  LCP ${Math.round(lcp)} ms  CLS ${cls.toFixed(3)}  JS ${js} B`);

    if (lcp >= BUDGETS.lcpMs) failures.push(`${path}: LCP ${Math.round(lcp)} ms (budget < ${BUDGETS.lcpMs} ms)`);
    if (cls >= BUDGETS.cls) failures.push(`${path}: CLS ${cls.toFixed(3)} (budget < ${BUDGETS.cls})`);
    if (js > BUDGETS.scriptBytes) failures.push(`${path}: ${js} bytes of JavaScript (budget ${BUDGETS.scriptBytes})`);
  }
} finally {
  chrome?.kill();
  await server.stop();
}

if (failures.length > 0) {
  console.error(`lighthouse: ${failures.length} budget failure(s):\n${failures.map((failure) => `  - ${failure}`).join('\n')}`);
  process.exit(1);
}

console.log(`lighthouse: ${paths.length} page(s) within budget.`);
