import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

// The type and cursor rules every public page follows (docs/design/design-system.md section 4.3, W1, W2 and A10),
// checked on the markup of the careers pages, the marketing pages and the shared candidate question components, so a
// page cannot bring back capitals, the code font for ordinary words, text under 12px or its own cursor.
const roots = [
  fileURLToPath(new URL('..', import.meta.url)),
  fileURLToPath(new URL('../../../marketing/src', import.meta.url)),
  fileURLToPath(new URL('../../../../packages/ui/src/questions', import.meta.url)),
];

function files(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return files(path);
    return /\.(astro|svelte)$/.test(entry.name) ? [path] : [];
  });
}

const markup = roots.flatMap(files).map((path) => ({ path: relative(join(roots[0]!, '..', '..', '..'), path).replaceAll('\\', '/'), text: readFileSync(path, 'utf8') }));

const offenders = (pattern: RegExp, keep: (match: RegExpMatchArray) => boolean = () => true) =>
  markup.filter((file) => [...file.text.matchAll(pattern)].some(keep)).map((file) => file.path);

describe('public page markup', () => {
  it('reads the pages it guards', () => {
    expect(markup.length).toBeGreaterThan(40);
    expect(markup.map((file) => file.path)).toEqual(
      expect.arrayContaining(['apps/careers/src/layouts/CareersLayout.astro', 'apps/marketing/src/layouts/BaseLayout.astro', 'packages/ui/src/questions/FileField.svelte']),
    );
  });

  it('never sets text in capitals (W2): no uppercase class, no toUpperCase on a label', () => {
    expect(offenders(/\buppercase\b|\.toUpperCase\(\)/g)).toEqual([]);
  });

  it('never sets text under 12px (W1)', () => {
    expect(offenders(/text-\[(\d+(?:\.\d+)?)px\]/g, (match) => Number(match[1]) < 12)).toEqual([]);
  });

  it('leaves the cursor to the theme (A10): no cursor class on a component', () => {
    expect(offenders(/\bcursor-(?:pointer|default|not-allowed)\b/g)).toEqual([]);
  });
});
