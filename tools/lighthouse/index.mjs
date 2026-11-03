// The one file apps/marketing/scripts/lighthouse.mjs imports by path. Bare specifiers resolve from the importing file,
// so re-exporting here is how the script reaches this folder's node_modules.
export { default as lighthouse } from 'lighthouse';
export * as chromeLauncher from 'chrome-launcher';
