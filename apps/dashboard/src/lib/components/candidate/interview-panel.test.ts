import type { ApplicationInterview } from '@screenhire/api-client';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub, user } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import { formatDateTime } from '#lib/dates.js';
import InterviewPanel from './interview-panel.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const applicationId = '01a157d4-0000-7000-8000-000000000300';
const interviewId = '01a157d4-0000-7000-8000-000000000400';
const listUrl = `/v1/applications/${applicationId}/interviews`;
const cancelUrl = `/v1/interviews/${interviewId}/cancel`;
const offerUrl = `/v1/interviews/${interviewId}/new-times`;

const interview = (overrides: Partial<ApplicationInterview>): ApplicationInterview => ({
  id: interviewId,
  status: 'Open',
  startsAt: null,
  endsAt: null,
  windowStart: '2026-11-03T05:00:00Z',
  windowEnd: '2026-11-17T05:00:00Z',
  timeZone: 'America/New_York',
  durationMinutes: 30,
  dayStart: '09:00',
  dayEnd: '17:00',
  meetingUrl: null,
  interviewers: [
    { userId: 'u1', displayName: 'Avery Quinn', isOrganizer: true, syncStatus: 'Synced', syncProblem: null },
    { userId: 'u2', displayName: 'Robin Ashby', isOrganizer: false, syncStatus: 'Synced', syncProblem: null },
  ],
  createdAt: '2026-10-20T12:00:00Z',
  createdByUserId: 'u1',
  cancelledByCandidate: false,
  newTimesOfferedAt: null,
  ...overrides,
});
const booked = (overrides: Partial<ApplicationInterview> = {}) =>
  interview({ status: 'Booked', startsAt: '2026-11-05T15:00:00Z', endsAt: '2026-11-05T15:30:00Z', meetingUrl: 'https://meet.example.org/abc-defg', ...overrides });

beforeEach(() => {
  // Only the clock: the form checks that the window is still open against the date.
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date('2026-11-02T12:00:00Z'));
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function renderPanel(list: ApplicationInterview[], routes: Parameters<typeof apiStub>[0] = {}, mayChange = true) {
  const calls: string[] = [];
  const stub = apiStub({ [`GET ${listUrl}`]: () => Response.json(list), ...routes });
  vi.stubGlobal('fetch', async (input: RequestInfo | URL, init?: RequestInit) => {
    calls.push(`${(init?.method ?? 'GET').toUpperCase()} ${String(input)}`);
    return stub(input, init);
  });
  const ondone = vi.fn();
  render(WithQueryClient, { props: { component: InterviewPanel, props: { applicationId, candidateName: 'Casey Morgan', mayChange, ondone } } });
  return { calls, ondone, panel: () => screen.findByRole('region', { name: 'Interview' }) };
}

async function chooseOffer() {
  const trigger = await screen.findByRole('button', { name: 'Interview actions' });
  await fireEvent.pointerDown(trigger, { button: 0, pointerType: 'mouse' });
  await fireEvent.click(within(await screen.findByRole('menu')).getByRole('menuitem', { name: 'Offer new times' }));
  return screen.findByRole('dialog', { name: 'Offer new times' });
}

async function chooseCancel() {
  const trigger = await screen.findByRole('button', { name: 'Interview actions' });
  await fireEvent.pointerDown(trigger, { button: 0, pointerType: 'mouse' });
  await fireEvent.click(within(await screen.findByRole('menu')).getByRole('menuitem', { name: 'Cancel interview' }));
  return screen.findByRole('dialog', { name: 'Cancel this interview?' });
}

describe('Interview panel', () => {
  it('draws nothing when no interview was ever offered', async () => {
    const { calls } = renderPanel([]);

    await waitFor(() => expect(calls).toContain(`GET ${listUrl}`));
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Interview' })).toBeNull());
    expect(screen.queryByRole('button', { name: 'Interview actions' })).toBeNull();
  });

  it('says it is waiting for the candidate, with the days they can choose from in the interview zone, and cancels from the menu', async () => {
    const { calls, panel } = renderPanel([interview({})], { [`POST ${cancelUrl}`]: () => Response.json(interview({ status: 'Cancelled' })) });

    const region = await panel();
    expect(region).toHaveTextContent('Waiting for the candidate');
    // 2026-11-03 05:00Z is midnight in New York, and the window ends at the start of 2026-11-17: the last day is the 16th.
    expect(region).toHaveTextContent('Times from Nov 3, 2026 to Nov 16, 2026.');
    expect(within(region).queryByRole('link')).toBeNull();

    const dialog = await chooseCancel();
    expect(dialog).toHaveTextContent("The candidate's link stops working.");
    // The way back is not another Cancel next to the action.
    expect(within(dialog).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(expect.arrayContaining(['Keep it', 'Cancel interview']));
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel interview' }));
    await waitFor(() => expect(calls).toContain(`POST ${cancelUrl}`));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await expectNoAxeViolations();
  });

  it('gives each state its status badge: waiting is grey, booked is clear, and a cancelled or expired one is grey (T1)', async () => {
    for (const [list, label, tone] of [
      [[interview({})], 'Waiting for the candidate', 'neutral'],
      [[booked()], 'Booked', 'clear'],
      [[interview({ status: 'Cancelled' })], 'Cancelled', 'neutral'],
      [[interview({ status: 'Expired' })], 'Expired', 'neutral'],
    ] as const) {
      const view = renderPanel([...list]);
      const badge = (await within(await view.panel()).findByText(label)).closest('[data-status]');
      expect(badge).toHaveAttribute('data-status', tone);
      cleanup();
    }
  });

  it('marks no organizer when one person interviews', async () => {
    const { panel } = renderPanel([booked({ interviewers: [{ userId: 'u1', displayName: 'Avery Quinn', isOrganizer: true, syncStatus: 'Synced', syncProblem: null }] })]);

    const people = within(await panel()).getByRole('list', { name: 'Interviewers' });
    expect(within(people).getByRole('listitem')).toHaveTextContent(/^Avery Quinn$/);
  });

  it('shows who interviews with the organizer marked in words and the meeting link, and never the time', async () => {
    const { panel } = renderPanel([booked()]);

    const region = await panel();
    const people = within(region).getByRole('list', { name: 'Interviewers' });
    expect(within(people).getAllByRole('listitem').map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Avery Quinn Organizer', 'Robin Ashby']);
    const join = within(region).getByRole('link', { name: /Join the meeting/ });
    expect(join).toHaveAttribute('href', 'https://meet.example.org/abc-defg');
    expect(join).toHaveAttribute('rel', 'noopener noreferrer');
    // The time is the header's one fact (W7).
    expect(region.textContent).not.toContain(formatDateTime('2026-11-05T15:00:00Z'));
    expect(region.textContent).not.toMatch(/Nov 5/);
    await expectNoAxeViolations();
  });

  it('notes a calendar that is not updated yet or refused, only for the interviewer it is about', async () => {
    const { panel } = renderPanel([
      booked({
        interviewers: [
          { userId: 'u1', displayName: 'Avery Quinn', isOrganizer: true, syncStatus: 'Synced', syncProblem: null },
          { userId: 'u2', displayName: 'Robin Ashby', isOrganizer: false, syncStatus: 'Pending', syncProblem: null },
          { userId: 'u3', displayName: 'Casey Lee', isOrganizer: false, syncStatus: 'Failed', syncProblem: 'Their calendar refused the event. Ask them to connect it again.' },
          { userId: 'u4', displayName: 'Jordan Hale', isOrganizer: false, syncStatus: 'Failed', syncProblem: null },
        ],
      }),
    ]);

    const region = await panel();
    const items = within(within(region).getByRole('list', { name: 'Interviewers' })).getAllByRole('listitem');
    expect(items[0]).not.toHaveTextContent(/calendar/i);
    expect(items[1]).toHaveTextContent("Not on Robin's calendar yet.");
    expect(items[2]).toHaveTextContent('Their calendar refused the event. Ask them to connect it again.');
    expect(items[3]).toHaveTextContent("Could not put this on Jordan's calendar.");
  });

  it('says a booked interview leaves every calendar, and cancels it after the dialog', async () => {
    const { calls } = renderPanel([booked()], { [`POST ${cancelUrl}`]: () => Response.json(interview({ status: 'Cancelled' })) });

    const dialog = await chooseCancel();
    expect(dialog).toHaveTextContent("The candidate is told, and it leaves every interviewer's calendar.");
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel interview' }));
    await waitFor(() => expect(calls).toContain(`POST ${cancelUrl}`));
  });

  it('keeps the dialog open with the reason when the API refuses', async () => {
    renderPanel([booked()], {
      [`POST ${cancelUrl}`]: () => Response.json({ title: 'Conflict', detail: 'This interview is already cancelled or has expired.', status: 409 }, { status: 409 }),
    });

    const dialog = await chooseCancel();
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel interview' }));
    await waitFor(() => expect(dialog).toHaveTextContent('This interview is already cancelled or has expired.'));
  });

  it('says in one line how the latest interview ended, with no actions', async () => {
    const { panel } = renderPanel([
      interview({ status: 'Cancelled', cancelledByCandidate: true, startsAt: '2026-11-05T15:00:00Z', endsAt: '2026-11-05T15:30:00Z' }),
      interview({ id: 'older', status: 'Expired' }),
    ]);

    const region = await panel();
    expect(region).toHaveTextContent(`The interview on ${formatDateTime('2026-11-05T15:00:00Z')} was cancelled by the candidate.`);
    expect(within(region).queryByRole('list')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Interview actions' })).toBeNull();
  });

  it('words an invitation withdrawn before a time was picked', async () => {
    const withdrawn = renderPanel([interview({ status: 'Cancelled' })]);
    expect(await withdrawn.panel()).toHaveTextContent('The invitation was cancelled.');
  });

  it('says the candidate did not pick a time before the last day when the window passed', async () => {
    const { panel } = renderPanel([interview({ status: 'Expired' })]);
    expect(await panel()).toHaveTextContent('The candidate did not pick a time before Nov 16, 2026.');
  });

  it('offers no way to cancel or move to someone who cannot email candidates', async () => {
    const { panel } = renderPanel([booked()], {}, false);

    await panel();
    expect(screen.queryByRole('button', { name: 'Interview actions' })).toBeNull();
    expect(screen.queryByRole('dialog', { name: 'Offer new times' })).toBeNull();
  });

  it('offers new times only for a booked interview, from the same menu as Cancel', async () => {
    renderPanel([interview({})]);

    const trigger = await screen.findByRole('button', { name: 'Interview actions' });
    await fireEvent.pointerDown(trigger, { button: 0, pointerType: 'mouse' });
    const items = within(await screen.findByRole('menu')).getAllByRole('menuitem').map((item) => item.textContent?.trim());
    expect(items).toEqual(['Cancel interview']);
  });

  it('says the offer is waiting while the candidate has new times, and that the booked time stays', async () => {
    const { panel } = renderPanel([booked({ newTimesOfferedAt: '2026-11-02T12:00:00Z', windowStart: '2026-11-09T05:00:00Z', windowEnd: '2026-11-17T05:00:00Z' })]);

    const region = await panel();
    expect(region).toHaveTextContent('Booked');
    expect(region).toHaveTextContent('Waiting for a new time');
    expect(region).toHaveTextContent('The booked time stays until Casey Morgan picks one. New times from Nov 9, 2026 to Nov 16, 2026.');
    expect(within(region).getByText('Waiting for a new time').closest('[data-status]')).toHaveAttribute('data-status', 'neutral');
    await expectNoAxeViolations();
  });

  it('offers new times for the days chosen in the interview zone, closes, and says so', async () => {
    const bodies: unknown[] = [];
    const { calls, ondone } = renderPanel([booked()], {
      [`POST ${offerUrl}`]: (init) => (bodies.push(JSON.parse(String(init?.body))), Response.json(booked({ newTimesOfferedAt: '2026-11-02T12:00:00Z' }))),
    });

    const dialog = await chooseOffer();
    expect(dialog).toHaveTextContent('Casey Morgan gets a link to pick another time.');
    expect(dialog).toHaveTextContent('Same hours as before: 9:00 AM to 5:00 PM on weekdays, New York (Eastern Time), 30 minutes.');
    expect(dialog).toHaveTextContent('The booked time stays until they pick.');
    await fireEvent.input(within(dialog).getByLabelText('First day'), { target: { value: '2026-11-09' } });
    await fireEvent.input(within(dialog).getByLabelText('Last day'), { target: { value: '2026-11-13' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Send new times' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    // Midnight in New York: 05:00Z before the clock change on 2026-11-01 ended, so EST is 5 hours behind.
    expect(bodies[0]).toEqual({ windowStart: '2026-11-09T05:00:00.000Z', windowEnd: '2026-11-14T05:00:00.000Z' });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(ondone).toHaveBeenCalledWith('Offered Casey Morgan new times to choose from.');
    expect(calls.filter((call) => call.startsWith('POST'))).toEqual([`POST ${offerUrl}`]);
  });

  it('refuses a window of more than 90 days in words and posts nothing', async () => {
    const { calls } = renderPanel([booked()]);

    const dialog = await chooseOffer();
    await fireEvent.input(within(dialog).getByLabelText('First day'), { target: { value: '2026-11-09' } });
    await fireEvent.input(within(dialog).getByLabelText('Last day'), { target: { value: '2027-03-09' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Send new times' }));

    const last = within(dialog).getByLabelText('Last day');
    await waitFor(() => expect(last).toHaveAttribute('aria-invalid', 'true'));
    expect(document.getElementById(last.getAttribute('aria-describedby')!)).toHaveTextContent('The window can be at most 90 days.');
    expect(calls.filter((call) => call.startsWith('POST'))).toEqual([]);
  });

  it("keeps the sheet open with the API's words when it refuses", async () => {
    renderPanel([booked()], {
      [`POST ${offerUrl}`]: () => Response.json({ title: 'Conflict', detail: 'New times can be offered only for a booked interview.', status: 409 }, { status: 409 }),
    });

    const dialog = await chooseOffer();
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Send new times' }));

    await waitFor(() => expect(dialog).toHaveTextContent('New times can be offered only for a booked interview.'));
    expect(screen.getByRole('dialog', { name: 'Offer new times' })).toBeInTheDocument();
  });
});
