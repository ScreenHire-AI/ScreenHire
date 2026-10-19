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
  screenhireZod: {
    input: { target: './openapi.json' },
    output: {
      target: './src/generated/zod.ts',
      client: 'zod',
      mode: 'single',
    },
  },
});
