// Pre-bundles the dev server's dependencies in a process that exits, so the long-lived `vite dev` starts on a warm cache.
//
// Vite bundles dependencies in the process that runs the dev server, and that process keeps the memory: the dashboard's
// held about 1.2 GB of commit on a cold cache against about 0.35 GB on a warm one (docs/process/dev-tooling.md,
// "What a preview stack commits"). Windows low-memory pauses read commit, not working set.
//
// Why not `vite optimize`: it calls optimizeDeps alone, but vite-plugin-svelte decides in its `buildStart` hook, which
// only a dev server runs, whether Svelte's settings changed, and it forces a second, in-process optimization when the
// metadata file it writes there is missing. A server that never listens runs the same hooks, so the cache it leaves is
// the one the real server accepts.
//
// Exit code 0 once the cache is committed. A failure exits 1 and the dev server bundles in-process, as it did before.
import { createServer } from 'vite';

const server = await createServer({
  appType: 'custom',
  // Middleware mode opens no port for the app. The HMR socket still needs a port, so let the OS pick a free one.
  server: { middlewareMode: true, ws: { port: 0 } },
  logLevel: 'warn',
});

try {
  await Promise.all(
    Object.values(server.environments).map(async (environment) => {
      const optimizer = environment.depsOptimizer;
      if (!optimizer) return;
      await optimizer.scanProcessing;
    }),
  );
} finally {
  await server.close();
}
