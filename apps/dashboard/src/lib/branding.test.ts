import { describe, expect, it } from 'vitest';
import { brandColorPattern, normalizeBrandColor } from './branding.js';

describe('brandColorPattern', () => {
  it.each(['#1f4e5a', '1f4e5a', '#1F4E5A', 'ABCDEF', '#000000'])('accepts %s', (text) => {
    expect(brandColorPattern.test(text)).toBe(true);
  });

  it.each([
    ['a three-digit colour', '#fff'],
    ['a non-hex digit', '#1f4e5g'],
    ['seven digits', '#1f4e5a0'],
    ['two hashes', '##1f4e5a'],
    ['a colour name', 'teal'],
    ['nothing', ''],
    ['surrounding spaces', ' #1f4e5a '],
  ])('rejects %s', (_name, text) => {
    expect(brandColorPattern.test(text)).toBe(false);
  });
});

describe('normalizeBrandColor', () => {
  it('adds the hash a person left off', () => {
    expect(normalizeBrandColor('1f4e5a')).toBe('#1f4e5a');
  });

  it('keeps one hash when there is one', () => {
    expect(normalizeBrandColor('#1f4e5a')).toBe('#1f4e5a');
  });

  it('lower-cases the digits', () => {
    expect(normalizeBrandColor('#1F4E5A')).toBe('#1f4e5a');
  });

  it('trims what surrounds the colour', () => {
    expect(normalizeBrandColor('  1F4E5A ')).toBe('#1f4e5a');
  });
});
