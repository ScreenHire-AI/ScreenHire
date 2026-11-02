import type { Interviewer } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, jobId, user } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import WithShell from '../../test-support/with-shell.svelte';
import ScheduleInterviewSheet from './schedule-interview-sheet.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const applicationId = '01a157d4-0000-7000-8000-000000000300';
const robin = '01a157d4-0000-7000-8000-0000000000c1';
const casey = '01a157d4-0000-7000-8000-0000000000c2';
const scheduleUrl = `/v1/applications/${applicationId}/interviews`;

const person = (userId: string, displayName: string, calendarConnected: boolean): Interviewer => ({
  userId,
  displayName,
  calendarConnected,
  calendarProvider: calendarConnected ? 'Sandbox' : null,
});
const team = [person(user.id, 'Avery Quinn', true), person(robin, 'Robin Ashby', true), person(casey, 'Casey Lee', false)];

beforeEach(() => {
  // Only the clock: the form checks that the window is still open against the date.
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date('2026-10-20T12:00:00Z'));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function renderSheet(routes: Parameters<typeof apiStub>[0] = {}, props: Record<string, unknown> = {}, people: Interviewer[] = team) {
  const calls: string[] = [];
  const stub = apiStub({ [`GET /v1/interviewers?jobId=${jobId}`]: () => Response.json(people), ...routes });
  vi.stubGlobal('fetch', async (input: RequestInfo | URL, init?: RequestInit) => {
    calls.push(`${(init?.method ?? 'GET').toUpperCase()} ${String(input)}`);
    return stub(input, init);
  });
  const ondone = vi.fn();
  const openCalendar = vi.fn();
  render(WithQueryClient, {
    props: {
      component: WithShell,
      props: { component: ScheduleInterviewSheet, openCalendar, props: { open: true, applicationId, jobId, candidateName: 'Casey Morgan', currentUserId: user.id, ondone, ...props } },
    },
  });
  return { calls, ondone, openCalendar, sheet: () => screen.getByRole('dialog', { name: 'Schedule interview' }) };
}

const tick = (sheet: HTMLElement, name: string) => within(sheet).getByRole('checkbox', { name: new RegExp(`^${name}`) });

async function fillWindow(sheet: HTMLElement, first: string, last: string, zone = 'America/New_York') {
  await fireEvent.change(within(sheet).getByLabelText('Time zone'), { target: { value: zone } });
  await fireEvent.input(within(sheet).getByLabelText('First day'), { target: { value: first } });
  await fireEvent.input(within(sheet).getByLabelText('Last day'), { target: { value: last } });
}

describe('Schedule interview sheet', () => {
  it('lists who sees the job, disables anyone with no calendar in words, and starts with the person scheduling ticked', async () => {
    const { sheet } = renderSheet();

    await within(sheet()).findByRole('checkbox', { name: /^Avery Quinn/ });
    const interviewers = within(sheet()).getByRole('group', { name: 'Interviewers' });
    expect(within(interviewers).getAllByRole('checkbox').map((box) => box.closest('label')?.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Avery Quinn',
      'Robin Ashby',
      'Casey Lee No calendar connected',
    ]);
    expect(tick(sheet(), 'Avery')).toBeChecked();
    expect(tick(sheet(), 'Robin')).not.toBeChecked();
    // The reason is text, tied to the disabled box, never colour alone.
    const disabled = tick(sheet(), 'Casey');
    expect(disabled).toBeDisabled();
    expect(document.getElementById(disabled.getAttribute('aria-describedby')!)).toHaveTextContent('No calendar connected');
    expect(within(sheet()).getByText("Avery Quinn's calendar sends the invitation.")).toBeInTheDocument();
  });

  it('makes the first person ticked the organizer and says so', async () => {
    const { sheet } = renderSheet();
    await within(sheet()).findByRole('checkbox', { name: /^Avery Quinn/ });

    await fireEvent.click(tick(sheet(), 'Avery'));
    await fireEvent.click(tick(sheet(), 'Robin'));
    await fireEvent.click(tick(sheet(), 'Avery'));
    expect(within(sheet()).getByText("Robin Ashby's calendar sends the invitation.")).toBeInTheDocument();

    // Ticking Avery again puts them second: Robin stays the organizer.
    await fireEvent.click(tick(sheet(), 'Avery'));
    expect(within(sheet()).getByText("Robin Ashby's calendar sends the invitation.")).toBeInTheDocument();
  });

  it('offers a half hour step of lengths and working hours of 9 to 5 on weekdays, in the browser zone', async () => {
    const { sheet } = renderSheet();
    await within(sheet()).findByRole('checkbox', { name: /^Avery Quinn/ });

    expect(within(within(sheet()).getByLabelText('Length')).getAllByRole('option').map((option) => option.textContent)).toEqual([
      '15 minutes',
      '30 minutes',
      '45 minutes',
      '1 hour',
      '1 hour 30 minutes',
      '2 hours',
    ]);
    expect(within(sheet()).getByLabelText('Length')).toHaveValue('30');
    expect(within(sheet()).getByLabelText('From')).toHaveValue('09:00');
    expect(within(sheet()).getByLabelText('To')).toHaveValue('17:00');
    expect(within(sheet()).getByText('Weekdays only.')).toBeInTheDocument();
    expect(within(sheet()).getByLabelText('Time zone')).toHaveValue(Intl.DateTimeFormat().resolvedOptions().timeZone);
    // Tomorrow to two weeks from today.
    expect(within(sheet()).getByLabelText('First day')).toHaveValue(
      new Intl.DateTimeFormat('en-CA', { timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone }).format(new Date('2026-10-21T12:00:00Z')),
    );
    await expectNoAxeViolations();
  });

  it("sends the days as instants in the chosen zone across the clock change, with the organizer first, and closes quietly", async () => {
    const bodies: unknown[] = [];
    const { calls, ondone, sheet } = renderSheet({
      [`POST ${scheduleUrl}`]: (init) => (bodies.push(JSON.parse(String(init?.body))), Response.json({ id: 'interview-1' }, { status: 201 })),
      [`GET ${scheduleUrl}`]: () => Response.json([]),
    });
    await within(sheet()).findByRole('checkbox', { name: /^Avery Quinn/ });
    await fireEvent.click(tick(sheet(), 'Avery'));
    await fireEvent.click(tick(sheet(), 'Robin'));
    await fireEvent.click(tick(sheet(), 'Avery'));
    await fireEvent.change(within(sheet()).getByLabelText('Length'), { target: { value: '45' } });
    await fillWindow(sheet(), '2026-10-31', '2026-11-02');
    await fireEvent.change(within(sheet()).getByLabelText('From'), { target: { value: '10:00' } });
    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Send invitation' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toEqual({
      interviewerUserIds: [robin, user.id],
      durationMinutes: 45,
      windowStart: '2026-10-31T04:00:00.000Z',
      windowEnd: '2026-11-03T05:00:00.000Z',
      timeZone: 'America/New_York',
      dayStart: '10:00',
      dayEnd: '17:00',
    });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(ondone).toHaveBeenCalledWith('Offered Casey Morgan times to choose from.');
    expect(calls.filter((call) => call.startsWith('POST'))).toEqual([`POST ${scheduleUrl}`]);
  });

  it('refuses a window of more than 90 days in words, and posts nothing', async () => {
    const { calls, sheet } = renderSheet();
    await within(sheet()).findByRole('checkbox', { name: /^Avery Quinn/ });

    await fillWindow(sheet(), '2026-10-21', '2027-01-20');
    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Send invitation' }));

    const last = within(sheet()).getByLabelText('Last day');
    await waitFor(() => expect(last).toHaveAttribute('aria-invalid', 'true'));
    expect(document.getElementById(last.getAttribute('aria-describedby')!)).toHaveTextContent('The window can be at most 90 days.');
    expect(calls.filter((call) => call.startsWith('POST'))).toEqual([]);
  });

  it('asks for an interviewer before it posts', async () => {
    const { calls, sheet } = renderSheet();
    await within(sheet()).findByRole('checkbox', { name: /^Avery Quinn/ });

    await fireEvent.click(tick(sheet(), 'Avery'));
    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Send invitation' }));

    expect(await within(sheet()).findByText('Choose who will interview.')).toBeInTheDocument();
    expect(calls.filter((call) => call.startsWith('POST'))).toEqual([]);
  });

  it("puts the API's field errors under their fields", async () => {
    const { sheet } = renderSheet({
      [`POST ${scheduleUrl}`]: () => Response.json({ status: 400, errors: { windowEnd: ['The window must end in the future.'], dayEnd: ['Working hours are shorter than the interview.'] } }, { status: 400 }),
    });
    await within(sheet()).findByRole('checkbox', { name: /^Avery Quinn/ });

    await fillWindow(sheet(), '2026-10-21', '2026-11-03');
    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Send invitation' }));

    const last = within(sheet()).getByLabelText('Last day');
    await waitFor(() => expect(last).toHaveAttribute('aria-invalid', 'true'));
    expect(document.getElementById(last.getAttribute('aria-describedby')!)).toHaveTextContent('The window must end in the future.');
    expect(document.getElementById(within(sheet()).getByLabelText('To').getAttribute('aria-describedby')!)).toHaveTextContent(
      'Working hours are shorter than the interview.',
    );
    expect(sheet()).toBeInTheDocument();
  });

  it('names the interviewers whose calendars do not work, under the list', async () => {
    const { sheet } = renderSheet({
      [`POST ${scheduleUrl}`]: () =>
        Response.json({ title: 'Calendar not connected', detail: 'Server words.', status: 422, interviewerUserIds: [robin, user.id] }, { status: 422 }),
    });
    await within(sheet()).findByRole('checkbox', { name: /^Avery Quinn/ });

    await fillWindow(sheet(), '2026-10-21', '2026-11-03');
    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Send invitation' }));

    const interviewers = within(sheet()).getByRole('group', { name: 'Interviewers' });
    expect(await within(interviewers).findByText('Robin Ashby and Avery Quinn have no working calendar connected. Ask them to connect one from the account menu.')).toBeInTheDocument();
    expect(sheet()).toBeInTheDocument();
  });

  it('says an interview is already open or booked in the API words, and that scheduling is not set up on a 404', async () => {
    const conflict = 'This candidate already has an interview that is open or booked. Cancel it first.';
    let status = 409;
    const { sheet } = renderSheet({
      [`POST ${scheduleUrl}`]: () =>
        status === 409 ? Response.json({ title: 'Conflict', detail: conflict, status }, { status }) : new Response(null, { status }),
    });
    await within(sheet()).findByRole('checkbox', { name: /^Avery Quinn/ });
    await fillWindow(sheet(), '2026-10-21', '2026-11-03');

    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Send invitation' }));
    await waitFor(() => expect(sheet().querySelector('[data-slot="failure-text"]')).toHaveTextContent(conflict));

    status = 404;
    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Send invitation' }));
    await waitFor(() =>
      expect(sheet().querySelector('[data-slot="failure-text"]')).toHaveTextContent('Scheduling is not set up here, so the candidate could not be sent a link.'),
    );
  });

  it('tells the person to connect a calendar when nobody can interview, and opens the Calendar sheet from there', async () => {
    const { openCalendar, sheet } = renderSheet({}, {}, [person(user.id, 'Avery Quinn', false), person(casey, 'Casey Lee', false)]);

    expect(await within(sheet()).findByText(/Nobody who sees this job has a calendar connected\./)).toBeInTheDocument();
    expect(within(sheet()).queryByRole('button', { name: 'Send invitation' })).toBeNull();
    expect(within(sheet()).queryByRole('checkbox')).toBeNull();
    await expectNoAxeViolations();

    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Connect your calendar' }));
    expect(openCalendar).toHaveBeenCalledOnce();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });
});
