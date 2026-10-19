import sitemap from '@astrojs/sitemap';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'astro/config';

// The public base URL is configuration, never hard-coded (ADR-0003): set SITE_URL for production builds.
export default defineConfig({
  site: process.env.SITE_URL ?? 'http://localhost:4321',
  integrations: [sitemap()],
  server: { port: Number(process.env.PORT ?? 4321) },
  vite: { plugins: [tailwindcss()] },
});
