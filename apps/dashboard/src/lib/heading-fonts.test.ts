import {
  bundledHeadingFont,
  defaultHeadingFont,
  headingFontFamily,
  headingFontIdPattern,
  headingFonts,
  hostedHeadingFontFace,
} from '@screenhire/ui/heading-fonts';
import { describe, expect, it } from 'vitest';

// The heading font helpers the careers stylesheet and the dashboard share (packages/ui/src/heading-fonts.ts, ADR-0031).
// Which six are bundled is the API's to say (HeadingFonts.Bundled; HeadingFontTests checks the catalogue marks them):
// here, that each bundled id is an id the API can save and that nothing but a checked id reaches a family name.
describe('heading fonts', () => {
  it('bundle six fonts, the default among them, each under its own self-hosted family and keyed by a font id', () => {
    const families = [defaultHeadingFont, ...Object.values(headingFonts)].map((font) => font.family.split(',')[0]);
    expect(new Set(families).size).toBe(6);
    expect(families.every((family) => family.endsWith(' Variable"'))).toBe(true);
    expect(Object.keys(headingFonts).every((id) => headingFontIdPattern.test(id))).toBe(true);
  });

  it("draw ScreenHire's font for none, a bundled font's own stack, and a hosted font's face with its fallback", () => {
    expect(headingFontFamily(null)).toBe(defaultHeadingFont.family);
    expect(bundledHeadingFont('playfair-display')?.label).toBe('Playfair Display');
    expect(headingFontFamily({ id: 'fraunces' })).toBe(headingFonts.fraunces.family);
    expect(bundledHeadingFont('roboto-slab')).toBeNull();
    expect(headingFontFamily({ id: 'roboto-slab', category: 'serif' })).toBe('"ScreenHire heading roboto-slab", Georgia, "Times New Roman", serif');
    expect(headingFontFamily({ id: 'work-sans', category: 'sans-serif' })).toMatch(/^"ScreenHire heading work-sans", "Hanken Grotesk Variable"/);
  });

  it('refuse an id that is not of the safe shape before it reaches a face name', () => {
    for (const id of ['Roboto Slab', 'roboto_slab', '-roboto', 'roboto--slab', 'x";}body{', '']) {
      expect(() => hostedHeadingFontFace(id)).toThrow();
    }
  });
});
