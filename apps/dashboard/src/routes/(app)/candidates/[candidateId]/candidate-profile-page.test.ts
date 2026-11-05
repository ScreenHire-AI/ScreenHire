import type { ApplicationSummary, ApplicationTimelineEntry, CandidateDetail, CandidateHistoryEntry, CurrentUser, TeamMember } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { goto } from '$app/navigation';
import { preferences } from '#lib/preferences.svelte.js';
import { apiStub, jobs, ownerPermissions, user } from '../../../../test-support/fixtures.js';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

const appPage = vi.hoisted(() => ({ url: new URL('http://dashboard.test/candidates/x'), data: {} as Record<string, unknown> }));
vi.mock('$app/state', () => ({ page: appPage, navigating: { to: null } }));
vi.mock('$app/navigation', () => ({ goto: vi.fn(async () => {}) }));

const candidateId = '01a157d4-0000-7000-8000-000000000204';
const teammate = '01a157d4-0000-7000-8000-0000000000cc';
const supportJob = '01a157d4-0000-7000-8000-000000000011';

// The fixtures' Owner has every permission but erasing, which the catalogue gained after them.
const withErase: CurrentUser = { ...user, organization: { ...user.organization!, permissions: [...ownerPermissions, 'EraseCandidates'] } };
const withEraseOnlyOwnJobs: CurrentUser = { ...withErase, organization: { ...withErase.organization!, seesAllJobs: false } };

const members: TeamMember[] = [
  { id: 'm1', userId: teammate, email: 'robin@brightwater.example.com', displayName: 'Robin Ashby', role: 'Recruiter', customRoleId: null, roleName: 'Recruiter', joinedAt: '2026-10-02T09:00:00Z', jobAccess: 'All', seesAllJobs: true, jobCount: 0 },
];

type StageKind = ApplicationSummary['stage']['kind'];
const application = (id: string, jobTitle: string, submittedAt: string, stage: { name: string; kind: StageKind }, overrides: Partial<ApplicationSummary> = {}): ApplicationSummary => ({
  id,
  jobId: `job-${id}`,
  candidateId,
  candidateName: 'Casey Morgan',
  stage: { id: stage.name.toLowerCase(), ...stage },
  submittedAt,
  stageEnteredAt: submittedAt,
  lastActivityAt: submittedAt,
  jobTitle,
  source: 'CareersSite',
  tags: [],
  ...overrides,
});

// Noon UTC, so the date reads the same in every time zone. The API sends them in no particular order.
const designer = application('a-designer', 'Product Designer', '2026-10-20T12:00:00Z', { name: 'Interview', kind: 'Interview' });
const senior = application('a-senior', 'Senior .NET Engineer', '2026-10-02T12:00:00Z', { name: 'Hired', kind: 'Hired' }, { stageEnteredAt: '2026-10-10T12:00:00Z' });
const support = application('a-support', 'Support Engineer', '2026-09-01T12:00:00Z', { name: 'Rejected', kind: 'Rejected' }, { stageEnteredAt: '2026-09-10T12:00:00Z' });
const withdrawn = application('a-withdrawn', 'Data Analyst', '2026-09-15T12:00:00Z', { name: 'Withdrawn', kind: 'Withdrawn' }, { stageEnteredAt: '2026-09-16T12:00:00Z' });
const kept = application('a-kept', 'Account Manager', '2026-10-05T12:00:00Z', { name: 'Screening', kind: 'Screening' }, { keptInMindAt: '2026-10-12T12:00:00Z' });

const candidate = (applications: ApplicationSummary[]): CandidateDetail => ({
  id: candidateId,
  fullName: 'Casey Morgan',
  firstName: 'Casey',
  lastName: 'Morgan',
  email: 'casey@example.com',
  phone: '+44 20 7946 0000',
  createdAt: '2026-09-01T12:00:00Z',
  applications,
});

const entry = (overrides: Partial<ApplicationTimelineEntry>): ApplicationTimelineEntry => ({
  id: crypto.randomUUID(),
  type: 'Submitted',
  occurredAt: '2026-10-20T09:00:00Z',
  actorUserId: null,
  fromStageId: null,
  toStageId: null,
  note: null,
  tag: null,
  automationRunId: null,
  actorApiKeyId: null,
  scorecardId: null,
  stageId: null,
  recommendation: null,
  ...overrides,
});

const item = (applicationId: string, jobTitle: string, e: ApplicationTimelineEntry, names: Partial<CandidateHistoryEntry> = {}): CandidateHistoryEntry => ({
  applicationId,
  jobId: `job-${applicationId}`,
  jobTitle,
  entry: e,
  ...names,
});

// Newest first, as the API sends it.
const history: CandidateHistoryEntry[] = [
  item('a-designer', 'Product Designer', entry({ type: 'InvitedToApply', occurredAt: '2026-10-19T12:00:00Z', actorUserId: teammate, otherJobId: supportJob })),
  item('a-senior', 'Senior .NET Engineer', entry({ type: 'StageChanged', occurredAt: '2026-10-10T12:00:00Z', actorUserId: user.id, fromStageId: 'offer', toStageId: 'hired' }), { fromStageName: 'Offer', toStageName: 'Hired' }),
  item('a-senior', 'Senior .NET Engineer', entry({ type: 'NoteEdited', occurredAt: '2026-10-09T12:00:00Z', actorUserId: user.id })),
  item('a-senior', 'Senior .NET Engineer', entry({ type: 'NoteAdded', occurredAt: '2026-10-08T12:00:00Z', actorUserId: teammate, note: null })),
  item('a-support', 'Support Engineer', entry({ type: 'StageChanged', occurredAt: '2026-09-10T12:00:00Z', actorUserId: teammate, fromStageId: 'screening', toStageId: 'rejected' }), { fromStageName: 'Screening', toStageName: 'Rejected' }),
];

const asked = item('a-kept', 'Account Manager', entry({ type: 'LeftTalentPool', occurredAt: '2026-10-12T12:00:00Z', poolExit: 'CandidateAsked' }));

interface Setup {
  user?: CurrentUser;
  applications?: ApplicationSummary[];
  history?: CandidateHistoryEntry[];
  candidateResponse?: () => Response;
  writes?: Parameters<typeof apiStub>[0];
}

function renderPage({ user: signedIn = user, applications = [designer, senior, support, withdrawn, kept], history: entries = history, candidateResponse, writes = {} }: Setup = {}) {
  appPage.data = { user: signedIn };
  const fetchMock = vi.fn(
    apiStub({
      [`GET /v1/candidates/${candidateId}`]: candidateResponse ?? (() => Response.json(candidate(applications))),
      [`GET /v1/candidates/${candidateId}/history`]: () => Response.json(entries),
      'GET /v1/organization/members': () => Response.json(members),
      'GET /v1/jobs': () => Response.json([...jobs, { ...jobs[0]!, id: supportJob, title: 'Support Engineer' }]),
      ...writes,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return { ...render(WithQueryClient, { props: { component: Page, props: { params: { candidateId } } } }), fetchMock };
}

// What a sighted person reads: the labels that are only for screen readers (dt) left out.
const text = (element: Element | null | undefined) => {
  const copy = element?.cloneNode(true) as Element | undefined;
  copy?.querySelectorAll('dt').forEach((label) => label.remove());
  return copy?.textContent?.replace(/\s+/g, ' ').trim();
};

beforeEach(() => vi.mocked(goto).mockClear());
afterEach(() => {
  vi.unstubAllGlobals();
  localStorage.clear();
  appPage.data = {};
});

describe('candidate profile page', () => {
  it('shows who they are in the header: name, and email and phone as links, and no first-applied fact the table already shows', async () => {
    renderPage();

    expect(await screen.findByRole('heading', { level: 1, name: 'Casey Morgan' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'casey@example.com' })).toHaveAttribute('href', 'mailto:casey@example.com');
    expect(screen.getByRole('link', { name: '+44 20 7946 0000' })).toHaveAttribute('href', 'tel:+44 20 7946 0000');
    expect(text(screen.getByRole('heading', { level: 1 }).closest('[data-slot="header-band"]'))).not.toContain('First applied');
  });

  it('leaves the phone out when there is none', async () => {
    renderPage({ applications: [senior], candidateResponse: () => Response.json({ ...candidate([senior]), phone: null }) });

    expect(await screen.findByRole('heading', { level: 1, name: 'Casey Morgan' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /^\+/ })).not.toBeInTheDocument();
  });

  const applicationRows = (table: HTMLElement) =>
    within(table)
      .getAllByRole('row')
      .slice(1)
      .map((row) => within(row).getAllByRole('cell').map((cell) => cell.textContent?.trim()));

  it('lists the applications as a table, oldest first, with each stage and its dates in their own columns (L1, T5)', async () => {
    renderPage();

    const table = await screen.findByRole('table', { name: 'Applications' });
    await waitFor(() => expect(within(table).getAllByRole('row')).toHaveLength(6));
    // How far a closed application got comes from the history, which loads on its own.
    await waitFor(() => expect(applicationRows(table)[0]![3]).toBe('Screening'));

    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent?.trim())).toEqual(['Job', 'Stage', 'Applied', 'Got as far as', 'Ended']);
    // A blank cell is blank: an open application has no end date and an application that never moved has no stage reached.
    expect(applicationRows(table)).toEqual([
      ['Support Engineer', 'Rejected', 'Sep 1, 2026', 'Screening', 'Sep 10, 2026'],
      ['Data Analyst', 'Withdrawn', 'Sep 15, 2026', '', 'Sep 16, 2026'],
      ['Senior .NET Engineer', 'Hired', 'Oct 2, 2026', 'Offer', 'Oct 10, 2026'],
      ['Account Manager', 'Saved', 'Oct 5, 2026', 'Screening', 'Oct 12, 2026'],
      ['Product Designer', 'Interview', 'Oct 20, 2026', '', ''],
    ]);
    expect(within(table).getByRole('link', { name: 'Support Engineer' })).toHaveAttribute('href', '/applications/a-support');
    expect(within(table).getByRole('columnheader', { name: /^Applied/ })).toHaveAttribute('aria-sort', 'ascending');
    expect(screen.getByRole('region', { name: 'Applications' })).toHaveTextContent('5');
  });

  it('sorts the applications by a column, and compares dates as instants', async () => {
    const earlier = application('early', 'Early job', '2026-10-20T10:00:00+05:00', { name: 'Interview', kind: 'Interview' });
    const later = application('late', 'Late job', '2026-10-20T08:00:00Z', { name: 'Interview', kind: 'Interview' });
    renderPage({ applications: [later, earlier], history: [] });

    const table = await screen.findByRole('table', { name: 'Applications' });
    // 10:00 at +05:00 is 05:00 UTC, before 08:00 UTC although "10" sorts after "08" as text.
    await waitFor(() => expect(applicationRows(table).map((row) => row[0])).toEqual(['Early job', 'Late job']));

    await fireEvent.click(within(table).getByRole('button', { name: /^Applied/ }));
    expect(applicationRows(table).map((row) => row[0])).toEqual(['Late job', 'Early job']);
    await fireEvent.click(within(table).getByRole('button', { name: /^Job/ }));
    expect(applicationRows(table).map((row) => row[0])).toEqual(['Early job', 'Late job']);
  });

  it('orders a remembered sort by how far each got once the history arrives, not by what was known before it', async () => {
    preferences.setSort('candidate-applications', { id: 'reached', desc: false });
    let sendHistory: (response: Response) => void = () => {};
    const pendingHistory = new Promise<Response>((resolve) => (sendHistory = resolve));
    renderPage({ writes: { [`GET /v1/candidates/${candidateId}/history`]: () => pendingHistory } });

    const table = await screen.findByRole('table', { name: 'Applications' });
    await waitFor(() => expect(applicationRows(table)).toHaveLength(5));
    sendHistory(Response.json(history));

    await waitFor(() => expect(applicationRows(table).map((row) => row[3])).toEqual(['', '', 'Offer', 'Screening', 'Screening']));
  });

  it('lists the history across applications, newest first, naming each stage, job and who did it, without edit lines or deleted notes', async () => {
    renderPage();

    const list = await screen.findByRole('list', { name: 'History across all applications' });
    await waitFor(() => expect(within(list).getAllByRole('listitem').length).toBeGreaterThan(0));

    expect(within(list).getAllByRole('listitem').map((li) => text(li.querySelector('p')))).toEqual([
      'Invited to apply for Support Engineer by Robin Ashby in Product Designer',
      'Moved from Offer to Hired by you in Senior .NET Engineer',
      'Moved from Screening to Rejected by Robin Ashby in Support Engineer',
    ]);
    expect(screen.getByRole('region', { name: 'History' })).toHaveTextContent('3');
  });

  it('names the job on each History line only when there is more than one application', async () => {
    renderPage({
      applications: [senior],
      history: [item('a-senior', 'Senior .NET Engineer', entry({ type: 'StageChanged', occurredAt: '2026-10-10T12:00:00Z', actorUserId: user.id, fromStageId: 'offer', toStageId: 'hired' }), { fromStageName: 'Offer', toStageName: 'Hired' })],
    });

    const list = await screen.findByRole('list', { name: 'History across all applications' });
    await waitFor(() => expect(list).toHaveTextContent('Moved from Offer to Hired'));
    expect(list).not.toHaveTextContent('Senior .NET Engineer');
  });

  it('shows a note in the history as the team reads it: a mention is the person, never their id', async () => {
    const stored = `Ask @[${teammate}] about the ledger.`;
    renderPage({ history: [item('a-senior', 'Senior .NET Engineer', entry({ type: 'NoteAdded', noteId: 'n1', actorUserId: teammate, note: stored }))] });

    const list = await screen.findByRole('list', { name: 'History across all applications' });
    await waitFor(() => expect(list).toHaveTextContent('Ask @Robin Ashby about the ledger.'));
    expect(list).not.toHaveTextContent(teammate);
  });

  it('says nothing has happened yet when the history is empty', async () => {
    renderPage({ history: [] });

    expect(await screen.findByText('Nothing has happened yet.')).toBeInTheDocument();
  });

  it('shows an error with a retry in the History panel when only the history fails, and keeps the applications', async () => {
    renderPage({ writes: { [`GET /v1/candidates/${candidateId}/history`]: () => Response.json({ title: 'Server error' }, { status: 500 }) } });

    const region = await screen.findByRole('region', { name: 'History' });

    await within(region).findByRole('button', { name: /retry|try again/i });
    expect(screen.getByRole('table', { name: 'Applications' })).toBeInTheDocument();
  });

  it('shows a not found state, with a way back, when the candidate does not exist or is another organization\'s', async () => {
    renderPage({ candidateResponse: () => Response.json({ title: 'Not Found', status: 404 }, { status: 404 }) });

    expect(await screen.findByRole('heading', { level: 1, name: 'Candidate not found' })).toBeInTheDocument();
    expect(screen.getByText('They do not exist, or they belong to another organization.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to candidates' })).toHaveAttribute('href', '/candidates');
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('shows an error with a retry, not the not found state, when loading fails for another reason', async () => {
    renderPage({ candidateResponse: () => Response.json({ title: 'Server error', status: 500 }, { status: 500 }) });

    expect(await screen.findByRole('heading', { level: 1, name: 'Candidate' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Could not load the candidate' })).toBeInTheDocument();
    expect(screen.queryByText('Candidate not found')).not.toBeInTheDocument();
  });

  it('has no axe violations', async () => {
    renderPage({ user: withErase, history: [...history, asked] });
    const table = await screen.findByRole('table', { name: 'Applications' });
    await waitFor(() => expect(applicationRows(table)[0]![3]).toBe('Screening'));
    await screen.findByRole('list', { name: 'History across all applications' });

    await expectNoAxeViolations();
  });

  describe('who may erase', () => {
    it('offers Erase candidate in the More menu, and the notice, to a person with the permission who sees every job', async () => {
      renderPage({ user: withErase, history: [...history, asked] });

      await screen.findByRole('heading', { level: 1, name: 'Casey Morgan' });
      await fireEvent.pointerDown(screen.getByRole('button', { name: 'More actions' }), { button: 0, pointerType: 'mouse' });

      expect(await screen.findByRole('menuitem', { name: 'Erase candidate' })).toBeInTheDocument();
      await waitFor(() => expect(screen.getByText(/They asked to be removed from Saved candidates on Oct 12, 2026\. To erase their data as well, use Erase candidate in the More menu\./)).toBeInTheDocument());
    });

    it('has no notice when they did not ask, though the person may erase', async () => {
      renderPage({ user: withErase });

      await screen.findByRole('list', { name: 'History across all applications' });

      expect(screen.getByRole('button', { name: 'More actions' })).toBeInTheDocument();
      expect(screen.queryByText(/They asked to be taken out/)).not.toBeInTheDocument();
    });

    it('leaves the menu and the notice out for a person without the permission', async () => {
      renderPage({ history: [...history, asked] });

      await screen.findByRole('list', { name: 'History across all applications' });

      expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument();
      expect(screen.queryByText(/They asked to be taken out/)).not.toBeInTheDocument();
    });

    it('leaves the menu and the notice out for a person with the permission who sees only their own jobs', async () => {
      renderPage({ user: withEraseOnlyOwnJobs, history: [...history, asked] });

      await screen.findByRole('list', { name: 'History across all applications' });

      expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument();
      expect(screen.queryByText(/They asked to be taken out/)).not.toBeInTheDocument();
    });
  });

  describe('erasing', () => {
    const erasePath = `POST /v1/candidates/${candidateId}/erase`;

    async function openDialog() {
      await screen.findByRole('heading', { level: 1, name: 'Casey Morgan' });
      await fireEvent.pointerDown(screen.getByRole('button', { name: 'More actions' }), { button: 0, pointerType: 'mouse' });
      await fireEvent.click(await screen.findByRole('menuitem', { name: 'Erase candidate' }));
      return screen.findByRole('dialog', { name: 'Erase Casey Morgan?' });
    }

    it('opens the dialog after the menu item is chosen, and the button stays off until the name is typed', async () => {
      const sent: unknown[] = [];
      renderPage({ user: withErase, writes: { [erasePath]: (init) => (sent.push(JSON.parse(String(init?.body))), new Response(null, { status: 204 })) } });

      const dialog = await openDialog();
      const button = within(dialog).getByRole('button', { name: 'Erase candidate' });

      expect(dialog).toHaveTextContent('This erases Casey Morgan and all of their applications');
      expect(button).toBeDisabled();
      await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Type Casey Morgan to confirm' }), { target: { value: 'Casey' } });
      expect(button).toBeDisabled();
      button.click();
      expect(sent).toEqual([]);
    });

    it('sends the full name as the confirmation once it is typed, then goes to the candidates list', async () => {
      const sent: unknown[] = [];
      renderPage({ user: withErase, writes: { [erasePath]: (init) => (sent.push(JSON.parse(String(init?.body))), new Response(null, { status: 204 })) } });
      const dialog = await openDialog();

      // Typed in lower case with a trailing space, which the dialog accepts; the server gets the name as it is stored.
      await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Type Casey Morgan to confirm' }), { target: { value: 'casey morgan ' } });
      const button = within(dialog).getByRole('button', { name: 'Erase candidate' });
      await waitFor(() => expect(button).toBeEnabled());
      await fireEvent.click(button);

      await waitFor(() => expect(goto).toHaveBeenCalledWith('/candidates', { state: { erased: 'Casey Morgan' } }));
      expect(sent).toEqual([{ confirmation: 'Casey Morgan' }]);
      expect(goto).toHaveBeenCalledOnce();
    });

    it('stays on the page, with the reason in the dialog, when the server refuses', async () => {
      renderPage({ user: withErase, writes: { [erasePath]: () => Response.json({ title: 'Conflict', detail: 'Someone else is erasing this candidate.' }, { status: 409 }) } });
      const dialog = await openDialog();

      await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Type Casey Morgan to confirm' }), { target: { value: 'Casey Morgan' } });
      await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Erase candidate' })).toBeEnabled());
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Erase candidate' }));

      await waitFor(() => expect(within(dialog).getByRole('alert')).toHaveTextContent('Someone else is erasing this candidate.'));
      expect(goto).not.toHaveBeenCalled();
    });
  });
});
