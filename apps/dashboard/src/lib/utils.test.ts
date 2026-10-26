import theme from '@screenhire/ui/theme.css?raw';
import { describe, expect, it } from 'vitest';
import { badgeVariants } from './components/ui/badge/index.js';
import { buttonVariants } from './components/ui/button/index.js';
import { cn, shadows, typeScale } from './utils.js';

describe('cn', () => {
  it('knows every type token in the theme', () => {
    const names = [...theme.matchAll(/--text-([a-z]+(?:-[a-z]+)*):/g)].map((match) => match[1]);
    expect(typeScale.toSorted()).toEqual(names.toSorted());
  });

  it('knows the section 6 shadows in the theme, and lets one replace a stock shadow', () => {
    const names = [...theme.matchAll(/--shadow-([a-z]+):/g)].map((match) => match[1]);
    expect(shadows.toSorted()).toEqual(names.toSorted());
    expect(cn('shadow-md', 'shadow-overlay')).toBe('shadow-overlay');
  });

  it('keeps size and colour through the component variants, which merge with tv()', () => {
    const smallButton = buttonVariants({ size: 'sm' }).split(' ');
    expect(smallButton).toEqual(expect.arrayContaining(['text-caption', 'text-primary-foreground']));
    expect(badgeVariants({ variant: 'review' }).split(' ')).toEqual(expect.arrayContaining(['text-caption', 'text-status-review']));
  });

  it('keeps a type token beside a text colour, and lets a later size win', () => {
    expect(cn('text-caption text-muted-foreground')).toBe('text-caption text-muted-foreground');
    expect(cn('text-body font-medium', 'text-caption')).toBe('font-medium text-caption');
  });
});
