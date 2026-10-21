import theme from '@screenhire/ui/theme.css?raw';
import { describe, expect, it } from 'vitest';
import { cn, typeScale } from './utils.js';

describe('cn', () => {
  it('knows every type token in the theme', () => {
    const names = [...theme.matchAll(/--text-([a-z]+(?:-[a-z]+)*):/g)].map((match) => match[1]);
    expect(typeScale.toSorted()).toEqual(names.toSorted());
  });

  it('keeps a type token beside a text colour, and lets a later size win', () => {
    expect(cn('text-caption text-muted-foreground')).toBe('text-caption text-muted-foreground');
    expect(cn('text-body font-medium', 'text-caption')).toBe('font-medium text-caption');
  });
});
