import type { InterviewHoursSettings } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import InterviewHoursPanels from './interview-hours-panels.svelte';

const settings = (overrides: Partial<InterviewHoursSettings> = {}): InterviewHoursSettings => ({
  timeZone: 'Europe/London',
  weekly: null,
  bufferMinutes: 0,
  maxPerDay: null,
  feedUrl: null,
  ...overrides,
});

function renderPanels(routes: Parameters<typeof apiStub>[0]) {
  vi.stubGlobal('fetch', vi.fn(apiStub(routes)));
  render(WithQueryClient, { props: { component: InterviewHoursPanels, props: {} } });
}

afterEach(() => vi.unstubAllGlobals());

describe('interview hours and ScreenHire calendar', () => {
  it('saves the days and hours, the time between interviews and the most in a day', async () => {
    let body: unknown;
    renderPanels({
      'GET /v1/account/interview-hours': () => Response.json(settings()),
      'PUT /v1/account/interview-hours': (init) => ((body = JSON.parse(String(init?.body))), Response.json(settings({ weekly: (body as { weekly: InterviewHoursSettings['weekly'] }).weekly }))),
    });

    const region = await screen.findByRole('region', { name: 'Interview hours' });
    await fireEvent.click(await within(region).findByRole('checkbox', { name: 'Set my interview hours' }));
    await fireEvent.click(within(region).getByRole('checkbox', { name: 'Saturday' }));
    await fireEvent.change(within(region).getByLabelText('Time between interviews'), { target: { value: '15' } });
    await fireEvent.change(within(region).getByLabelText('Most interviews in a day'), { target: { value: '3' } });
    await fireEvent.click(within(region).getByRole('button', { name: 'Save hours' }));

    await waitFor(() => expect(body).toMatchObject({ timeZone: 'Europe/London', bufferMinutes: 15, maxPerDay: 3 }));
    const days = (body as { weekly: { days: { day: string }[] } }).weekly.days.map((day) => day.day);
    expect(days).toEqual(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']);
    expect(await within(region).findByText('Saved your interview hours.')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('makes the private link, and resets it only after saying the old one stops working', async () => {
    let made = 0;
    renderPanels({
      'GET /v1/account/interview-hours': () => Response.json(settings()),
      'POST /v1/account/interview-hours/feed': () => (made++, Response.json(settings({ feedUrl: `https://app.example.com/v1/public/calendar-feeds/t${made}.ics` }))),
    });

    const region = await screen.findByRole('region', { name: 'ScreenHire calendar' });
    expect(region).toHaveTextContent("never a candidate's name");
    await fireEvent.click(await within(region).findByRole('button', { name: 'Make my link' }));
    expect(await within(region).findByLabelText('Your private link')).toHaveValue('https://app.example.com/v1/public/calendar-feeds/t1.ics');

    await fireEvent.click(within(region).getByRole('button', { name: 'Reset link' }));
    const dialog = await screen.findByRole('dialog', { name: 'Reset your link?' });
    expect(dialog).toHaveTextContent('The old link stops working');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Reset link' }));
    await waitFor(() => expect(within(region).getByLabelText('Your private link')).toHaveValue('https://app.example.com/v1/public/calendar-feeds/t2.ics'));
  });
});
