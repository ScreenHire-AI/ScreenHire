import sitemap from '@astrojs/sitemap';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig, envField, fontProviders } from 'astro/config';

// The public base URL is configuration, never hard-coded (ADR-0003): set SITE_URL for production builds.
const site = process.env.SITE_URL ?? 'http://localhost:4321';

// One English page serves both markets until en-GB copy exists, so every hreflang points at the page itself.
// Keep in sync with HREFLANGS in src/lib/seo.ts (this file is plain JS and cannot import it).
const hreflangs = ['en-US', 'en-GB', 'x-default'];

// The Warm Grid fonts (the same self-hosted fontsource files as @screenhire/ui/fonts.css), loaded through Astro's fonts
// API so pages get metric-matched fallbacks (no layout shift when the font swaps in) and a preload. Latin subset only:
// the site is English. src/styles/global.css points the --sh-font-* tokens at these variables.
const font = (name, cssVariable, file, fallback) => ({
  provider: fontProviders.local(),
  name,
  cssVariable,
  fallbacks: fallback,
  options: { variants: [{ src: [`@fontsource-variable/${file}`], weight: '100 900', style: 'normal', display: 'swap' }] },
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
    font('Hanken Grotesk', '--font-hanken', 'hanken-grotesk/files/hanken-grotesk-latin-wght-normal.woff2', ['sans-serif']),
    font('Bricolage Grotesque', '--font-bricolage', 'bricolage-grotesque/files/bricolage-grotesque-latin-wght-normal.woff2', ['sans-serif']),
    font('JetBrains Mono', '--font-jetbrains', 'jetbrains-mono/files/jetbrains-mono-latin-wght-normal.woff2', ['monospace']),
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
