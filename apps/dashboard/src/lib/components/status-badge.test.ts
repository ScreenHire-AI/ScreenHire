import { render } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { Badge } from './ui/badge/index.js';
import StatusBadge from './status-badge.svelte';

const badges = [
  ['done', 'No concerns', 'circle-check', 'bg-status-clear-soft', 'text-status-clear-mark'],
  ['review', 'Review suggested', 'eye', 'bg-status-review-soft', 'text-status-review-mark'],
  ['verification', 'Verification suggested', 'id-card', 'bg-status-verify-soft', 'text-status-verify-mark'],
  ['unsafeFile', 'Unsafe file', 'shield-x', 'bg-status-verify-soft', 'text-status-verify-mark'],
  ['ai', 'Some signs of AI help', 'sparkles', 'bg-status-ai-soft', 'text-status-ai-mark'],
  ['waiting', 'Sent', 'clock-4', 'bg-secondary', 'text-muted-foreground'],
] as const;

describe('status badge', () => {
  it.each(badges)('shows %s with its icon, colour and label (T1)', async (status, label, icon, fill, iconColour) => {
    const { container } = render(StatusBadge, { props: { status, label } });
    const badge = container.querySelector('[data-slot="badge"]')!;

    expect(badge).toHaveTextContent(label);
    expect(badge.querySelector('[data-status-icon]')).toHaveAttribute('data-status-icon', icon);
    expect(badge.querySelector('[data-status-icon]')).toHaveClass(iconColour);
    expect(badge).toHaveClass(fill);
    await expectNoAxeViolations(container);
  });

  it('draws no shape: the icon is the mark', () => {
    const { container } = render(StatusBadge, { props: { status: 'review', label: 'Review suggested' } });

    expect(container.querySelector('[data-shape]')).toBeNull();
    expect(container.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
  });

  it('keeps a calm state quiet in a table: the icon and muted text, no fill (T3)', async () => {
    const { container } = render(StatusBadge, { props: { status: 'done', label: 'No concerns', quiet: true } });
    const badge = container.querySelector('[data-slot="badge"]')!;

    expect(badge.querySelector('[data-status-icon]')).toHaveAttribute('data-status-icon', 'circle-check');
    expect(badge).toHaveClass('text-muted-foreground');
    expect(badge.className).not.toMatch(/\bbg-/);
    await expectNoAxeViolations(container);
  });
});

describe('badge', () => {
  it('is never a link or a button (A5)', () => {
    const { container } = render(Badge, { props: { variant: 'muted', href: '/jobs' } as never });

    expect(container.querySelector('[data-slot="badge"]')!.tagName).toBe('SPAN');
    expect(container.querySelector('a')).toBeNull();
  });

  it('draws a fact pill with the input edge and a count as a wide pill, white on a pressed toggle (T4)', () => {
    const fact = render(Badge, { props: { variant: 'muted' } }).container.querySelector('[data-slot="badge"]');
    const count = render(Badge, { props: { variant: 'count' } }).container.querySelector('[data-slot="badge"]');

    expect(fact).toHaveClass('bg-secondary', 'border-input', 'text-caption', 'rounded-full');
    expect(count).toHaveClass('bg-secondary', 'text-muted-foreground', 'min-w-7', 'tabular-nums', 'rounded-full', 'group-data-[state=on]/toggle:bg-card');
  });
});
