import { defineConfig } from 'orval';

// The API build writes openapi.json next to this file (ScreenHire.Api.csproj: OpenApiDocumentsDirectory).
// Regenerate with `pnpm generate:api` from the repo root whenever an endpoint or contract changes.
export default defineConfig({
  screenhire: {
    input: { target: './openapi.json' },
    output: {
      target: './src/generated/client.ts',
      schemas: './src/generated/model',
      client: 'fetch',
      mode: 'single',
      override: {
        mutator: { path: './src/http.ts', name: 'apiFetch' },
      },
    },
  },
  // TanStack Query (svelte-query v6, options as accessors) for the dashboard: query keys split by path segment
  // (`['v1', 'jobs', jobId]`), so invalidating `['v1', 'jobs']` covers every query under it, and query functions that
  // throw on a status outside 2xx, so failed queries reach the error state. The careers app keeps the fetch client.
  screenhireSvelteQuery: {
    input: { target: './openapi.json' },
    output: {
      target: './src/generated/svelte-query.ts',
      schemas: './src/generated/model',
      client: 'svelte-query',
      httpClient: 'fetch',
      mode: 'single',
      override: {
        mutator: { path: './src/http.ts', name: 'apiQueryFetch' },
        fetch: { forceSuccessResponse: true },
        query: { shouldSplitQueryKey: true },
      },
    },
  },
  screenhireZod: {
    input: { target: './openapi.json' },
    output: {
      target: './src/generated/zod.ts',
      client: 'zod',
      mode: 'single',
    },
  },
});
