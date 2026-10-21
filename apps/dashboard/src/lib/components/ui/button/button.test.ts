import { describe, expect, it } from 'vitest';
import { buttonVariants } from './index.js';

describe('button', () => {
  it.each(['default', 'xs', 'sm', 'lg'] as const)('gives the %s size a 44px target on touch (F4)', (size) => {
    expect(buttonVariants({ size }).split(' ')).toContain('pointer-coarse:h-11');
  });

  it.each(['icon', 'icon-xs', 'icon-sm', 'icon-lg'] as const)('gives the %s size a 44px square on touch (F4)', (size) => {
    expect(buttonVariants({ size }).split(' ')).toContain('pointer-coarse:size-11');
  });
});
