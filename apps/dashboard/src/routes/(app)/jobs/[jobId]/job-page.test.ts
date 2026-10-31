import type { JobDetail } from '@screenhire/api-client';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { page } from '$app/state';
import { apiStub, applications, board, jobId, posting, stages, user, userAs } from '../../../../test-support/fixtures.js';
import { stubViewport } from '../../../../test-support/viewport.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { state: {}, data: {}, url: new URL('http://dashboard.test/'), shallow: null }, navigating: { to: null } }));
const navigation = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: navigation.goto }));

const job = {
  id: jobId,
  title: 'Senior .NET Engineer',
  slug: 'senior-dotnet-engineer',
  status: 'Open',
  description: '<p>Build things.</p>',
  department: 'Engineering',
  employmentType: 'FullTime',
  workplaceType: 'Remote',
  locations: [],
  applicantLocationRequirements: ['GB', 'IE'],
  location: 'Remote (GB, IE)',
  salary: { min: 70000, max: 90000, currency: 'GBP', period: 'Year' },
  validThrough: null,
  workflow: { stages, questionnaire: { questions: [] }, verificationPolicy: { aiUse: 'Allowed' }, automations: [] },
  workflowVersion: 1,
  template: null,
  publishedVersion: 1,
  hasUnpublishedChanges: false,
  createdAt: '2026-10-18T09:00:00Z',
  updatedAt: null,
  publishedAt: '2026-10-18T12:00:00Z',
  closedAt: null,
  postings: [posting('careers', 'Careers page'), posting('sandbox', 'Sandbox board')],
  lastUpdated: { at: '2026-10-18T12:00:00Z', by: 'ScreenHire' },
} as unknown as JobDetail;

const boards = [board('careers', 'Careers page', { canUnpublishPerBoard: false }), board('sandbox', 'Sandbox board')];

function stub(detail: JobDetail, routes: Parameters<typeof apiStub>[0] = {}) {
  const fetchMock = vi.fn(
    apiStub({
      [`GET /v1/jobs/${jobId}`]: () => Response.json(detail),
      [`GET /v1/jobs/${jobId}/applications`]: () => Response.json(applications),
      'GET /v1/boards': () => Response.json(boards),
      'GET /v1/organization/members': () => Response.json([]),
      [`GET /v1/jobs/${jobId}/hiring-team`]: () => Response.json([]),
      ...routes,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: { params: { jobId } } } });
  return fetchMock;
}

async function openMoreActions() {
  const trigger = await screen.findByRole('button', { name: 'More actions' });
  // bits-ui opens a menu on pointerdown.
  await fireEvent.pointerDown(trigger, { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
}

afterEach(() => {
  vi.unstubAllGlobals();
  page.state = {};
  page.data = {};
  (page as unknown as { url: URL }).url = new URL('http://dashboard.test/');
  navigation.goto.mockClear();
});

/** Opens the page on its Postings tab, as the notice's link and a reload of that tab do. */
function onPostingsTab(tabValue = 'postings') {
  (page as unknown as { url: URL }).url = new URL(`http://dashboard.test/jobs/${jobId}?tab=${tabValue}`);
}

const tab = (name: RegExp) => screen.getByRole('tab', { name });

describe('job page', () => {
  it('opens the hiring team from the header, with its count, and saves the people chosen', async () => {
    const jordan = { id: 'm-2', userId: 'u-2', email: 'jordan@example.com', displayName: 'Jordan Ellis', role: 'Recruiter', customRoleId: null, roleName: 'Recruiter', joinedAt: '2026-10-01T09:00:00Z', jobAccess: 'All', seesAllJobs: true, jobCount: 1 };
    const robin = { id: 'm-3', userId: 'u-3', email: 'robin@example.com', displayName: 'Robin Hale', role: 'Viewer', customRoleId: null, roleName: 'Viewer', joinedAt: '2026-10-01T09:00:00Z', jobAccess: 'HiringTeamOnly', seesAllJobs: false, jobCount: 0 };
    const saved: unknown[] = [];
    // Someone who edits jobs, and so the team.
    page.data = { user };
    stub(job, {
      'GET /v1/organization/members': () => Response.json([jordan, robin]),
      'GET /v1/jobs': () => Response.json([]),
      [`GET /v1/jobs/${jobId}/hiring-team`]: () =>
        Response.json([{ memberId: 'm-2', userId: 'u-2', email: 'jordan@example.com', displayName: 'Jordan Ellis', roleName: 'Recruiter', seesAllJobs: true }]),
      [`PUT /v1/jobs/${jobId}/hiring-team`]: (init) => (saved.push(JSON.parse(String(init?.body))), Response.json([])),
    });

    const button = await screen.findByRole('button', { name: /^Hiring team/ });
    await waitFor(() => expect(button).toHaveTextContent('1'));
    await fireEvent.click(button);
    const sheet = await screen.findByRole('dialog', { name: 'Hiring team' });
    expect(await within(sheet).findByText('Jordan Ellis')).toBeInTheDocument();

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Remove Jordan Ellis from the hiring team' }));
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(saved).toEqual([{ memberIds: [], copyFromJobId: null }]));
  });

  it('heads the page with the title, its status beside it and a labelled facts row', async () => {
    stub(job);

    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Senior .NET Engineer');
    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    expect(within(band).getByText('Open')).toBeInTheDocument();
    const facts = within(band).getAllByRole('term').map((term) => term.textContent);
    expect(facts).toEqual(['Location', 'Type', 'Salary', 'Opened', 'Last updated']);
    expect(within(band).getByRole('link', { name: 'Edit details' })).toHaveAttribute('href', `/jobs/${jobId}/edit`);
    expect(within(band).getByRole('link', { name: 'Edit workflow' })).toHaveAttribute('href', `/jobs/${jobId}/workflow`);
    expect(within(band).queryByRole('button', { name: /^Publish/ })).not.toBeInTheDocument();
    // The work area first, with no side column beside it (S7).
    expect(await screen.findByRole('region', { name: 'Pipeline' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Postings' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Selected candidate' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Authenticity' })).not.toBeInTheDocument();
  });

  it("says a draft's creation once: as its last fact while nothing has changed it, else as its own Created fact (W7)", async () => {
    const draft = { ...job, status: 'Draft', publishedAt: null } as JobDetail;
    stub({ ...draft, lastUpdated: { at: job.createdAt, by: 'Member', userId: user.id, created: true } } as JobDetail);
    await screen.findByRole('heading', { level: 1 });
    const terms = () => within(document.querySelector<HTMLElement>('[data-slot=header-band]')!).getAllByRole('term').map((term) => term.textContent);
    expect(terms()).toEqual(['Location', 'Type', 'Salary', 'Created']);
    cleanup();

    stub(draft);
    await screen.findByRole('heading', { level: 1 });
    expect(terms()).toEqual(['Location', 'Type', 'Salary', 'Created', 'Last updated']);
  });

  it('has a Candidates tab with its count and a Postings tab whose count says how many boards the job is on (E2)', async () => {
    stub({ ...job, postings: [posting('careers', 'Careers page', { state: 'Live' })] });

    await waitFor(() => expect(tab(/^Candidates/)).toHaveTextContent(`Candidates ${applications.length}`));
    await waitFor(() => expect(tab(/^Postings/)).toHaveAccessibleName(/^Postings ?posted on 1 of 2 boards$/));
    expect(tab(/^Candidates/)).toHaveAttribute('aria-selected', 'true');

    await fireEvent.mouseDown(tab(/^Postings/));
    await fireEvent.click(tab(/^Postings/));
    // The tab is in the address, changed without a navigation.
    await waitFor(() =>
      expect(navigation.goto).toHaveBeenCalledWith(expect.objectContaining({ search: '?tab=postings' }), expect.objectContaining({ shallow: true, replace: true })),
    );
  });

  it('opens the Postings tab from the address, with the boards', async () => {
    onPostingsTab();
    stub(job);

    const distribution = await screen.findByRole('region', { name: 'Postings' });
    expect(tab(/^Postings/)).toHaveAttribute('aria-selected', 'true');
    await waitFor(() => expect(distribution.querySelector('[data-board=careers]')).not.toBeNull());
    expect(screen.queryByRole('region', { name: 'Pipeline' })).not.toBeInTheDocument();
  });

  it('opens Postings from its old address, ?tab=distribution', async () => {
    onPostingsTab('distribution');
    stub(job);

    expect(await screen.findByRole('region', { name: 'Postings' })).toBeInTheDocument();
    expect(tab(/^Postings/)).toHaveAttribute('aria-selected', 'true');
  });

  it('says a board did not take the posting, naming it, with the issue and a link to the fix (E2)', async () => {
    stub({ ...job, postings: [posting('careers', 'Careers page', { state: 'Live' }), posting('sandbox', 'Sandbox board', { state: 'Draft', issues: ['Sandbox board needs a city.'] })] });

    await waitFor(() => expect(document.querySelector('[data-slot=notice]')).not.toBeNull());
    const notice = document.querySelector<HTMLElement>('[data-slot=notice]')!;
    expect(notice).toHaveTextContent('Sandbox board did not take this job. Sandbox board needs a city.');
    expect(within(notice).getByRole('link', { name: 'Fix on Postings' })).toHaveAttribute('href', '?tab=postings');
    await waitFor(() => expect(tab(/^Postings/)).toHaveAccessibleName(/^Postings ?posted on 1 of 2 boards$/));
  });

  it("says a board refused the posting with the board's reason, and nothing while every posting is fine", async () => {
    stub({ ...job, postings: [posting('sandbox', 'Sandbox board', { state: 'Rejected', lastError: 'Title too long.' })] });

    await waitFor(() => expect(document.querySelector('[data-slot=notice]')).toHaveTextContent("Sandbox board refused this job's posting. Sandbox board: Title too long."));
  });

  it('shows no notice for a job that is not open, or whose postings are fine', async () => {
    stub(job);

    await screen.findByRole('region', { name: 'Pipeline' });
    await waitFor(() => expect(tab(/^Postings/)).toHaveAccessibleName(/posted on/));
    expect(document.querySelector('[data-slot=notice]')).toBeNull();
  });

  it('closes the job through the More menu, only after the confirmation', async () => {
    const close = `/v1/jobs/${jobId}/close`;
    const fetchMock = stub(job, { [`POST ${close}`]: () => Response.json({}) });

    const menu = await openMoreActions();
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Close & remove everywhere' }));
    const dialog = await screen.findByRole('dialog', { name: 'Close this job and remove it everywhere?' });
    expect(fetchMock).not.toHaveBeenCalledWith(close, expect.anything());

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Close & remove everywhere' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(close, expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('keeps the job open when the person backs out, and says why closing failed', async () => {
    const close = `/v1/jobs/${jobId}/close`;
    const fetchMock = stub(job, { [`POST ${close}`]: () => Response.json({ detail: 'The job changed meanwhile.' }, { status: 409 }) });

    await fireEvent.click(within(await openMoreActions()).getByRole('menuitem', { name: 'Close & remove everywhere' }));
    let dialog = await screen.findByRole('dialog');
    // The dialog takes focus once the menu has closed (the end-to-end test checks it goes back to the button after).
    await waitFor(() => expect(dialog).toContainElement(document.activeElement as HTMLElement));
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Keep it open' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(fetchMock).not.toHaveBeenCalledWith(close, expect.anything());

    await fireEvent.click(within(await openMoreActions()).getByRole('menuitem', { name: 'Close & remove everywhere' }));
    dialog = await screen.findByRole('dialog');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Close & remove everywhere' }));

    await waitFor(() => expect(within(dialog).getByRole('alert')).toHaveTextContent('The job changed meanwhile.'));
  });

  it("makes publishing a draft the page's one primary action", async () => {
    const publish = `/v1/jobs/${jobId}/publish`;
    const fetchMock = stub({ ...job, status: 'Draft', publishedAt: null, postings: [] }, { [`POST ${publish}`]: () => Response.json({}) });

    await fireEvent.click(await screen.findByRole('button', { name: 'Publish to 2 boards' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(publish, expect.objectContaining({ method: 'POST' })));
    expect(fetchMock.mock.calls.find(([url]) => url === publish)![1]!.body).toBeUndefined();
    // A draft has nothing to close, so on a wide screen there is no More menu.
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument();
  });

  it('shows a closed job as closed, with no way to edit or close it again', async () => {
    stub({ ...job, status: 'Closed', closedAt: '2026-10-20T10:00:00Z' });

    await screen.findByRole('heading', { level: 1 });
    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    expect(within(band).getAllByText('Closed')).not.toHaveLength(0);
    expect(within(band).queryByRole('link', { name: 'Edit details' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument();
  });

  it('says what saving the details did above the tabs, whichever one is open', async () => {
    page.state = { saved: 'Saved. The careers page shows it now; publish again to update the boards.' };
    stub(job);

    await screen.findByRole('region', { name: 'Pipeline' });
    expect(screen.getAllByRole('status').some((status) => status.textContent?.includes('Saved. The careers page shows it now'))).toBe(true);
  });

  it("says why publishing failed where the person is, not only on the Postings tab", async () => {
    stub({ ...job, status: 'Draft', publishedAt: null, postings: [] }, { [`POST /v1/jobs/${jobId}/publish`]: () => Response.json({ detail: 'The job changed meanwhile.' }, { status: 409 }) });

    await fireEvent.click(await screen.findByRole('button', { name: 'Publish to 2 boards' }));

    await waitFor(() => expect(screen.getAllByRole('alert').some((alert) => alert.textContent?.includes('The job changed meanwhile.'))).toBe(true));
    expect(screen.getByRole('region', { name: 'Pipeline' })).toBeInTheDocument();
  });

  it('shows the failure and a retry when the job boards do not load, never "no boards"', async () => {
    onPostingsTab();
    const fetchMock = stub(job, { 'GET /v1/boards': () => new Response(null, { status: 503 }) });

    const distribution = await screen.findByRole('region', { name: 'Postings' });
    await waitFor(() => expect(within(distribution).getByRole('button', { name: /try again/i })).toBeInTheDocument());
    expect(distribution).not.toHaveTextContent('No job boards are set up.');

    fetchMock.mockImplementation(apiStub({ 'GET /v1/boards': () => Response.json(boards) }));
    await fireEvent.click(within(distribution).getByRole('button', { name: /try again/i }));
    // The loaded panel replaces the error one.
    await waitFor(() => expect(screen.getByRole('region', { name: 'Postings' }).querySelector('[data-board=careers]')).not.toBeNull());
  });

  it('says when the job does not exist', async () => {
    stub(job, { [`GET /v1/jobs/${jobId}`]: () => Response.json({ title: 'Not Found' }, { status: 404 }) });

    expect(await screen.findByText('It does not exist, or it belongs to another organization.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Job not found');
    expect(screen.getByRole('link', { name: 'Back to jobs' })).toHaveAttribute('href', '/');
  });

  it('folds the secondary actions into More on a phone', async () => {
    stubViewport(360);
    stub(job);

    const menu = await openMoreActions();
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual([
      'Edit details',
      'Edit workflow',
      'Hiring team',
      'Close & remove everywhere',
    ]);
    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    expect(within(band).queryByRole('link', { name: 'Edit details' })).not.toBeInTheDocument();
  });

  it('offers Add candidate as an outline button for this job to a role that adds candidates', async () => {
    page.data = { user };
    stub(job);

    const add = await screen.findByRole('button', { name: 'Add candidate' });
    expect(add.closest('[data-slot=header-band]')).not.toBeNull();
    // Publish is the page's primary (A1), so this one is outline.
    expect(add).toHaveClass('bg-card');
    expect(add).not.toHaveClass('bg-primary');
    await fireEvent.click(add);
    const sheet = await screen.findByRole('dialog', { name: 'Add candidate' });
    expect(sheet).toHaveTextContent('They join Senior .NET Engineer in its first stage.');
    expect(within(sheet).queryByLabelText('Job')).toBeNull();
  });

  it('has no Add candidate for a role that cannot add candidates (A3)', async () => {
    page.data = { user: userAs('Viewer') };
    stub(job);
    await screen.findByRole('button', { name: /Hiring team/ });
    expect(screen.queryByRole('button', { name: 'Add candidate' })).not.toBeInTheDocument();
  });

  it('has no Add candidate on a closed job, which takes no applications', async () => {
    page.data = { user };
    stub({ ...job, status: 'Closed', closedAt: '2026-10-20T10:00:00Z' });
    await screen.findByRole('button', { name: /Hiring team/ });
    expect(screen.queryByRole('button', { name: 'Add candidate' })).not.toBeInTheDocument();
  });

  it('puts Add candidate in More on a phone', async () => {
    page.data = { user };
    stubViewport(360);
    stub(job);

    const menu = await openMoreActions();
    expect(within(menu).getByRole('menuitem', { name: 'Add candidate' })).toBeInTheDocument();
  });

  it("puts a draft's publish in the action bar on a phone", async () => {
    stubViewport(360);
    stub({ ...job, status: 'Draft', publishedAt: null, postings: [] });

    const publish = await screen.findByRole('button', { name: 'Publish to 2 boards' });
    expect(publish.closest('[data-slot=action-bar]')).not.toBeNull();
    expect(publish.closest('[data-slot=header-band]')).toBeNull();
  });
});
