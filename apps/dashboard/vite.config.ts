import adapter from '@sveltejs/adapter-static';
import { sveltekit } from '@sveltejs/kit/vite';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vite';

// SvelteKit 3: project config lives here (svelte.config.js is no longer supported).
// Aspire sets PORT and API_URL in development; the /v1 proxy keeps the API same-origin for cookie auth.
const apiUrl = process.env.API_URL ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [
    tailwindcss(),
    sveltekit({
      adapter: adapter({ fallback: '200.html' }),
      compilerOptions: { runes: true },
    }),
  ],
  server: {
    port: Number(process.env.PORT ?? 4323),
    strictPort: true,
    proxy: { '/v1': { target: apiUrl, changeOrigin: true } },
  },
});
