import { HeadingFont } from '@screenhire/api-client';
import { defaultHeadingFont, headingFont, headingFonts } from '@screenhire/ui/heading-fonts';
import { describe, expect, it } from 'vitest';

// The fonts ScreenHire hosts (packages/ui/src/heading-fonts.ts) and the values the API accepts (HeadingFont in
// src/ScreenHire.Domain) are two lists of one thing: a font the API saves must have a family, and the picker must offer
// nothing the API refuses.
describe('heading fonts', () => {
  it('are exactly the fonts the API accepts', () => {
    expect(Object.keys(headingFonts).toSorted()).toEqual(Object.values(HeadingFont).filter(Boolean).toSorted());
  });

  it("fall back to ScreenHire's font, and refuse a name not on the list", () => {
    expect(headingFont(null)).toBe(defaultHeadingFont);
    expect(headingFont('PlayfairDisplay').label).toBe('Playfair Display');
    expect(() => headingFont('Comic Sans')).toThrow();
  });

  it('are six fonts, the default among them, each self-hosted under its own family', () => {
    const families = [defaultHeadingFont, ...Object.values(headingFonts)].map((font) => font.family.split(',')[0]);
    expect(new Set(families).size).toBe(6);
    expect(families.every((family) => family.endsWith(' Variable"'))).toBe(true);
  });
});
