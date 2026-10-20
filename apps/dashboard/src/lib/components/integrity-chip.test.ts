import { render } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { aiAssistanceLevels, authenticityLevels, type LevelView } from '#lib/integrity.js';
import IntegrityChip from './integrity-chip.svelte';

// Shape mark + colour + words, every time (design-system section 2.2): the words are visible, the mark has its shape,
// and the badge carries the level's colour.
function parts(container: HTMLElement) {
  const badge = container.querySelector('[data-slot="badge"]')!;
  return {
    badge,
    shape: badge.querySelector('[data-shape]')?.getAttribute('data-shape'),
    words: badge.querySelector('[aria-hidden="true"]:not([data-shape])')?.textContent,
    spoken: badge.querySelector('.sr-only')?.textContent,
  };
}

const authenticity: [string, LevelView, string, string, string][] = [
  ['NoConcerns', authenticityLevels.NoConcerns, 'circle', 'bg-status-clear-soft', 'No concerns'],
  ['ReviewSuggested', authenticityLevels.ReviewSuggested, 'diamond', 'bg-status-review-soft', 'Review suggested'],
  ['VerificationRecommended', authenticityLevels.VerificationRecommended, 'square', 'bg-status-verify-soft', 'Verification recommended'],
];

const ai: [string, LevelView, string, string, string][] = [
  ['NotIndicated', aiAssistanceLevels.NotIndicated, 'ring', 'border-border', 'Not indicated'],
  ['SomeIndicators', aiAssistanceLevels.SomeIndicators, 'ring', 'bg-status-ai-soft', 'Some indicators'],
  ['StrongIndicators', aiAssistanceLevels.StrongIndicators, 'ring', 'bg-status-ai-soft', 'Strong indicators'],
];

describe('integrity chip', () => {
  it.each(authenticity)('shows authenticity %s with its mark, colour and words', (_, level, shape, colour, label) => {
    const { container } = render(IntegrityChip, { props: { kind: 'authenticity', level } });
    const chip = parts(container);

    expect(chip.shape).toBe(shape);
    expect(chip.badge).toHaveClass(colour);
    expect(chip.words).toBe(label);
    expect(chip.spoken).toBe(`Authenticity: ${label}`);
  });

  it.each(ai)('shows AI assistance %s as information, with a mark and words', (_, level, shape, colour, label) => {
    const { container } = render(IntegrityChip, { props: { kind: 'aiAssistance', level } });
    const chip = parts(container);

    expect(chip.shape).toBe(shape);
    expect(chip.badge).toHaveClass(colour);
    // Never a status colour: AI assistance is not a warning.
    expect(chip.badge.className).not.toMatch(/status-(clear|review|verify)/);
    expect(chip.words).toBe(label);
    expect(chip.spoken).toBe(`AI assistance: ${label}`);
  });

  it("uses the table's short words with the number of reasons, and says them in full", () => {
    const { container } = render(IntegrityChip, {
      props: { kind: 'authenticity', level: authenticityLevels.ReviewSuggested, reasons: 3, compact: true },
    });
    const chip = parts(container);

    expect(chip.words).toBe('Review · 3');
    expect(chip.spoken).toBe('Authenticity: Review suggested, 3 reasons');
  });

  it('shows a pending chip while the checks run', () => {
    const { container } = render(IntegrityChip, { props: { kind: 'authenticity', level: null, compact: true } });
    const chip = parts(container);

    expect(chip.shape).toBe('ring');
    expect(chip.words).toBe('Pending');
    expect(chip.spoken).toBe('Authenticity: Checks pending');
  });

  it('keeps the level of a dismissed assessment and says it was dismissed', () => {
    const { container } = render(IntegrityChip, {
      props: { kind: 'authenticity', level: authenticityLevels.VerificationRecommended, reasons: 1, dismissed: true, compact: true },
    });
    const chip = parts(container);

    expect(chip.words).toBe('Verify · 1');
    expect(chip.spoken).toBe('Authenticity: Verification recommended, 1 reason, dismissed after review');
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
