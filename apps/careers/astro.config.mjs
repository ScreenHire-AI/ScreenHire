import node from '@astrojs/node';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'astro/config';

// Tenant careers pages render on demand: jobs change constantly and teardown needs an immediate 410 (ADR-0003).
export default defineConfig({
  output: 'server',
  adapter: node({ mode: 'standalone' }),
  server: { port: Number(process.env.PORT ?? 4322) },
  vite: { plugins: [tailwindcss()] },
});
