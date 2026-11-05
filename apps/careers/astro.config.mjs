import node from '@astrojs/node';
import svelte from '@astrojs/svelte';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'astro/config';

// The hosts Astro believes X-Forwarded-For and the forwarded host from: the careers domain and its tenant subdomains, as
// patterns (`careers.example.org`, `*.careers.example.org`). Behind a proxy or a CDN, without them every candidate shares the
// proxy's address, and the API rate-limits on it. Set at build time (Astro reads it then), so the image build gets it as the
// CAREERS_ALLOWED_DOMAINS build argument (apps/careers/Dockerfile). Empty locally, where nothing is in front.
const allowedDomains = (process.env.CAREERS_ALLOWED_DOMAINS ?? '')
  .split(',')
  .map((hostname) => hostname.trim())
  .filter(Boolean)
  .map((hostname) => ({ hostname, protocol: 'https' }));

// Tenant careers pages render on demand: jobs change constantly and teardown needs an immediate 410 (ADR-0003).
export default defineConfig({
  output: 'server',
  // Its bar sits over the bottom of every page in development, on top of what people are checking.
  devToolbar: { enabled: false },
  adapter: node({ mode: 'standalone' }),
  server: { port: Number(process.env.PORT ?? 4322) },
  // Svelte only for islands that need behaviour HTML cannot give: the direct-to-storage file upload.
  integrations: [svelte()],
  // Stylesheets are files, never inline <style>, so the content security policy needs no style hashes for them.
  build: { inlineStylesheets: 'never' },
  security: {
    allowedDomains,
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
  // Fonts and other assets stay files: an inlined data: URI is something the content security policy refuses.
  vite: { plugins: [tailwindcss()], build: { assetsInlineLimit: 0 } },
});
