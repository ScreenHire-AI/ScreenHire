// Pre-bundles the dev server's dependencies in a process that exits, so the long-lived `vite dev` starts on a warm cache.
//
// Vite bundles dependencies in the process that runs the dev server, and that process keeps the memory
// (docs/process/dev-tooling.md, "What a preview stack commits"). Windows low-memory pauses read commit, not working set.
//
// Why not `vite optimize`: it calls optimizeDeps alone, but vite-plugin-svelte decides in its `buildStart` hook, which
// only a dev server runs, whether Svelte's settings changed, and it forces a second, in-process optimization when the
// metadata file it writes there is missing. A server that never listens runs the same hooks, so the cache it leaves is
// the one the real server accepts.
//
// This script always exits 0, because the `dev` script chains it with `&&`: if it fails, it warns and `vite dev` bundles
// in-process, as it did before.
import { createServer } from 'vite';

// createServer returns once the SSR dependencies are written, and the client's are still being bundled. Closing the
// server then discards that run. `processing` is the promise Vite's own transforms wait on, and it settles after the
// files are in the cache folder.
async function dependenciesWritten(environment) {
  const optimizer = environment.depsOptimizer;
  if (!optimizer) return;
  await optimizer.scanProcessing;
  await Promise.all(optimizer.metadata.depInfoList.map((dependency) => dependency.processing));
}

try {
  const server = await createServer({
    appType: 'custom',
    // Middleware mode opens no port for the app, and `ws: false` opens no HMR socket, which would otherwise take the
    // fixed port 24678 and clash with another worktree's pre-bundle.
    server: { middlewareMode: true, ws: false },
    logLevel: 'warn',
  });
  try {
    await Promise.all(Object.values(server.environments).map(dependenciesWritten));
  } finally {
    await server.close();
  }
} catch (error) {
  console.warn(`Pre-bundle skipped, vite dev will bundle in-process: ${error}`);
}
