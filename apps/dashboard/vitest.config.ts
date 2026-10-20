import { svelteTesting } from '@testing-library/svelte/vite';
import { defineConfig, mergeConfig } from 'vitest/config';
import viteConfig from './vite.config';

// Dates render in the local timezone; pin it so date assertions hold on any machine.
process.env.TZ = 'UTC';

// Component tests run on jsdom; svelteTesting() resolves Svelte's browser build and unmounts after each test.
export default mergeConfig(
  viteConfig,
  defineConfig({
    plugins: [svelteTesting()],
    test: {
      environment: 'jsdom',
      include: ['src/**/*.test.ts'],
      setupFiles: ['./vitest.setup.ts'],
    },
  }),
);
