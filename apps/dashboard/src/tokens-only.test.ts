import { describe, expect, it } from 'vitest';

// Sizes and colours come from the @screenhire/ui tokens (docs/design/design-system.md, checks W1 and section 3). This
// scans every source file in the dashboard for what a search can prove: a pixel font size, a hex colour, a colour
// function, or a Tailwind palette colour.

const hues = 'red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|slate|gray|zinc|neutral|stone';
const colourUtilities = 'bg|text|border(?:-[trblxyse])?|ring|ring-offset|outline|fill|stroke|from|via|to|decoration|divide|placeholder|caret|accent|shadow';

const rules = [
  { rule: 'pixel font size', pattern: /\btext-\[(?:length:)?[\d.]+px\]|font-size\s*[:=]\s*["']?[\d.]+px/g },
  { rule: 'hex colour', pattern: /(?<![\w&])#(?:[\da-f]{8}|[\da-f]{6}|[\da-f]{3,4})(?![\w-])/gi },
  { rule: 'colour function', pattern: /\b(?:rgba?|hsla?|hwb|lab|lch|oklab|oklch)\(/g },
  { rule: 'palette colour', pattern: new RegExp(`\\b(?:${colourUtilities})-(?:(?:${hues})-\\d{2,3}|black|white)\\b`, 'g') },
];

function violations(source: string): string[] {
  return source.split('\n').flatMap((line, index) =>
    rules.flatMap(({ rule, pattern }) => [...line.matchAll(pattern)].map((match) => `${index + 1}: ${rule} "${match[0]}"`)),
  );
}

describe('the tokens-only check', () => {
  it.each([
    ['<p class="text-[11px] text-muted-foreground">', 'pixel font size "text-[11px]"'],
    ['<p class="text-[12.5px]">', 'pixel font size "text-[12.5px]"'],
    ['  html { font-size: 13.5px; }', 'pixel font size "font-size: 13.5px"'],
    ['<span style:font-size="12px">', 'pixel font size "font-size="12px"'],
    ['<div style="color: #1c1b19">', 'hex colour "#1c1b19"'],
    ["const ink = '#FFF';", 'hex colour "#FFF"'],
    ['box-shadow: 0 4px 12px rgb(28 27 25 / .14);', 'colour function "rgb("'],
    ['<p class="text-red-600">', 'palette colour "text-red-600"'],
    ['<div class="hover:bg-slate-100">', 'palette colour "bg-slate-100"'],
    ['<div class="bg-black/10">', 'palette colour "bg-black"'],
  ])('flags %s', (line, found) => {
    expect(violations(line)).toEqual([`1: ${found}`]);
  });

  it.each([
    '<p class="text-caption text-muted-foreground">',
    '<h1 class="font-display text-page-title">',
    "import Panel from '#lib/components/panel.svelte';",
    '<a href="#add-question">',
    '&#123;',
    'shadow-[0_1px_2px_color-mix(in_oklab,var(--foreground)_14%,transparent)]',
    '<div class="bg-scrim/10 size-[26px]">',
  ])('allows %s', (line) => {
    expect(violations(line)).toEqual([]);
  });
});

describe('dashboard source', () => {
  const sources = import.meta.glob<string>(['./**/*.{svelte,ts,css}', '!./**/*.test.ts'], { query: '?raw', import: 'default', eager: true });

  it('reads the source files', () => {
    expect(Object.keys(sources)).toContain('./app.css');
    expect(Object.keys(sources)).toContain('./lib/components/ui/button/button.svelte');
    expect(Object.entries(sources).filter(([, source]) => source.trim() === '').map(([file]) => file)).toEqual([]);
  });

  it('takes sizes and colours from the tokens only', () => {
    const found = Object.entries(sources).flatMap(([file, source]) => violations(source).map((violation) => `${file}:${violation}`));
    expect(found).toEqual([]);
  });
});
