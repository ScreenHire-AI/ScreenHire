// The size budget (integrity-engine.md §5): the telemetry module ships at most 5 KB brotli-compressed. Runs after
// `vite build`, so `pnpm -r build` and therefore `pnpm verify` fail when the module outgrows it.
import { readFileSync } from 'node:fs';
import { brotliCompressSync, constants } from 'node:zlib';

const BUDGET_BYTES = 5 * 1024;
const file = new URL('../dist/telemetry.js', import.meta.url);
const source = readFileSync(file);
const compressed = brotliCompressSync(source, { params: { [constants.BROTLI_PARAM_QUALITY]: 11 } }).length;

console.log(`@screenhire/telemetry: ${source.length} bytes, ${compressed} bytes brotli (budget ${BUDGET_BYTES})`);
if (compressed > BUDGET_BYTES) {
  console.error(`@screenhire/telemetry is ${compressed} bytes brotli, over its ${BUDGET_BYTES}-byte budget.`);
  process.exit(1);
}
