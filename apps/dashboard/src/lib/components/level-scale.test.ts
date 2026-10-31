import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { aiAssistanceLevels, aiAssistanceOrder, authenticityLevels, authenticityOrder } from '#lib/verification.js';
import LevelScale from './level-scale.svelte';

const authenticity = authenticityOrder.map((level) => authenticityLevels[level]);
const ai = aiAssistanceOrder.map((level) => aiAssistanceLevels[level]);

describe('level scale', () => {
  it('names the level and its place for screen readers, and keeps every label in full', () => {
    render(LevelScale, { levels: ai, current: aiAssistanceLevels.NotIndicated, name: 'AI assistance' });

    const scale = screen.getByRole('img', { name: 'AI assistance: No signs of AI help, the lowest of 3' });
    // Section 16: labels keep their full names, so the other steps wrap on a phone rather than truncate.
    for (const label of ['Some signs of AI help', 'Strong signs of AI help']) {
      const step = [...scale.querySelectorAll('span')].find((span) => span.textContent === label);
      expect(step).toBeDefined();
      expect(step).not.toHaveClass('truncate');
    }
  });

  it("gives the current step's column room for its badge, so a phone does not scroll sideways", () => {
    render(LevelScale, { levels: authenticity, current: authenticityLevels.VerificationRecommended, name: 'Authenticity' });

    const scale = screen.getByRole('img', { name: 'Authenticity: Verification suggested, the highest of 3' });
    expect(scale).toHaveClass('grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(max-content,1fr)]');
    expect(scale.querySelector('[data-current]')).toHaveTextContent('Verification suggested');
  });
});
