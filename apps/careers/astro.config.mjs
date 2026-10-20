import node from '@astrojs/node';
import svelte from '@astrojs/svelte';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'astro/config';

// Tenant careers pages render on demand: jobs change constantly and teardown needs an immediate 410 (ADR-0003).
export default defineConfig({
  output: 'server',
  adapter: node({ mode: 'standalone' }),
  server: { port: Number(process.env.PORT ?? 4322) },
  // Svelte only for islands that need behaviour HTML cannot give: the direct-to-storage file upload.
  integrations: [svelte()],
  // Stylesheets are files, never inline <style>, so the content security policy needs no style hashes for them.
  build: { inlineStylesheets: 'never' },
  security: {
    // A 10 MB resume posted without JavaScript, plus the rest of the form.
    actionBodySizeLimit: 12 * 1024 * 1024,
    // Strict policy: Astro hashes the scripts and styles it emits; everything else is listed here. connect-src is set
    // per page (the apply page adds the upload origin), because a directive given twice keeps only its first value.
    csp: {
      directives: [
        "default-src 'none'",
        "img-src 'self'",
        "font-src 'self'",
        "form-action 'self'",
        "base-uri 'none'",
        "object-src 'none'",
      ],
    },
  },
  // No Markdown code blocks here; Shiki's inline styles would also break the content security policy.
  markdown: { syntaxHighlight: false },
  vite: { plugins: [tailwindcss()] },
});
