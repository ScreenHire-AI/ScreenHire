import type { CurrentUser, JobDetail, ManualPostingStatus } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiStub, board, jobId, now, posting, userAs } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import DistributionPanel from './distribution-panel.svelte';

const state = vi.hoisted(() => ({ user: undefined as CurrentUser | undefined }));
vi.mock('$app/state', () => ({
  page: {
    url: new URL('http://dashboard.test/jobs'),
    get data() {
      return { user: state.user };
    },
  },
  navigating: { to: null },
}));

const boards = [
  board('careers', 'Careers page', { mode: 'Hosted', canUnpublishPerBoard: false, canCustomize: false }),
  board('adzuna', 'Adzuna feed', { mode: 'Pull', confirmsLive: false, confirmsRemoval: false }),
  board('sandbox', 'Sandbox board'),
];

type PanelJob = Pick<JobDetail, 'id' | 'status' | 'postings'> & Partial<Pick<JobDetail, 'title' | 'location' | 'employmentType' | 'salary' | 'description'>>;

const jobUrl = 'https://acme.careers.example.com/jobs/senior-engineer';
function manualPosting(overrides: Partial<ManualPostingStatus> = {}): ManualPostingStatus {
  return {
    id: '01a157d4-0000-7000-8000-0000000000a1',
    boardName: 'LinkedIn',
    sourceKey: 'linkedin',
    trackedUrl: `${jobUrl}?src=linkedin`,
    listingUrl: 'https://www.linkedin.com/jobs/view/123',
    postedOn: '2026-10-18',
    endsOn: null,
    contentAt: '2026-10-18T09:00:00Z',
    takenDownAt: null,
    changedFields: [],
    createdAt: '2026-10-18T09:00:00Z',
    updatedAt: '2026-10-18T09:00:00Z',
    ...overrides,
  };
}
const manualRow = (boardName: string) => [...document.querySelectorAll<HTMLElement>('[data-manual-posting]')].find((row) => row.textContent?.includes(boardName))!;
// The two reads the panel makes for the team's own postings and the counts, with whatever else a test adds.
function stubReads(postings: ManualPostingStatus[], counts: { source: string; applications: number }[] = [], more: Parameters<typeof apiStub>[0] = {}) {
  const fetchMock = vi.fn(
    apiStub({
      [`GET /v1/jobs/${jobId}/manual-postings`]: () => Response.json({ jobUrl, postings }),
      [`GET /v1/jobs/${jobId}/source-applications`]: () => Response.json(counts),
      ...more,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function renderPanel(job: PanelJob, props: Record<string, unknown> = {}) {
  return render(WithQueryClient, { props: { component: DistributionPanel, props: { job, boards, now, ...props } } });
}

const item = (boardId: string) => document.querySelector<HTMLElement>(`[data-board="${boardId}"]`)!;
const stateOf = (boardId: string) => item(boardId).querySelector('[data-posting-state]')?.textContent?.trim();
const cellsOf = (boardId: string) => [...item(boardId).querySelectorAll('td')].map((cell) => cell.textContent?.trim());
const menuFor = (boardId: string) => within(item(boardId)).queryByRole('button', { name: /^Actions for / });
async function openMenu(name: string) {
  // bits-ui opens a menu on pointerdown.
  await fireEvent.pointerDown(screen.getByRole('button', { name: `Actions for ${name}` }), { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
}
const in20Seconds = new Date(now + 20_000).toISOString();
const in4Hours = new Date(now + 4 * 3_600_000).toISOString();

beforeEach(() => {
  state.user = userAs('Owner');
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('distribution panel', () => {
  it("shows each board's state as a status badge, and the board's own estimate as a pill", () => {
    renderPanel({
      id: jobId,
      status: 'Open',
      postings: [
        posting('careers', 'Careers page'),
        posting('adzuna', 'Adzuna feed', { state: 'Submitted', confirmsLive: false, confirmsRemoval: false, expectedBy: in4Hours }),
        posting('sandbox', 'Sandbox board', { state: 'Queued', expectedBy: in20Seconds }),
      ],
    });

    expect(stateOf('careers')).toBe('Live');
    expect(stateOf('adzuna')).toBe('Sent');
    // The estimate is in the status's tooltip and for screen readers, never a value in the row.
    expect(item('adzuna')).toHaveTextContent('Expected in 4 hours.');
    expect(item('adzuna')).not.toHaveTextContent('~4h');
    expect(stateOf('sandbox')).toBe('Queued');
    expect(item('careers').querySelector('[data-status-icon]')).toHaveAttribute('data-status-icon', 'door-open');
    // Waiting on the board is grey: nobody needs to act (T3).
    expect(item('sandbox').querySelector('[data-status-icon]')).toHaveAttribute('data-status-icon', 'clock-4');
    expect(item('sandbox').querySelector('[data-status]')).toHaveAttribute('data-status', 'neutral');
    expect(screen.getByRole('region', { name: 'Job postings' })).toBeInTheDocument();
    expect(item('adzuna')).toHaveTextContent('this board never confirms when a job shows');
  });

  it('never says "removed" for a board that does not confirm removal', () => {
    renderPanel({
      id: jobId,
      status: 'Closed',
      postings: [
        posting('careers', 'Careers page', { state: 'Closed' }),
        posting('adzuna', 'Adzuna feed', { state: 'Closing', confirmsLive: false, confirmsRemoval: false, expectedBy: in4Hours }),
        posting('sandbox', 'Sandbox board', { state: 'Closing', expectedBy: in20Seconds }),
      ],
    });

    expect(stateOf('careers')).toBe('Removed');
    expect(stateOf('adzuna')).toBe('Removal requested');
    expect(stateOf('sandbox')).toBe('Removing');
    expect(screen.queryByRole('button', { name: /^Actions for / })).not.toBeInTheDocument();
  });

  it('offers no board action on a draft, which is published from the page header', () => {
    renderPanel({ id: jobId, status: 'Draft', postings: [] });

    expect(stateOf('sandbox')).toBe('Not posted');
    expect(menuFor('sandbox')).toBeNull();
  });

  it('says when each posting went out and when it last changed, and leaves a board it never reached blank (L1)', () => {
    renderPanel({
      id: jobId,
      status: 'Open',
      postings: [
        posting('careers', 'Careers page', { submittedAt: null, liveAt: '2026-11-02T09:00:00Z', updatedAt: '2026-11-03T09:00:00Z' }),
        posting('adzuna', 'Adzuna feed', { state: 'Submitted', submittedAt: '2026-11-01T09:00:00Z', updatedAt: '2026-11-04T09:00:00Z' }),
      ],
    });

    // The Applications column (index 3) stays blank until the counts arrive; none are stubbed here.
    expect(cellsOf('careers').slice(2, 5)).toEqual(['Nov 2, 2026', '', 'Nov 3, 2026']);
    expect(cellsOf('adzuna').slice(2, 5)).toEqual(['Nov 1, 2026', '', 'Nov 4, 2026']);
    expect(cellsOf('sandbox').slice(2, 5)).toEqual(['', '', '']);
    for (const name of ['Status', 'Board', 'Posted', 'Applications', 'Last updated']) {
      expect(screen.getByRole('columnheader', { name })).toBeInTheDocument();
    }
  });

  it('offers the headers choices in a Sort chip for a phone, where the table has no header (L2)', async () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('careers', 'Careers page', { updatedAt: '2026-11-03T09:00:00Z' })] });

    const chip = screen.getByRole('button', { name: 'Sort' });
    expect(chip.closest('div')).toHaveClass('md:hidden');
    await fireEvent.pointerDown(chip, { button: 0, pointerType: 'mouse' });
    const choices = (await screen.findAllByRole('menuitemradio')).map((choice) => choice.textContent?.replace(/,.*$/, '').trim());
    expect(choices).toEqual(['Status', 'Board', 'Posted', 'Applications', 'Last updated']);
  });

  it('keeps one row per board whatever a posting needs, with what is wrong for screen readers and in the tooltip', () => {
    renderPanel({
      id: jobId,
      status: 'Open',
      postings: [
        posting('careers', 'Careers page', { state: 'Draft', issues: ['Adzuna needs a description of at least 100 characters.'] }),
        posting('sandbox', 'Sandbox board', { state: 'Rejected', lastError: 'Title too long.' }),
      ],
    });

    expect(screen.getByRole('table').querySelectorAll('tbody tr')).toHaveLength(boards.length);
    expect(item('careers')).toHaveTextContent('Not sent: Adzuna needs a description of at least 100 characters.');
    expect(item('sandbox')).toHaveTextContent('Rejected by the board: Title too long.');
  });

  it('puts both fixes in the row menu, each saying how far it reaches', async () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('adzuna', 'Adzuna feed', { state: 'Draft', issues: ['Adzuna needs a description of at least 100 characters.'] })] });

    const menu = await openMenu('Adzuna feed');
    expect(within(menu).getByRole('menuitem', { name: 'Customize for Adzuna feed' })).toBeInTheDocument();
    expect(within(menu).getByRole('menuitem', { name: 'Edit details for every board' })).toHaveAttribute('href', `/jobs/${jobId}/edit`);
  });

  it('offers only Edit details on the Careers page, which is never customized', async () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('careers', 'Careers page', { state: 'Draft', issues: ['Add a city.'] })] });

    const menu = await openMenu('Careers page');
    expect(within(menu).getByRole('menuitem', { name: 'Edit details for every board' })).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: /^Customize/ })).toBeNull();
  });

  it('offers Customize in the menu of every other board while the job is open', async () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('adzuna', 'Adzuna feed', { state: 'Submitted' }), posting('sandbox', 'Sandbox board')] });

    expect(within(await openMenu('Sandbox board')).getByRole('menuitem', { name: 'Customize for Sandbox board' })).toBeInTheDocument();
  });

  it('offers no Customize on a board that is removing the job, which saving would post again', () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('sandbox', 'Sandbox board', { state: 'Closing', expectedBy: in20Seconds })] });

    expect(menuFor('sandbox')).toBeNull();
  });

  it('marks a board with text of its own with one badge after its name, and no other board', () => {
    renderPanel({
      id: jobId,
      status: 'Open',
      postings: [posting('adzuna', 'Adzuna feed', { state: 'Submitted', customizedDescription: true }), posting('sandbox', 'Sandbox board')],
    });

    expect(within(item('adzuna')).getAllByText('Customized')).toHaveLength(1);
    expect(within(item('sandbox')).queryByText('Customized')).toBeNull();
  });

  it('says when the job changed since a board was customized, without a second badge', () => {
    renderPanel({
      id: jobId,
      status: 'Open',
      postings: [posting('adzuna', 'Adzuna feed', { state: 'Submitted', customizedTitle: true, jobChangedSinceCustomized: true })],
    });

    expect(within(item('adzuna')).getByText('Job changed')).toBeInTheDocument();
    expect(within(item('adzuna')).queryByText('Customized')).toBeNull();
  });

  it("opens the Customize sheet with the board's text from its menu", async () => {
    const customization = {
      boardId: 'adzuna',
      boardName: 'Adzuna feed',
      title: { override: 'Senior engineer (remote)', job: 'Senior .NET Engineer' },
      description: { override: null, job: '<p>Short.</p>' },
      changes: [],
    };
    vi.stubGlobal('fetch', vi.fn(apiStub({ [`GET /v1/jobs/${jobId}/postings/adzuna/customization`]: () => Response.json(customization) })));
    renderPanel({ id: jobId, status: 'Open', postings: [posting('adzuna', 'Adzuna feed', { state: 'Draft', issues: ['Adzuna needs a description of at least 100 characters.'] })] });

    await fireEvent.click(within(await openMenu('Adzuna feed')).getByRole('menuitem', { name: 'Customize for Adzuna feed' }));

    const sheet = await screen.findByRole('dialog', { name: 'Customize for Adzuna feed' });
    await waitFor(() => expect(within(sheet).getByLabelText('Title')).toHaveValue('Senior engineer (remote)'));
  });

  it('links to the live posting, and copies its link, where the board has one', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', Object.assign(Object.create(navigator), { clipboard: { writeText } }));
    const url = 'https://careers.example.com/acme/jobs/senior-engineer';
    renderPanel({ id: jobId, status: 'Open', postings: [posting('careers', 'Careers page', { viewUrl: url }), posting('sandbox', 'Sandbox board')] });

    const menu = await openMenu('Careers page');
    const view = within(menu).getByRole('menuitem', { name: /^View posting/ });
    expect(view).toHaveAttribute('href', url);
    expect(view).toHaveAttribute('target', '_blank');
    // The reason a board that only closes with the job has no Take down stays beside the link.
    expect(within(menu).getByRole('menuitem', { name: 'Stays on while the job is open' })).toHaveAttribute('aria-disabled', 'true');

    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Copy link' }));

    await waitFor(() => expect(within(menu).getByRole('menuitem', { name: 'Copied' })).toBeInTheDocument());
    expect(writeText).toHaveBeenCalledWith(url);
  });

  it('offers no link for a board that never says where its listing landed', async () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('sandbox', 'Sandbox board')] });

    const menu = await openMenu('Sandbox board');
    expect(within(menu).queryByRole('menuitem', { name: /^View posting/ })).toBeNull();
    expect(within(menu).queryByRole('menuitem', { name: 'Copy link' })).toBeNull();
    expect(within(menu).getByRole('menuitem', { name: 'Take down' })).toBeInTheDocument();
  });

  it('sends a posting whose checks failed to its fix, then offers Retry', async () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('sandbox', 'Sandbox board', { state: 'Draft', issues: ['Sandbox board needs a city.'] })] });

    const menu = await openMenu('Sandbox board');
    expect(within(menu).getByRole('menuitem', { name: 'Edit details for every board' })).toHaveAttribute('href', `/jobs/${jobId}/edit`);
    expect(within(menu).getByRole('menuitem', { name: 'Retry' })).toBeInTheDocument();
  });

  it('sends a posting that needs fixes again from its menu', async () => {
    const fetchMock = vi.fn(apiStub({ [`POST /v1/jobs/${jobId}/publish`]: () => Response.json({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderPanel({ id: jobId, status: 'Open', postings: [posting('sandbox', 'Sandbox board', { state: 'Error', lastError: 'Timed out.' })] });

    await fireEvent.click(within(await openMenu('Sandbox board')).getByRole('menuitem', { name: 'Retry' }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        `/v1/jobs/${jobId}/publish`,
        expect.objectContaining({ method: 'POST', body: JSON.stringify({ boards: ['sandbox'] }) }),
      ),
    );
  });

  it('takes the job down from one board from its menu', async () => {
    const fetchMock = vi.fn(apiStub({ [`POST /v1/jobs/${jobId}/postings/sandbox/close`]: () => Response.json({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderPanel({ id: jobId, status: 'Open', postings: [posting('careers', 'Careers page'), posting('sandbox', 'Sandbox board')] });

    await fireEvent.click(within(await openMenu('Sandbox board')).getByRole('menuitem', { name: 'Take down' }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(`/v1/jobs/${jobId}/postings/sandbox/close`, expect.objectContaining({ method: 'POST' })),
    );
  });

  it('says why a board that only closes with the job has nothing to do (E3)', async () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('careers', 'Careers page')] });

    const item = within(await openMenu('Careers page')).getByRole('menuitem', { name: 'Stays on while the job is open' });
    expect(item).toHaveAttribute('aria-disabled', 'true');
  });

  it('has no menu for a board still removing the job', () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('sandbox', 'Sandbox board', { state: 'Closing', expectedBy: in20Seconds })] });

    expect(menuFor('sandbox')).toBeNull();
  });

  it('offers Post for a board the job never went to', async () => {
    renderPanel({ id: jobId, status: 'Open', postings: [] });

    expect(within(await openMenu('Sandbox board')).getByRole('menuitem', { name: 'Post' })).toBeInTheDocument();
  });

  it('posts an open job again to a board it was taken down from, naming only that board', async () => {
    const fetchMock = vi.fn(apiStub({ [`POST /v1/jobs/${jobId}/publish`]: () => Response.json({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderPanel({
      id: jobId,
      status: 'Open',
      postings: [posting('careers', 'Careers page'), posting('sandbox', 'Sandbox board', { state: 'Closed' })],
    });

    await fireEvent.click(within(await openMenu('Sandbox board')).getByRole('menuitem', { name: 'Post again' }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        `/v1/jobs/${jobId}/publish`,
        expect.objectContaining({ method: 'POST', body: JSON.stringify({ boards: ['sandbox'] }) }),
      ),
    );
  });

  it("shows the API's reason when a board action is refused", async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          [`POST /v1/jobs/${jobId}/postings/sandbox/close`]: () =>
            Response.json({ detail: 'A board answered meanwhile; refresh and try again.' }, { status: 409 }),
        }),
      ),
    );
    renderPanel({ id: jobId, status: 'Open', postings: [posting('sandbox', 'Sandbox board')] });

    await fireEvent.click(within(await openMenu('Sandbox board')).getByRole('menuitem', { name: 'Take down' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('A board answered meanwhile; refresh and try again.'));
  });
  // The plan refused the post (HTTP 402): a notice with the API's sentence, and for an owner the way to upgrade.
  describe('when the plan refuses a post', () => {
    const refuse = () =>
      vi.stubGlobal(
        'fetch',
        vi.fn(
          apiStub({
            [`POST /v1/jobs/${jobId}/publish`]: () =>
              Response.json(
                {
                  type: 'urn:screenhire:problem:plan-limit',
                  title: 'Plan limit reached',
                  detail: 'Your plan allows 5 open jobs and 5 are open. Close a job, or ask an owner to upgrade the plan.',
                },
                { status: 402 },
              ),
          }),
        ),
      );
    const post = async () => {
      renderPanel({ id: jobId, status: 'Open', postings: [] });
      await fireEvent.click(within(await openMenu('Sandbox board')).getByRole('menuitem', { name: 'Post' }));
      return screen.findByText(/Your plan allows 5 open jobs/);
    };

    it('links an owner to Billing', async () => {
      refuse();

      const notice = (await post()).closest('[data-slot="notice"]')!;

      expect(within(notice as HTMLElement).getByRole('link', { name: 'Upgrade plan' })).toHaveAttribute('href', '/settings/billing');
      expect(notice.closest('[data-slot="panel"]')).toBeNull();
      expect(screen.queryByRole('alert')).toBeNull();
    });

    it('shows a recruiter the sentence alone, which already says to ask an owner', async () => {
      state.user = userAs('Recruiter');
      refuse();

      const notice = (await post()).closest('[data-slot="notice"]')!;

      expect(notice).toHaveTextContent('ask an owner to upgrade the plan');
      expect(within(notice as HTMLElement).queryByRole('link')).toBeNull();
    });
  });

  describe('posted by you', () => {
    const fullJob = { title: 'Senior Engineer', location: 'Remote (United Kingdom)', employmentType: 'FullTime', salary: null, description: '<p>Build things.</p>' } as const;

    it('lists a board the team posted on below the others, its name linking to the listing, with every link counted', async () => {
      stubReads([manualPosting()], [
        { source: 'careers', applications: 3 },
        { source: 'adzuna', applications: 1 },
        { source: 'linkedin', applications: 2 },
      ]);
      renderPanel({
        id: jobId,
        status: 'Open',
        postings: [posting('careers', 'Careers page'), posting('adzuna', 'Adzuna feed', { state: 'Submitted' }), posting('google', 'Google for Jobs', { state: 'Submitted' })],
        ...fullJob,
      });

      await waitFor(() => expect(manualRow('LinkedIn')).toBeDefined());
      const row = manualRow('LinkedIn');
      expect(row.querySelector('[data-posting-state]')).toHaveTextContent('Posted by you');
      const link = within(row).getByRole('link', { name: /LinkedIn/ });
      expect(link).toHaveAttribute('href', 'https://www.linkedin.com/jobs/view/123');
      expect(link).toHaveAttribute('rel', 'noopener noreferrer');
      // The automatic boards' rows come first; the team's own come after them.
      expect(document.querySelector('tbody tr:last-child')).toBe(row);
      await waitFor(() => expect(row.querySelector('[data-applications]')).toHaveTextContent('2'));
      expect(item('careers').querySelector('[data-applications]')).toHaveTextContent('3');
      expect(item('adzuna').querySelector('[data-applications]')).toHaveTextContent('1');
      // Google sends candidates to the careers page's own address, so it has no count of its own.
      expect(item('google').querySelector('[data-applications]')).toBeNull();
      expect(item('google')).toHaveTextContent('Counted with the careers page');
    });

    it('says the job changed since it was posted, and what changed, until it is marked updated', async () => {
      const fetchMock = stubReads([manualPosting({ changedFields: ['Title', 'Salary'] })], [], {
        [`POST /v1/jobs/${jobId}/manual-postings/01a157d4-0000-7000-8000-0000000000a1/updated`]: () => Response.json(manualPosting()),
      });
      renderPanel({ id: jobId, status: 'Open', postings: [], ...fullJob });

      await waitFor(() => expect(manualRow('LinkedIn').querySelector('[data-job-changed]')).toHaveTextContent('Job changed since you posted'));

      await fireEvent.click(within(await openMenu('LinkedIn')).getByRole('menuitem', { name: 'Mark as updated' }));

      await waitFor(() =>
        expect(fetchMock).toHaveBeenCalledWith(`/v1/jobs/${jobId}/manual-postings/01a157d4-0000-7000-8000-0000000000a1/updated`, expect.objectContaining({ method: 'POST' })),
      );
    });

    it('marks in Copy the updated text which fields changed since the listing was posted', async () => {
      stubReads([manualPosting({ changedFields: ['Title', 'Salary'] })]);
      renderPanel({ id: jobId, status: 'Open', postings: [], ...fullJob });

      await waitFor(() => expect(manualRow('LinkedIn')).toBeDefined());
      await fireEvent.click(within(await openMenu('LinkedIn')).getByRole('menuitem', { name: 'Copy the updated text' }));

      const sheet = await screen.findByRole('dialog', { name: 'Update LinkedIn' });
      expect(sheet).toHaveTextContent('Since you posted it, the job changed the title and the salary.');
      expect(sheet.querySelector('[data-copy-field="Title"]')).toHaveTextContent('Changed');
      expect(sheet.querySelector('[data-copy-field="Description"]')).not.toHaveTextContent('Changed');
      expect(within(sheet).getByRole('button', { name: 'Mark as updated' })).toBeInTheDocument();
    });

    it('says Still posted by you once the job is closed, and offers Mark as taken down', async () => {
      const fetchMock = stubReads([manualPosting()], [], {
        [`POST /v1/jobs/${jobId}/manual-postings/01a157d4-0000-7000-8000-0000000000a1/taken-down`]: () => Response.json(manualPosting({ takenDownAt: '2026-10-20T12:00:00Z' })),
      });
      renderPanel({ id: jobId, status: 'Closed', postings: [], ...fullJob });

      await waitFor(() => expect(manualRow('LinkedIn').querySelector('[data-posting-state]')).toHaveTextContent('Still posted by you'));
      expect(manualRow('LinkedIn')).toHaveTextContent('the job is closed');
      expect(manualRow('LinkedIn').querySelector('[data-status-icon]')).toHaveAttribute('data-status-icon', 'megaphone');

      await fireEvent.click(within(await openMenu('LinkedIn')).getByRole('menuitem', { name: 'Mark as taken down' }));

      await waitFor(() =>
        expect(fetchMock).toHaveBeenCalledWith(`/v1/jobs/${jobId}/manual-postings/01a157d4-0000-7000-8000-0000000000a1/taken-down`, expect.objectContaining({ method: 'POST' })),
      );
    });

    it('flags a posting whose end date has passed on an open job, and leaves a taken-down one quiet', async () => {
      stubReads([
        manualPosting({ endsOn: '2026-10-19' }),
        manualPosting({ id: '01a157d4-0000-7000-8000-0000000000a3', boardName: 'Indeed', sourceKey: 'indeed', takenDownAt: '2026-10-19T10:00:00Z', changedFields: ['Title'] }),
      ]);
      renderPanel({ id: jobId, status: 'Open', postings: [], ...fullJob });

      await waitFor(() => expect(manualRow('LinkedIn').querySelector('[data-posting-state]')).toHaveTextContent('Still posted by you'));
      expect(manualRow('LinkedIn')).toHaveTextContent('the end date you gave has passed');
      expect(manualRow('Indeed').querySelector('[data-posting-state]')).toHaveTextContent('Taken down');
      expect(manualRow('Indeed').querySelector('[data-job-changed]')).toBeNull();
    });

    it("makes the board's apply link from its name as it is typed, and saves the posting with that link", async () => {
      let sent: unknown;
      stubReads([], [], {
        [`POST /v1/jobs/${jobId}/manual-postings`]: (init) => {
          sent = JSON.parse(String(init?.body));
          return Response.json(manualPosting(), { status: 201 });
        },
      });
      renderPanel({ id: jobId, status: 'Open', postings: [], ...fullJob });

      await waitFor(() => expect(screen.getByRole('button', { name: 'Post it yourself' })).toBeEnabled());
      await fireEvent.click(screen.getByRole('button', { name: 'Post it yourself' }));
      const sheet = await screen.findByRole('dialog', { name: 'Post it yourself' });
      expect(within(sheet).getByRole('button', { name: 'Copy title' })).toBeInTheDocument();
      expect(within(sheet).getByRole('button', { name: 'Copy formatted description' })).toBeInTheDocument();
      expect(within(sheet).getByRole('button', { name: 'Copy as plain text description' })).toBeInTheDocument();

      await fireEvent.input(within(sheet).getByLabelText('Board'), { target: { value: 'Riverside Jobs Weekly' } });
      expect(within(sheet).getByLabelText('Apply link for this board')).toHaveValue(`${jobUrl}?src=riverside-jobs-weekly`);
      await fireEvent.input(within(sheet).getByLabelText(/Listing address/), { target: { value: 'https://jobs.example.org/123' } });
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Save posting' }));

      await waitFor(() =>
        expect(sent).toEqual({
          boardName: 'Riverside Jobs Weekly',
          sourceKey: 'riverside-jobs-weekly',
          listingUrl: 'https://jobs.example.org/123',
          postedOn: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
          endsOn: null,
        }),
      );
    });

    it("copies the job's text field by field, and says so when the browser gives no clipboard", async () => {
      const writeText = vi.fn().mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error('Not allowed'));
      vi.stubGlobal('navigator', Object.assign(Object.create(navigator), { clipboard: { writeText } }));
      stubReads([]);
      renderPanel({ id: jobId, status: 'Open', postings: [], ...fullJob });

      await waitFor(() => expect(screen.getByRole('button', { name: 'Post it yourself' })).toBeEnabled());
      await fireEvent.click(screen.getByRole('button', { name: 'Post it yourself' }));
      const sheet = await screen.findByRole('dialog', { name: 'Post it yourself' });

      await fireEvent.click(within(sheet).getByRole('button', { name: 'Copy title' }));
      await waitFor(() => expect(within(sheet).getByRole('button', { name: 'Copied title' })).toBeInTheDocument());
      expect(writeText).toHaveBeenCalledWith('Senior Engineer');

      // A second press moves the message to the button pressed; the title's goes back to its own words.
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Copy as plain text description' }));
      await waitFor(() => expect(within(sheet).getByRole('button', { name: 'Could not copy description' })).toBeInTheDocument());
      expect(within(sheet).getByRole('button', { name: 'Copy title' })).toBeInTheDocument();
    });

    it("refuses a board name that is one ScreenHire posts to itself before it is saved", async () => {
      stubReads([]);
      renderPanel({ id: jobId, status: 'Open', postings: [], ...fullJob });

      await waitFor(() => expect(screen.getByRole('button', { name: 'Post it yourself' })).toBeEnabled());
      await fireEvent.click(screen.getByRole('button', { name: 'Post it yourself' }));
      const sheet = await screen.findByRole('dialog', { name: 'Post it yourself' });
      await fireEvent.input(within(sheet).getByLabelText('Board'), { target: { value: 'Adzuna' } });

      expect(within(sheet).getByLabelText('Apply link for this board')).toHaveValue('');
      expect(sheet).toHaveTextContent('ScreenHire posts to this board itself.');
    });

    it('offers Post it yourself only on an open job, to someone who may edit it', async () => {
      stubReads([]);
      state.user = userAs('Viewer');
      const view = renderPanel({ id: jobId, status: 'Open', postings: [], ...fullJob });
      await waitFor(() => expect(item('careers')).toBeDefined());
      expect(screen.queryByRole('button', { name: 'Post it yourself' })).toBeNull();
      view.unmount();

      state.user = userAs('Owner');
      renderPanel({ id: jobId, status: 'Draft', postings: [], ...fullJob });
      expect(screen.queryByRole('button', { name: 'Post it yourself' })).toBeNull();
    });
  });
});
