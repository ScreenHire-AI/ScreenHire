import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

// The type, shape, spacing, panel, status and cursor rules every public page follows (docs/design/design-system.md
// sections 4.4 and 5, W1, W2, S5 and A10), checked on the markup of the careers pages, the marketing pages and the shared
// candidate question components, so a page cannot bring back capitals, the code font for ordinary words, a pixel text
// size, an arbitrary radius, an off-grid gap, a panel without its shadow, a hand-built status chip or its own cursor.
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

const careersMarkup = markup.filter((file) => file.path.startsWith('apps/careers/') || file.path.startsWith('packages/ui/'));

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

  it('keeps the code font for codes (W2): a reason code and the notice version', () => {
    expect(offenders(/\bfont-mono\b/g).sort()).toEqual([
      'apps/careers/src/pages/[organization]/privacy.astro',
      'apps/marketing/src/components/SampleVerificationCard.astro',
    ]);
  });

  it('sets no pixel text size (W1): the tokens and the scale keep everything at 12px or more', () => {
    expect(offenders(/text-\[\d+(?:\.\d+)?px\]/g)).toEqual([]);
  });

  it('sets careers text on the candidate scale (section 4.3), never Tailwind sizes', () => {
    const found = careersMarkup.filter((file) => /(?<![\w-])text-(?:xs|sm|base|lg|xl|[2-9]xl)(?![\w-])/.test(file.text)).map((file) => file.path);
    expect(found).toEqual([]);
  });

  it('draws shape from the tokens (section 5): no arbitrary radius', () => {
    expect(offenders(/rounded-\[/g)).toEqual([]);
    for (const stylesheet of ['../styles/global.css', '../../../marketing/src/styles/global.css', '../../../../packages/ui/src/questions.css']) {
      expect(readFileSync(fileURLToPath(new URL(stylesheet, import.meta.url)), 'utf8')).not.toMatch(/rounded-\[/);
    }
  });

  it('keeps margins, padding and gaps on the 4px grid (section 5): no half steps, 28px or 36px', () => {
    const offGrid = /(?<![\w-])(?:[a-z0-9-]+:)*-?(?:p|px|py|pt|pb|pl|pr|ps|pe|m|mx|my|mt|mb|ml|mr|ms|me|gap|gap-x|gap-y|space-x|space-y)-(?:0\.5|1\.5|2\.5|3\.5|7|9|11)(?![\w.])/g;
    expect(offenders(offGrid)).toEqual([]);
  });

  it('lifts every panel off the page with the soft surface shadow (S5)', () => {
    // A string of classes that draws a radius-12 surface on the card colour must carry the shadow. It reads one quoted
    // string at a time, so a panel's classes stay together in one.
    const quoted = /(['"`])((?:(?!\1)[^\n])*rounded-panel(?:(?!\1)[^\n])*)\1/g;
    const bare = markup
      .filter((file) =>
        [...file.text.matchAll(quoted)].some((match) => /bg-(?:card|candidate-surface)/.test(match[2]!) && !match[2]!.includes('shadow-surface')),
      )
      .map((file) => file.path);
    expect(bare).toEqual([]);
  });

  it('gives a panel title the heading font, and keeps the privacy notice neutral (section 18)', () => {
    const source = (name: string) => markup.find((file) => file.path === `apps/careers/src/components/${name}.astro`)!.text;
    expect(source('Panel')).toContain("!neutral && 'font-display'");
    expect(source('PrivacyNotice')).toMatch(/<Panel[^>]*\sneutral\b/);
    expect(markup.filter((file) => /<Panel[^>]*\sneutral\b/.test(file.text)).map((file) => file.path)).toEqual(['apps/careers/src/components/PrivacyNotice.astro']);
  });

  it('draws a status only through the shared badge (T1): no hand-built status chip', () => {
    expect(offenders(/bg-status-/g)).toEqual(['apps/marketing/src/components/StatusBadge.astro']);
  });

  it('leaves the root font size to the browser (design system section 4), so the rem tokens mean what they say', () => {
    for (const stylesheet of ['../styles/global.css', '../../../marketing/src/styles/global.css']) {
      const css = readFileSync(fileURLToPath(new URL(stylesheet, import.meta.url)), 'utf8');
      expect(css).not.toMatch(/(?:^|[\s,])(?:html|:root)\s*\{[^}]*font-size/);
    }
  });

  it('sizes text in the stylesheets from the scale (section 4.3): no fixed rem or pixel font size', () => {
    for (const stylesheet of ['../styles/global.css', '../../../marketing/src/styles/global.css', '../../../../packages/ui/src/questions.css']) {
      expect(readFileSync(fileURLToPath(new URL(stylesheet, import.meta.url)), 'utf8')).not.toMatch(/font-size:s*[d.]+(?:rem|px)/);
    }
  });

  it('leaves the cursor to the theme (A10): no cursor class on a component', () => {
    expect(offenders(/\bcursor-(?:pointer|default|not-allowed)\b/g)).toEqual([]);
  });
});
