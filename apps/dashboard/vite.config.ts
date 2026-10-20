import adapter from '@sveltejs/adapter-static';
import { sveltekit } from '@sveltejs/kit/vite';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vite';

// SvelteKit 3: project config lives here (svelte.config.js is no longer supported).
// Aspire sets PORT and API_URL in development; the /v1 proxy keeps the API same-origin for cookie auth.
const apiUrl = process.env.API_URL ?? 'http://localhost:5080';
const port = Number(process.env.PORT ?? 4323);
// xfwd: the API rate-limits sign-in per client address, which it reads from X-Forwarded-For.
// ws: the real-time hub (/v1/hubs/events) upgrades to a WebSocket through the same proxy.
const proxy = { '/v1': { target: apiUrl, changeOrigin: true, xfwd: true, ws: true } };

export default defineConfig({
  plugins: [
    tailwindcss(),
    sveltekit({
      adapter: adapter({ fallback: '200.html' }),
      compilerOptions: { runes: true },
    }),
  ],
  server: { port, strictPort: true, proxy },
  // `vite preview` serves the production build (AppHost Frontends=Built, used by the end-to-end tests).
  preview: { port, strictPort: true, proxy },
});
