import adapter from '@sveltejs/adapter-static';
import { sveltekit } from '@sveltejs/kit/vite';
import tailwindcss from '@tailwindcss/vite';
import Icons from 'unplugin-icons/vite';
import { defineConfig } from 'vite';
import { fileViewer } from './file-viewer-plugin.ts';

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
    // Lucide icons as Svelte components (`~icons/lucide/<name>`) from @iconify-json/lucide, one JSON file for the whole set.
    // @lucide/svelte ships three files per icon, which was 8,100 of the 44,000 files every worktree's install linked.
    // Size 16 is the default (design system section 14); `data-icon` is the name, for the stroke rule in app.css and for tests.
    Icons({
      compiler: 'svelte',
      iconCustomizer(_collection, icon, props) {
        props.width = '16';
        props.height = '16';
        props['data-icon'] = icon;
      },
    }),
    fileViewer(),
    sveltekit({
      adapter: adapter({ fallback: '200.html' }),
      compilerOptions: { runes: true },
    }),
  ],
  // Vite's dependency scan finds nothing in these routes, so without this list the first page load discovers these
  // packages and bundles them inside the dev server, which keeps the memory (docs/process/dev-tooling.md, "What a preview
  // stack commits"). scripts/prebundle.mjs bundles them in a process that exits instead. A package missing here still
  // works: the page load bundles it in-process and the log says "dependencies optimized".
  optimizeDeps: {
    include: [
      '@screenhire/realtime > @microsoft/signalr',
      '@tanstack/svelte-query',
      '@tanstack/svelte-table',
      '@tanstack/svelte-virtual',
      '@tiptap/core',
      '@tiptap/extension-list',
      '@tiptap/starter-kit',
      '@xyflow/svelte',
      'bits-ui',
      'svelte-dnd-action',
      'sveltekit-superforms',
      'sveltekit-superforms/adapters',
      'tailwind-merge',
      'tailwind-variants',
      'zod',
    ],
  },
  server: { port, strictPort: true, proxy },
  // `vite preview` is for a quick local look at a build. The AppHost's Frontends=Built serves build/ from Caddy instead.
  preview: { port, strictPort: true, proxy },
});
