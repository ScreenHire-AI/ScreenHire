import { render } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { Badge } from './ui/badge/index.js';
import StatusBadge from './status-badge.svelte';

const statuses = [
  ['clear', 'No concerns', 'circle', 'bg-status-clear-soft'],
  ['review', 'Review suggested', 'diamond', 'bg-status-review-soft'],
  ['verify', 'Verification recommended', 'square', 'bg-status-verify-soft'],
] as const;

describe('status badge', () => {
  it.each(statuses)('shows %s with its mark, colour and label (T1)', async (status, label, shape, fill) => {
    const { container } = render(StatusBadge, { props: { status, label } });
    const badge = container.querySelector('[data-slot="badge"]')!;

    expect(badge).toHaveTextContent(label);
    expect(badge.querySelector('[data-shape]')).toHaveAttribute('data-shape', shape);
    expect(badge).toHaveClass(fill);
    await expectNoAxeViolations(container);
  });

  it('shows AI assistance as information, with no mark', async () => {
    const { container } = render(StatusBadge, { props: { status: 'ai', label: 'Some indicators' } });
    const badge = container.querySelector('[data-slot="badge"]')!;

    expect(badge.querySelector('[data-shape]')).toBeNull();
    expect(badge).toHaveClass('bg-status-ai-soft');
    await expectNoAxeViolations(container);
  });

  it('keeps a calm state quiet in a table: the mark and muted text, no fill (T3)', async () => {
    const { container } = render(StatusBadge, { props: { status: 'clear', label: 'No concerns', quiet: true } });
    const badge = container.querySelector('[data-slot="badge"]')!;

    expect(badge.querySelector('[data-shape]')).toHaveAttribute('data-shape', 'circle');
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

  it('draws a fact pill with the input edge and a count as a wide pill (T4)', () => {
    const fact = render(Badge, { props: { variant: 'muted' } }).container.querySelector('[data-slot="badge"]');
    const count = render(Badge, { props: { variant: 'count' } }).container.querySelector('[data-slot="badge"]');

    expect(fact).toHaveClass('bg-secondary', 'border-input', 'text-caption', 'rounded-full');
    expect(count).toHaveClass('bg-secondary', 'text-muted-foreground', 'min-w-7', 'tabular-nums', 'rounded-full');
  });
});
