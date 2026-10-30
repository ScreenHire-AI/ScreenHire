import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import RecordTabsHarness from '../../test-support/record-tabs-harness.svelte';

describe('record tabs', () => {
  it('shows each tab with its count in the same grey badge, and a state in the count where one fits', async () => {
    render(RecordTabsHarness);

    expect(screen.getByRole('tablist', { name: 'Job sections' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Candidates 24' })).toHaveAttribute('aria-selected', 'true');
    // The count reads 7/8 on screen and in words for screen readers. A browser puts a space before the badge, which jsdom
    // does not; the end-to-end tests pin the browser's name.
    const distribution = screen.getByRole('tab', { name: /^Distribution ?posted on 7 of 8 boards$/ });
    expect(distribution).toHaveTextContent('7/8');
    // Every count is the same grey: a state that needs a look is the record's status, not a coloured tab (E2, W7).
    expect(screen.getByRole('tab', { name: 'Verification 5' }).querySelector('[data-slot="badge"]')).toHaveClass('bg-secondary');
    expect(screen.getByRole('tab', { name: 'Details' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Activity 3' })).toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });
});
