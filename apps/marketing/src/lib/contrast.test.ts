import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

// The contrast table in docs/design/design-system.md section 22, computed from the tokens. Text needs 4.5:1 and the parts
// of a control that identify it need 3:1 (WCAG 2.2 AA). Change a colour in packages/ui/src/tokens.css and this fails
// before a page does.
const css = readFileSync(fileURLToPath(new URL('../../../../packages/ui/src/tokens.css', import.meta.url)), 'utf8');

function block(opening: RegExp): Record<string, string> {
  const start = css.search(opening);
  const body = css.slice(start, css.indexOf('\n}', start));
  return Object.fromEntries([...body.matchAll(/--([a-z0-9-]+):\s*(#[0-9a-fA-F]{6})/g)].map((match) => [match[1]!, match[2]!]));
}

const light = block(/^:root,\s*\n\.light \{/m);
const dark = { ...light, ...block(/^\.dark \{/m) };

function luminance(hex: string): number {
  const channel = (offset: number) => {
    const value = parseInt(hex.slice(offset, offset + 2), 16) / 255;
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);
}

function ratio(a: string, b: string): number {
  const [high, low] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (high! + 0.05) / (low! + 0.05);
}

describe('the tokens file', () => {
  it('is read', () => {
    expect(light['foreground']).toMatch(/^#/);
    expect(dark['foreground']).not.toBe(light['foreground']);
    expect(light['page']).toBe('#ffffff');
  });
});

const bands = [
  ['the white page', light, 'page'],
  ['the warm band', light, 'background'],
  ['the dark band', dark, 'page'],
] as const;

describe.each(bands)('%s', (_name, tokens, ground) => {
  const background = tokens[ground]!;
  it.each(['foreground', 'secondary-foreground', 'muted-foreground', 'accent-foreground'])('reads %s at 4.5:1', (token) => {
    expect(ratio(tokens[token]!, background), token).toBeGreaterThanOrEqual(4.5);
  });
  it('reads the link colour of the band at 4.5:1', () => {
    // Links are the brand colour on light bands. On the dark band they are the foreground, underlined.
    const link = tokens === dark ? tokens['foreground']! : tokens['primary']!;
    expect(ratio(link, background)).toBeGreaterThanOrEqual(4.5);
  });
  it('keeps the focus ring and the edge of an outline button visible at 3:1', () => {
    expect(ratio(tokens['ring']!, background)).toBeGreaterThanOrEqual(3);
    expect(ratio(tokens['muted-foreground']!, background)).toBeGreaterThanOrEqual(3);
  });
  it('reads the solid call to action, background on foreground, at 4.5:1', () => {
    expect(ratio(tokens['background']!, tokens['foreground']!)).toBeGreaterThanOrEqual(4.5);
  });
});

describe('status chips', () => {
  // Chips sit on the product frame, which is always the light theme (`.light`), in any band.
  it.each(['clear', 'review', 'verify', 'ai'])('%s: its label reads on its fill at 4.5:1 and its icon at 3:1', (status) => {
    expect(ratio(light[`status-${status}`]!, light[`status-${status}-soft`]!)).toBeGreaterThanOrEqual(4.5);
    expect(ratio(light[`status-${status}-mark`]!, light[`status-${status}-soft`]!)).toBeGreaterThanOrEqual(3);
  });
});
