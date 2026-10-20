import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

// Build-time helpers for the Open Graph images (astro-og-canvas renders them with CanvasKit, no browser). The build
// runs with apps/marketing as the working directory, so paths resolve from there.

export type Rgb = [number, number, number];

const uiPackage = resolve('node_modules/@screenhire/ui');

/** Reads a colour from the Warm Grid tokens, so the images use the same values as the pages and no hex is copied. */
export function tokenColor(css: string, name: string): Rgb {
  const match = new RegExp(`--${name}:\\s*#([0-9a-f]{6})\\b`, 'i').exec(css);
  if (!match?.[1]) {
    throw new Error(`Token --${name} is missing or not a 6-digit hex colour in @screenhire/ui tokens.css.`);
  }

  const hex = match[1];
  return [0, 2, 4].map((offset) => Number.parseInt(hex.slice(offset, offset + 2), 16)) as Rgb;
}

export function readTokens(): string {
  return readFileSync(resolve(uiPackage, 'src/tokens.css'), 'utf8');
}

/** The same self-hosted woff2 files the pages use. CanvasKit reads woff2, so no font is fetched from the network. */
export function fontFiles(): string[] {
  const files = [
    '@fontsource-variable/bricolage-grotesque/files/bricolage-grotesque-latin-wght-normal.woff2',
    '@fontsource-variable/hanken-grotesk/files/hanken-grotesk-latin-wght-normal.woff2',
  ].map((file) => resolve('node_modules', file));

  const missing = files.filter((file) => !existsSync(file));
  if (missing.length > 0) {
    throw new Error(`Open Graph fonts not found: ${missing.join(', ')}. Run pnpm install at the repository root.`);
  }

  return files;
}
