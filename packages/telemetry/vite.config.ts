import { defineConfig } from 'vitest/config';

// The build is only for the size budget (scripts/check-size.mjs): the careers app imports the source and bundles it
// itself. One minified ES module, as a browser would receive it.
export default defineConfig({
  build: {
    lib: { entry: 'src/index.ts', formats: ['es'], fileName: 'telemetry' },
    outDir: 'dist',
    emptyOutDir: true,
    minify: true,
  },
  test: {
    include: ['src/**/*.test.ts'],
    environment: 'jsdom',
  },
});
