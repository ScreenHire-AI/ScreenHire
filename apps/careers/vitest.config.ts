/// <reference types="vitest/config" />
import { getViteConfig } from 'astro/config';

// Astro's Vite config, so tests can render components with the container API (astro:container) and the Svelte renderer.
export default getViteConfig({
  test: {
    include: ['src/**/*.test.ts'],
  },
});
