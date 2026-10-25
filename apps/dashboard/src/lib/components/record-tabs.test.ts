import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import RecordTabsHarness from '../../test-support/record-tabs-harness.svelte';

describe('record tabs', () => {
  it('shows each tab with its count, a state in the count where one fits, and a marker where it does not', async () => {
    render(RecordTabsHarness);

    expect(screen.getByRole('tablist', { name: 'Job sections' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Candidates 24' })).toHaveAttribute('aria-selected', 'true');
    // The count reads 7/8 on screen and in words for screen readers.
    const distribution = screen.getByRole('tab', { name: 'Distribution, posted on 7 of 8 boards' });
    expect(distribution).toHaveTextContent('7/8');
    // The marker is a dot, not a status icon (I2), with its words.
    const integrity = screen.getByRole('tab', { name: 'Integrity, verification suggested' });
    expect(integrity.querySelector('[data-tab-marker]')).toHaveClass('bg-status-verify-mark');
    expect(integrity.querySelector('svg')).toBeNull();
    expect(screen.getByRole('tab', { name: 'Details' })).toBeInTheDocument();
    // A marked tab with a count keeps the count in its name.
    expect(screen.getByRole('tab', { name: 'Activity 3, review suggested' })).toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });
});
