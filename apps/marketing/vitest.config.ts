/// <reference types="vitest/config" />
import { getViteConfig } from 'astro/config';

// getViteConfig runs the tests through Astro's own Vite setup, so a test can render a component with Astro's container
// API and `astro:env` and `astro:assets` resolve. The build scripts' own tests (scripts/*.test.mjs) run here too.
export default getViteConfig({
  test: { include: ['src/**/*.test.ts', 'scripts/**/*.test.mjs'] },
});
