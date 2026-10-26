import { render, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { formatDate, formatDateTime } from '#lib/dates.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import DateText from './date-text.svelte';

describe('date text', () => {
  it('shows the date, and the exact time in a tooltip that opens on focus (T5)', async () => {
    const at = '2026-10-20T14:05:00Z';
    render(WithTooltips, { props: { component: DateText, props: { at } } });

    const time = screen.getByText(formatDate(at));
    expect(time.tagName).toBe('TIME');
    expect(time).toHaveAttribute('datetime', at);
    time.focus();
    await waitFor(() => expect(time).toHaveAttribute('data-state', expect.stringMatching(/open/)));
    expect(document.getElementById(time.getAttribute('aria-describedby')!)).toHaveTextContent(formatDateTime(at));
  });
});
