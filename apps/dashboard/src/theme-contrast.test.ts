import { describe, expect, it } from 'vitest';
import css from '@screenhire/ui/tokens.css?raw';

// The colours axe cannot judge, from the tokens, in both themes (design system 3.1 to 3.5). axe's colour-contrast rule reads
// text only, so the end-to-end walk of every page in the dark theme (DashboardThemeTests) does not see a focus ring, a status
// icon or a chart bar. WCAG 2.2 AA asks 4.5:1 for text and 3:1 for the parts of a control or graphic that identify it
// (1.4.11). Change a colour in packages/ui/src/tokens.css and this fails before a page does.
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

const themes = [
  ['light', light],
  ['dark', dark],
] as const;

describe('the tokens file', () => {
  it('has both themes', () => {
    expect(light['background']).toMatch(/^#/);
    expect(dark['background']).toMatch(/^#/);
    expect(dark['background']).not.toBe(light['background']);
  });
});

describe.each(themes)('the %s theme', (_name, tokens) => {
  const grounds = ['background', 'card', 'popover', 'sidebar'] as const;

  it.each(grounds)('keeps the focus ring visible at 3:1 on %s', (ground) => {
    expect(ratio(tokens['ring']!, tokens[ground]!), `ring on ${ground}`).toBeGreaterThanOrEqual(3);
    expect(ratio(tokens['sidebar-ring']!, tokens[ground]!), `sidebar-ring on ${ground}`).toBeGreaterThanOrEqual(3);
  });

  it.each(['background', 'card', 'secondary', 'muted'] as const)('reads body and muted text at 4.5:1 on %s', (ground) => {
    expect(ratio(tokens['foreground']!, tokens[ground]!), 'foreground').toBeGreaterThanOrEqual(4.5);
    expect(ratio(tokens['muted-foreground']!, tokens[ground]!), 'muted-foreground').toBeGreaterThanOrEqual(4.5);
  });

  it('reads the primary colour as text and as a button at 4.5:1', () => {
    expect(ratio(tokens['primary']!, tokens['card']!), 'primary on card').toBeGreaterThanOrEqual(4.5);
    expect(ratio(tokens['primary']!, tokens['background']!), 'primary on background').toBeGreaterThanOrEqual(4.5);
    expect(ratio(tokens['primary-foreground']!, tokens['primary']!), 'text on primary').toBeGreaterThanOrEqual(4.5);
    expect(ratio(tokens['accent-foreground']!, tokens['accent']!), 'text on accent').toBeGreaterThanOrEqual(4.5);
    expect(ratio(tokens['destructive']!, tokens['card']!), 'destructive on card').toBeGreaterThanOrEqual(4.5);
    expect(ratio(tokens['sidebar-foreground']!, tokens['sidebar']!), 'sidebar text').toBeGreaterThanOrEqual(4.5);
  });

  describe.each(['clear', 'review', 'verify', 'ai'])('status %s', (status) => {
    const [label, soft, mark] = [`status-${status}`, `status-${status}-soft`, `status-${status}-mark`];

    it('reads its label on its fill at 4.5:1 and its icon at 3:1', () => {
      expect(ratio(tokens[label]!, tokens[soft]!), 'label on fill').toBeGreaterThanOrEqual(4.5);
      expect(ratio(tokens[mark]!, tokens[soft]!), 'icon on fill').toBeGreaterThanOrEqual(3);
    });

    it('keeps its bare icon visible at 3:1 and its text readable at 4.5:1 on the card', () => {
      expect(ratio(tokens[mark]!, tokens['card']!), 'icon on card').toBeGreaterThanOrEqual(3);
      expect(ratio(tokens[label]!, tokens['card']!), 'label on card').toBeGreaterThanOrEqual(4.5);
    });
  });

  it('keeps the one-series chart colour visible at 3:1 on the card', () => {
    // `chart-2` is the whole the part belongs to and is under 3:1 by design: every bar writes its value (section 3.5).
    expect(ratio(tokens['chart-1']!, tokens['card']!)).toBeGreaterThanOrEqual(3);
  });
});

describe('the candidate pages in the dark theme', () => {
  // The tokens exist for the apply flow and the candidate's own page (3.4); the careers pages draw them only in the light
  // theme today (docs/status.md), so this pins the dark values for the day they do.
  it.each([
    ['candidate-foreground', 'candidate-background', 4.5],
    ['candidate-foreground', 'candidate-surface', 4.5],
    ['candidate-muted-foreground', 'candidate-background', 4.5],
    ['candidate-muted-foreground', 'candidate-surface', 4.5],
    ['candidate-chip-foreground', 'candidate-chip', 4.5],
    ['candidate-cta-foreground', 'candidate-cta', 4.5],
    ['candidate-brand-text', 'candidate-background', 4.5],
    ['candidate-brand-ui', 'candidate-background', 3],
  ] as const)('%s on %s is at least %s:1', (foreground, background, minimum) => {
    expect(ratio(dark[foreground]!, dark[background]!)).toBeGreaterThanOrEqual(minimum);
  });
});
