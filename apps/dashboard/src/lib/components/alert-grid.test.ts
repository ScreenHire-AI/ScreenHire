import { cleanup, fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it } from 'vitest';
import AlertGrid from './alert-grid.svelte';

afterEach(() => cleanup());

const rows = [{ label: 'AI usage', about: 'Tells people who can see AI usage.', inApp: 'The bell tells them.', email: 'budgetEmail', slack: 'budgetSlack' }] as const;

// Design system section 11, Alert grid: a row per alert, a fixed tick for the app, and Email and Slack only where they work.
describe('AlertGrid', () => {
  it('ticks In the app for good, never as a control, and turns Email and Slack on and off', async () => {
    render(AlertGrid, { props: { rows, values: { budgetEmail: false, budgetSlack: true }, emailAvailable: true, slackConnected: true, label: 'Alerts' } });

    const grid = screen.getByRole('table', { name: 'Alerts' });
    expect(within(grid).getAllByRole('columnheader').map((header) => header.textContent?.trim())).toEqual(['Alert', 'In the app', 'Email', 'Slack']);
    expect(within(grid).getByText(/Always\. The bell tells them\./)).toBeInTheDocument();
    expect(within(grid).getAllByRole('checkbox')).toHaveLength(2);
    expect(grid.querySelectorAll('[disabled]')).toHaveLength(0);

    const email = within(grid).getByRole('checkbox', { name: 'AI usage by email' });
    expect(email).not.toBeChecked();
    expect(within(grid).getByRole('checkbox', { name: 'AI usage on Slack, as a direct message' })).toBeChecked();
    await fireEvent.click(email);
    expect(email).toBeChecked();
  });

  it('leaves out Email where nothing can send it and Slack until it is connected', () => {
    render(AlertGrid, { props: { rows, values: { budgetEmail: false, budgetSlack: false }, emailAvailable: false, slackConnected: false } });

    const grid = screen.getByRole('table', { name: 'Alerts' });
    expect(within(grid).getAllByRole('columnheader').map((header) => header.textContent?.trim())).toEqual(['Alert', 'In the app']);
    expect(within(grid).queryByRole('checkbox')).not.toBeInTheDocument();
  });
});
