import sitemap from '@astrojs/sitemap';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig, envField, fontProviders } from 'astro/config';

// The public base URL is configuration, never hard-coded (ADR-0003): set SITE_URL for production builds.
const site = process.env.SITE_URL ?? 'http://localhost:4321';

// One English page serves both markets until en-GB copy exists, so every hreflang points at the page itself.
// Keep in sync with HREFLANGS in src/lib/seo.ts (this file is plain JS and cannot import it).
const hreflangs = ['en-US', 'en-GB', 'x-default'];

// The Warm Grid fonts (the same self-hosted fontsource files as @screenhire/ui/fonts.css), loaded through Astro's fonts
// API so pages get metric-matched fallbacks (no layout shift when the font swaps in). Latin and latin-ext subsets, as in
// docs/design/design-system.md; the browser downloads latin-ext only when a page uses those characters, and BaseLayout
// preloads the latin body font (the first variant).
// src/styles/global.css points the --sh-font-* tokens at these variables.
const unicodeRanges = {
  latin: [
    'U+0000-00FF', 'U+0131', 'U+0152-0153', 'U+02BB-02BC', 'U+02C6', 'U+02DA', 'U+02DC', 'U+0304', 'U+0308', 'U+0329',
    'U+2000-206F', 'U+20AC', 'U+2122', 'U+2191', 'U+2193', 'U+2212', 'U+2215', 'U+FEFF', 'U+FFFD',
  ],
  'latin-ext': [
    'U+0100-02BA', 'U+02BD-02C5', 'U+02C7-02CC', 'U+02CE-02D7', 'U+02DD-02FF', 'U+0304', 'U+0308', 'U+0329',
    'U+1D00-1DBF', 'U+1E00-1E9F', 'U+1EF2-1EFF', 'U+2020', 'U+20A0-20AB', 'U+20AD-20C0', 'U+2113', 'U+2C60-2C7F',
    'U+A720-A7FF',
  ],
};
const font = (name, cssVariable, family, fallback) => ({
  provider: fontProviders.local(),
  name,
  cssVariable,
  fallbacks: fallback,
  options: {
    variants: Object.entries(unicodeRanges).map(([subset, unicodeRange]) => ({
      src: [`@fontsource-variable/${family}/files/${family}-${subset}-wght-normal.woff2`],
      weight: '100 900',
      style: 'normal',
      display: 'swap',
      unicodeRange,
    })),
  },
});

export default defineConfig({
  site,
  // Directory-style URLs with a trailing slash everywhere, so canonical, og:url, hreflang and the sitemap agree.
  trailingSlash: 'always',
  integrations: [
    sitemap({
      serialize: (item) => ({ ...item, links: hreflangs.map((lang) => ({ lang, url: item.url })) }),
    }),
  ],
  fonts: [
    font('Hanken Grotesk', '--font-hanken', 'hanken-grotesk', ['sans-serif']),
    font('Bricolage Grotesque', '--font-bricolage', 'bricolage-grotesque', ['sans-serif']),
    font('JetBrains Mono', '--font-jetbrains', 'jetbrains-mono', ['monospace']),
  ],
  env: {
    schema: {
      // Cookieless analytics (ADR-0003), off unless the provider and a first-party, site-relative script path are set
      // (src/lib/analytics.ts).
      PUBLIC_ANALYTICS: envField.enum({ context: 'server', access: 'public', values: ['off', 'plausible', 'umami'], default: 'off' }),
      PUBLIC_ANALYTICS_SCRIPT_PATH: envField.string({ context: 'server', access: 'public', optional: true, startsWith: '/' }),
      // Plausible's data-domain (defaults to the SITE_URL host) or Umami's website id.
      PUBLIC_ANALYTICS_SITE_ID: envField.string({ context: 'server', access: 'public', optional: true }),
      // The recruiter dashboard. When set, the header shows "Sign in"; hosts are never hard-coded (ADR-0003).
      PUBLIC_APP_URL: envField.string({ context: 'server', access: 'public', optional: true, url: true }),
    },
  },
  server: { port: Number(process.env.PORT ?? 4321) },
  vite: { plugins: [tailwindcss()] },
});
