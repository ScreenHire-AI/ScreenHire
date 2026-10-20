import { describe, expect, it } from 'vitest';
import { tokenColor } from './og';

const css = ':root {\n  --background: #f7f5f0;\n  --primary: #4338CA;\n  --radius: 0.625rem;\n}';

describe('tokenColor', () => {
  it('reads a token as RGB', () => {
    expect(tokenColor(css, 'background')).toEqual([0xf7, 0xf5, 0xf0]);
    expect(tokenColor(css, 'primary')).toEqual([0x43, 0x38, 0xca]);
  });

  it('fails loudly when a token is missing or not a hex colour', () => {
    expect(() => tokenColor(css, 'foreground')).toThrow(/--foreground/);
    expect(() => tokenColor(css, 'radius')).toThrow(/--radius/);
  });
});
