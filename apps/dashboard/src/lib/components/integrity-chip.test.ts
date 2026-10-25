import { render } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { aiAssistanceLevels, authenticityLevels, type LevelView } from '#lib/integrity.js';
import IntegrityChip from './integrity-chip.svelte';

// Status icon + colour + words, every time (design system T1): the words are visible, the icon is the level's own,
// and the badge carries the level's colour.
function parts(container: HTMLElement) {
  const badge = container.querySelector('[data-slot="badge"]')!;
  return {
    badge,
    icon: badge.querySelector('[data-status-icon]')?.getAttribute('data-status-icon'),
    words: badge.querySelector('span[aria-hidden="true"]')?.textContent,
    spoken: badge.querySelector('.sr-only')?.textContent,
  };
}

const authenticity: [string, LevelView, string, string, string][] = [
  ['NoConcerns', authenticityLevels.NoConcerns, 'circle-check', 'bg-status-clear-soft', 'No concerns'],
  ['ReviewSuggested', authenticityLevels.ReviewSuggested, 'eye', 'bg-status-review-soft', 'Review suggested'],
  ['VerificationRecommended', authenticityLevels.VerificationRecommended, 'id-card', 'bg-status-verify-soft', 'Verification suggested'],
];

const ai: [string, LevelView, string, string][] = [
  ['NotIndicated', aiAssistanceLevels.NotIndicated, 'bg-status-ai-soft', 'No signs of AI help'],
  ['SomeIndicators', aiAssistanceLevels.SomeIndicators, 'bg-status-ai-soft', 'Some signs of AI help'],
  ['StrongIndicators', aiAssistanceLevels.StrongIndicators, 'bg-status-ai-soft', 'Strong signs of AI help'],
];

describe('integrity chip', () => {
  it.each(authenticity)('shows authenticity %s with its icon, colour and words', (_, level, icon, colour, label) => {
    const { container } = render(IntegrityChip, { props: { kind: 'authenticity', level } });
    const chip = parts(container);

    expect(chip.icon).toBe(icon);
    expect(chip.badge).toHaveClass(colour);
    expect(chip.words).toBe(label);
    expect(chip.spoken).toBe(`Authenticity: ${label}`);
  });

  it.each(ai)('shows AI assistance %s as information, with sparkles in its own colour (T1)', (_, level, colour, label) => {
    const { container } = render(IntegrityChip, { props: { kind: 'aiAssistance', level } });
    const chip = parts(container);

    expect(chip.icon).toBe('sparkles');
    expect(chip.badge).toHaveClass(colour);
    // Never a status colour: AI assistance is not a warning.
    expect(chip.badge.className).not.toMatch(/status-(clear|review|verify)/);
    expect(chip.words).toBe(label);
    expect(chip.spoken).toBe(`AI assistance: ${label}`);
  });

  it("uses the table's short words, the number of reasons as a count pill, and says them in full (T4, W3)", () => {
    const { container } = render(IntegrityChip, {
      props: { kind: 'authenticity', level: authenticityLevels.ReviewSuggested, reasons: 3, compact: true },
    });
    const chip = parts(container);
    const badges = container.querySelectorAll('[data-slot="badge"]');

    expect(chip.words).toBe('Review suggested');
    expect(chip.badge).toHaveClass('bg-status-review-soft');
    expect(badges).toHaveLength(2);
    expect(badges[1]).toHaveTextContent(/^3$/);
    expect(badges[1]).toHaveClass('min-w-7');
    expect(container).not.toHaveTextContent('·');
    expect(chip.spoken).toBe('Authenticity: Review suggested, 3 reasons');
  });

  it.each([
    ['authenticity', authenticityLevels.NoConcerns, 'circle-check', 'No concerns'],
    ['aiAssistance', aiAssistanceLevels.NotIndicated, 'sparkles', 'No signs'],
    ['aiAssistance', aiAssistanceLevels.StrongIndicators, 'sparkles', 'Strong signs'],
  ] as const)('keeps a calm %s level quiet in a list: muted words, no fill (T3)', (kind, level, icon, words) => {
    const { container } = render(IntegrityChip, { props: { kind, level, compact: true } });
    const chip = parts(container);

    expect(chip.badge).toHaveClass('text-muted-foreground');
    expect(chip.badge.className).not.toMatch(/bg-status/);
    expect(chip.icon).toBe(icon);
    expect(chip.words).toBe(words);
    expect(container.querySelector('[data-quiet]')).not.toBeNull();
  });

  it('shows a pending chip while the checks run', () => {
    const { container } = render(IntegrityChip, { props: { kind: 'authenticity', level: null, compact: true } });
    const chip = parts(container);

    expect(chip.icon).toBe('loader-circle');
    expect(chip.words).toBe('Checks pending');
    expect(chip.spoken).toBe('Authenticity: Checks pending');
  });

  it('keeps the level of a dismissed assessment and says it was dismissed', () => {
    const { container } = render(IntegrityChip, {
      props: { kind: 'authenticity', level: authenticityLevels.VerificationRecommended, reasons: 1, dismissed: true, compact: true },
    });
    const chip = parts(container);

    // A reviewer has handled it, so a list shows it quietly, with no count.
    expect(chip.words).toBe('Verification suggested');
    expect(chip.badge.className).not.toMatch(/bg-status/);
    expect(container.querySelectorAll('[data-slot="badge"]')).toHaveLength(1);
    expect(chip.spoken).toBe('Authenticity: Verification suggested, 1 reason, dismissed after review');
    expect(container).toHaveTextContent('Dismissed');
  });

  it('never shows a number that grades the candidate', () => {
    for (const level of [...Object.values(authenticityLevels), ...Object.values(aiAssistanceLevels)]) {
      const { container, unmount } = render(IntegrityChip, { props: { kind: 'authenticity', level } });
      expect(container.textContent).not.toMatch(/%|score/i);
      unmount();
    }
  });
});
