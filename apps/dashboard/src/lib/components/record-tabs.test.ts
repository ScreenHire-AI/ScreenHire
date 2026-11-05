import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import RecordTabsHarness from '../../test-support/record-tabs-harness.svelte';

describe('record tabs', () => {
  it('shows each tab with its count, a plain number in the same grey badge', async () => {
    render(RecordTabsHarness);

    expect(screen.getByRole('tablist', { name: 'Job sections' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Candidates 24' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Job postings 7' })).toHaveTextContent('7');
    // Every count is the same grey: a state that needs a look is the record's status, not a coloured tab (E2, W7).
    expect(screen.getByRole('tab', { name: 'Verification 5' }).querySelector('[data-slot="badge"]')).toHaveClass('bg-secondary');
    expect(screen.getByRole('tab', { name: 'Details' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Activity 3' })).toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });
});
