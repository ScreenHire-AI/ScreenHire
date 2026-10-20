import { svelte } from '@sveltejs/vite-plugin-svelte';
import { defineConfig } from 'vitest/config';

// The Svelte plugin compiles the runes in *.svelte.ts modules (the connection-state store) for the tests.
export default defineConfig({
  plugins: [svelte({ compilerOptions: { runes: true } })],
  test: {
    include: ['src/**/*.test.ts'],
  },
});
