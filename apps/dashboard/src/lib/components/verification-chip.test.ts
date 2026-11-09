import { render } from '@testing-library/svelte';
import type { Component } from 'svelte';
import { describe, expect, it } from 'vitest';
import { pendingState, verificationStateOf } from '#lib/verification.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import AiMark from './ai-mark.svelte';
import LevelMark from './level-mark.svelte';
import VerificationChip from './verification-chip.svelte';

// A chip with a tooltip needs the provider the root layout gives it.
const show = (component: Component<any>, props: Record<string, unknown>) => render(WithTooltips, { props: { component, props } });

// One word, the level mark or the state's icon, and the colour, every time (design system T1, section 3.2): Review and Verify
// are a filled badge with the three-step mark, and Clear, Pending and Dismissed are quiet, an icon and a muted word.
function parts(container: HTMLElement) {
  const badge = container.querySelector('[data-slot="badge"]')!;
  return {
    badge,
    icon: badge.querySelector('[data-status-icon]')?.getAttribute('data-status-icon'),
    mark: badge.querySelector('[data-level-mark]')?.getAttribute('data-level-mark'),
    words: badge.querySelector('span[aria-hidden="true"]:not([data-level-mark])')?.textContent,
    spoken: badge.querySelector('.sr-only')?.textContent,
  };
}

describe('verification chip', () => {
  it.each([
    ['ReviewSuggested', 'Review', '2', 'bg-status-review-soft', 'Review suggested'],
    ['VerificationRecommended', 'Verify', '3', 'bg-status-verify-soft', 'Verification suggested'],
  ] as const)('shows %s as the word %s with the level mark, filled to its level (T1, T3)', (level, word, steps, colour, label) => {
    const { container } = show(VerificationChip, { state: verificationStateOf(level, false) });
    const chip = parts(container);

    expect(chip.words).toBe(word);
    expect(chip.mark).toBe(steps);
    expect(chip.badge).toHaveClass(colour);
    // The mark is the icon: no second one beside it.
    expect(chip.icon).toBeUndefined();
    expect(chip.spoken).toBe(`Verification: ${label}`);
  });

  it('tells Review from Verify by the count of filled steps, not by colour alone', () => {
    for (const [level, filled] of [['ReviewSuggested', 2], ['VerificationRecommended', 3]] as const) {
      const { container, unmount } = show(VerificationChip, { state: verificationStateOf(level, false) });
      expect(container.querySelectorAll('[data-level-mark] > span.bg-current')).toHaveLength(filled);
      expect(container.querySelectorAll('[data-level-mark] > span')).toHaveLength(3);
      unmount();
    }
  });

  it.each([
    ['clear', verificationStateOf('NoConcerns', false), 'circle-check', 'Clear', 'Verification: No concerns'],
    ['pending', pendingState, 'loader-circle', 'Pending', 'Verification: Checks pending'],
    ['dismissed', verificationStateOf('VerificationRecommended', true), 'circle-minus', 'Dismissed', 'Verification: Verification suggested, dismissed after review'],
  ] as const)('keeps %s quiet: its icon and a muted word, no fill and no mark (T3)', (_, state, icon, word, spoken) => {
    const { container } = show(VerificationChip, { state });
    const chip = parts(container);

    expect(chip.badge).toHaveClass('text-muted-foreground');
    expect(chip.badge.className).not.toMatch(/bg-status/);
    expect(chip.icon).toBe(icon);
    expect(chip.words).toBe(word);
    expect(chip.mark).toBeUndefined();
    expect(chip.spoken).toBe(spoken);
    expect(container.querySelector('[data-quiet]')).not.toBeNull();
  });

  it('says the number of reasons only to screen readers (W3, W6)', () => {
    const { container } = show(VerificationChip, { state: verificationStateOf('ReviewSuggested', false), reasons: 3 });
    const chip = parts(container);

    expect(chip.spoken).toBe('Verification: Review suggested, 3 reasons');
    expect(container.querySelector('[data-reasons]')).toBeNull();
    expect(container).not.toHaveTextContent('·');
    expect(container.querySelectorAll('[data-slot="badge"]')).toHaveLength(1);
  });

  it('keeps the long name out of the visible words', () => {
    const { container } = show(VerificationChip, { state: verificationStateOf('VerificationRecommended', false) });

    expect(parts(container).words).toBe('Verify');
    expect(container.querySelector('[aria-hidden="true"]:not([data-level-mark])')).toHaveTextContent(/^Verify$/);
  });

  it('marks the chip with its state for the page and the tests', () => {
    const { container } = show(VerificationChip, { state: pendingState });

    expect(container.querySelector('[data-verification-chip]')).toHaveAttribute('data-level', 'pending');
  });

  it('never shows a number that grades the candidate', () => {
    for (const level of ['NoConcerns', 'ReviewSuggested', 'VerificationRecommended'] as const) {
      const { container, unmount } = show(VerificationChip, { state: verificationStateOf(level, false), size: 'lg' });
      expect(container.textContent).not.toMatch(/%|score/i);
      unmount();
    }
  });
});

describe('level mark', () => {
  it('draws three squares, filled to the level, hidden from assistive technology', () => {
    const { container } = render(LevelMark, { props: { steps: 2, tone: 'review' } });
    const mark = container.querySelector('[data-level-mark]')!;

    expect(mark).toHaveAttribute('aria-hidden', 'true');
    expect(mark.children).toHaveLength(3);
    expect([...mark.children].map((square) => square.classList.contains('bg-current'))).toEqual([true, true, false]);
    expect(mark).toHaveClass('text-status-review-mark');
  });

  it('is larger on the candidate page', () => {
    const { container } = render(LevelMark, { props: { steps: 3, tone: 'verify', size: 'lg' } });

    expect(container.querySelector('[data-level-mark] > span')).toHaveClass('size-2');
    expect(container.querySelector('[data-level-mark]')).toHaveClass('text-status-verify-mark');
  });
});

describe('AI mark', () => {
  it.each(['SomeIndicators', 'StrongIndicators'] as const)('shows %s as a bare sparkles mark outside a badge, in the information colour', (level) => {
    const { container } = show(AiMark, { level });

    expect(container.querySelector('[data-ai-mark]')).not.toBeNull();
    expect(container.querySelector('[data-slot="badge"]')).toBeNull();
    expect(container.querySelector('[data-status-icon], svg')).not.toBeNull();
    expect(container.querySelector('[data-ai-mark]')).toHaveClass('text-status-ai-mark');
    expect(container.querySelector('.sr-only')?.textContent).toMatch(/signs of AI help/);
  });

  it('draws nothing without Some or Strong signs', () => {
    expect(show(AiMark, { level: undefined }).container.querySelector('[data-ai-mark]')).toBeNull();
    expect(show(AiMark, { level: 'NotIndicated' }).container.querySelector('[data-ai-mark]')).toBeNull();
  });
});
