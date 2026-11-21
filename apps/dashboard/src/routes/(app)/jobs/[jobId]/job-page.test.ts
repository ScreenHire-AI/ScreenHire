import type { JobDetail } from '@screenhire/api-client';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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
  location: 'Remote (United Kingdom, Ireland)',
  salary: { min: 70000, max: 90000, currency: 'GBP', period: 'Year' },
  validThrough: null,
  workflow: { stages, questionnaire: { questions: [] }, automations: [] },
  workflowVersion: 1,
  aiUse: { policy: 'AllowedWithDisclosure', override: null, organizationDefault: 'AllowedWithDisclosure' },
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

const organization = {
  id: '01a157d4-0000-7000-8000-0000000000bb',
  name: 'Brightwater Studio',
  slug: 'brightwater',
  careersUrl: 'https://jobs.brightwater.example.com',
  questionsPerPage: 8,
  embedOrigins: [] as string[],
};

function stub(detail: JobDetail, routes: Parameters<typeof apiStub>[0] = {}) {
  const fetchMock = vi.fn(
    apiStub({
      [`GET /v1/jobs/${jobId}`]: () => Response.json(detail),
      [`GET /v1/jobs/${jobId}/applications`]: () => Response.json(applications),
      'GET /v1/boards': () => Response.json(boards),
      'GET /v1/organization': () => Response.json(organization),
      'GET /v1/organization/members': () => Response.json([]),
      [`GET /v1/jobs/${jobId}/hiring-team`]: () => Response.json([]),
      [`GET /v1/jobs/${jobId}/publish-check`]: () => Response.json({ items: [] }),
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

beforeEach(() => {
  page.data = { user };
});

afterEach(() => {
  vi.unstubAllGlobals();
  page.state = {};
  page.data = {};
  (page as unknown as { url: URL }).url = new URL('http://dashboard.test/');
  navigation.goto.mockClear();
});

/** Opens the page on its Job postings tab, as the notice's link and a reload of that tab do. */
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

  it('keeps Opened once someone has changed the job since it opened', async () => {
    stub({ ...job, lastUpdated: { at: '2026-10-20T12:00:00Z', by: 'ScreenHire' } } as JobDetail);

    await screen.findByRole('heading', { level: 1 });
    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    expect(within(band).getAllByRole('term').map((term) => term.textContent)).toEqual(['Location', 'Type', 'Salary', 'Opened', 'Last updated']);
  });

  it('says the template version the job is on, and a newer one as its own fact with a link to what it changes', async () => {
    stub({
      ...job,
      template: { templateId: 't-1', templateName: 'Retail assistant', versionId: 'v-1', version: 5, latestPublishedVersion: 6, updateAvailable: true },
    } as JobDetail);

    await screen.findByRole('heading', { level: 1 });
    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    const facts = within(band).getAllByRole('definition').map((value) => value.textContent?.replace(/\s+/g, ' ').trim());
    expect(facts).toContain('Retail assistant v5');
    expect(facts).toContain('Retail assistant v6, See what changes');
    expect(within(band).getByRole('link', { name: 'See what changes' })).toHaveAttribute('href', `/jobs/${jobId}/workflow?review=update`);
  });

  it('heads the page with the title, its status beside it and a labelled facts row', async () => {
    stub(job);

    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Senior .NET Engineer');
    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    expect(within(band).getByText('Open')).toBeInTheDocument();
    const facts = within(band).getAllByRole('term').map((term) => term.textContent);
    // This fixture's last change is its opening, which Last updated says, so Opened is left out (W7).
    expect(facts).toEqual(['Location', 'Type', 'Salary', 'Last updated']);
    expect(within(band).getByRole('link', { name: 'Edit details' })).toHaveAttribute('href', `/jobs/${jobId}/edit`);
    expect(within(band).getByRole('link', { name: 'Edit pipeline' })).toHaveAttribute('href', `/jobs/${jobId}/workflow`);
    expect(within(band).queryByRole('button', { name: /^Publish/ })).not.toBeInTheDocument();
    // The work area first, with no side column beside it (S7).
    expect(await screen.findByRole('region', { name: 'Candidates' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Job postings' })).not.toBeInTheDocument();
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

  it('has a Candidates tab with its count and a Job postings tab whose count says how many boards the job went to (E2)', async () => {
    stub({ ...job, postings: [posting('careers', 'Careers page', { state: 'Live' })] });

    await waitFor(() => expect(tab(/^Candidates/)).toHaveTextContent(`Candidates ${applications.length}`));
    await waitFor(() => expect(tab(/^Job postings/)).toHaveAccessibleName(/^Job postings ?1$/));
    expect(tab(/^Candidates/)).toHaveAttribute('aria-selected', 'true');

    await fireEvent.mouseDown(tab(/^Job postings/));
    await fireEvent.click(tab(/^Job postings/));
    // The tab is in the address, changed without a navigation.
    await waitFor(() =>
      expect(navigation.goto).toHaveBeenCalledWith(expect.objectContaining({ search: '?tab=postings' }), expect.objectContaining({ shallow: true, replace: true })),
    );
  });

  it('opens the Job postings tab from the address, with the boards', async () => {
    onPostingsTab();
    stub(job);

    const distribution = await screen.findByRole('region', { name: 'Job postings' });
    expect(tab(/^Job postings/)).toHaveAttribute('aria-selected', 'true');
    await waitFor(() => expect(distribution.querySelector('[data-board=careers]')).not.toBeNull());
    expect(screen.queryByRole('region', { name: 'Candidates' })).not.toBeInTheDocument();
  });

  it('opens Job postings from its old address, ?tab=distribution', async () => {
    onPostingsTab('distribution');
    stub(job);

    expect(await screen.findByRole('region', { name: 'Job postings' })).toBeInTheDocument();
    expect(tab(/^Job postings/)).toHaveAttribute('aria-selected', 'true');
  });

  it('says a board did not take the posting, naming it, with the issue and a link to the fix (E2)', async () => {
    stub({ ...job, postings: [posting('careers', 'Careers page', { state: 'Live' }), posting('sandbox', 'Sandbox board', { state: 'Draft', issues: ['Sandbox board needs a city.'] })] });

    await waitFor(() => expect(document.querySelector('[data-slot=notice]')).not.toBeNull());
    const notice = document.querySelector<HTMLElement>('[data-slot=notice]')!;
    expect(notice).toHaveTextContent('Sandbox board did not take this job. Sandbox board needs a city.');
    expect(within(notice).getByRole('link', { name: 'Fix on Job postings' })).toHaveAttribute('href', '?tab=postings');
    await waitFor(() => expect(tab(/^Job postings/)).toHaveAccessibleName(/^Job postings ?1$/));
  });

  it("says a board refused the posting with the board's reason, and nothing while every posting is fine", async () => {
    stub({ ...job, postings: [posting('sandbox', 'Sandbox board', { state: 'Rejected', lastError: 'Title too long.' })] });

    await waitFor(() => expect(document.querySelector('[data-slot=notice]')).toHaveTextContent("Sandbox board refused this job's posting. Sandbox board: Title too long."));
  });

  it('keeps the notice on Job postings, linking to Edit details rather than to the tab it is on', async () => {
    onPostingsTab();
    stub({ ...job, postings: [posting('careers', 'Careers page'), posting('sandbox', 'Sandbox board', { state: 'Draft', issues: ['Sandbox board needs a city.'] })] });

    await waitFor(() => expect(document.querySelector('[data-slot=notice]')).toHaveTextContent('Sandbox board did not take this job. Sandbox board needs a city.'));
    const notice = document.querySelector<HTMLElement>('[data-slot=notice]')!;
    expect(within(notice).queryByRole('link', { name: 'Fix on Job postings' })).toBeNull();
    expect(within(notice).getByRole('link', { name: 'Edit details' })).toHaveAttribute('href', `/jobs/${jobId}/edit`);
  });

  it('shows no notice for a job that is not open, or whose postings are fine', async () => {
    stub(job);

    await screen.findByRole('region', { name: 'Candidates' });
    await waitFor(() => expect(tab(/^Job postings/)).toHaveAccessibleName(/^Job postings ?\d+$/));
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

  it('says where a published job went, with a link to Job postings, because the Publish button is gone', async () => {
    const publish = `/v1/jobs/${jobId}/publish`;
    stub({ ...job, status: 'Draft', publishedAt: null, postings: [] }, { [`POST ${publish}`]: () => Response.json({}) });

    await fireEvent.click(await screen.findByRole('button', { name: 'Publish to 2 boards' }));

    const message = await waitFor(() => {
      const found = document.querySelector<HTMLElement>('[data-published]');
      expect(found).toHaveTextContent('Published. It is on your careers page now.');
      return found!;
    });
    expect(within(message).getByRole('link', { name: 'See Job postings' })).toHaveAttribute('href', '?tab=postings');
  });

  it('drops the published line once the job is closed, because it is off the careers page then', async () => {
    let current = { ...job, status: 'Draft', publishedAt: null, postings: [] } as unknown as JobDetail;
    stub(current, {
      [`GET /v1/jobs/${jobId}`]: () => Response.json(current),
      [`GET /v1/jobs/${jobId}/applications`]: () => Response.json([]),
      [`POST /v1/jobs/${jobId}/publish`]: () => {
        current = job;
        return Response.json({});
      },
      [`POST /v1/jobs/${jobId}/close`]: () => {
        current = { ...job, status: 'Closed', closedAt: '2026-10-19T09:00:00Z' } as unknown as JobDetail;
        return Response.json({});
      },
    });
    const line = () => document.querySelector('[data-published]')?.textContent?.trim() ?? '';

    await fireEvent.click(await screen.findByRole('button', { name: 'Publish to 2 boards' }));
    await waitFor(() => expect(line()).toContain('Published.'));

    await fireEvent.click(within(await openMoreActions()).getByRole('menuitem', { name: 'Close & remove everywhere' }));
    const dialog = await screen.findByRole('dialog', { name: 'Close this job and remove it everywhere?' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Close & remove everywhere' }));

    await waitFor(() => expect(line()).toBe(''));
  });

  it('says nothing is published when the publish fails, and a draft tells the candidates list why it is empty', async () => {
    stub(
      { ...job, status: 'Draft', publishedAt: null, postings: [] },
      {
        [`GET /v1/jobs/${jobId}/applications`]: () => Response.json([]),
        [`POST /v1/jobs/${jobId}/publish`]: () => Response.json({ detail: 'The job changed meanwhile.' }, { status: 409 }),
      },
    );

    expect(await screen.findByText('Nobody can apply until you publish this job.')).toBeInTheDocument();
    await fireEvent.click(await screen.findByRole('button', { name: 'Publish to 2 boards' }));

    await waitFor(() => expect(screen.getByText('The job changed meanwhile.')).toBeInTheDocument());
    expect(document.querySelector('[data-published]')?.textContent?.trim() ?? '').toBe('');
  });

  it('puts a publish the plan refused in a full-width notice under the band, with the way to upgrade for an owner', async () => {
    const detail = 'Your free trial has ended, so no new job can be published. Everything else works as before and open jobs stay open. Ask an owner to choose a plan.';
    page.data = { user: userAs('Owner') };
    stub(
      { ...job, status: 'Draft', publishedAt: null, postings: [] },
      {
        [`POST /v1/jobs/${jobId}/publish`]: () =>
          Response.json({ type: 'urn:screenhire:problem:subscription-ended', title: 'Free trial ended', detail }, { status: 402 }),
      },
    );

    await fireEvent.click(await screen.findByRole('button', { name: 'Publish to 2 boards' }));

    const notice = (await screen.findByText(detail)).closest('[data-slot="notice"]') as HTMLElement;
    expect(within(notice).getByRole('link', { name: 'Upgrade plan' })).toHaveAttribute('href', '/settings/billing');
    expect(notice.closest('[data-slot="header-band"]')).toBeNull();
  });

  it('shows a closed job as closed, with no way to edit or close it again', async () => {
    stub({ ...job, status: 'Closed', closedAt: '2026-10-20T10:00:00Z' });

    await screen.findByRole('heading', { level: 1 });
    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    expect(within(band).getAllByText('Closed')).not.toHaveLength(0);
    expect(within(band).queryByRole('link', { name: 'Edit details' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument();
  });

  it('says a closed job is closed once: no Closed fact when closing was the last change', async () => {
    stub({ ...job, status: 'Closed', closedAt: '2026-10-20T10:00:00Z', lastUpdated: { at: '2026-10-20T10:00:20Z', by: 'Member', userId: user.id } } as JobDetail);

    await screen.findByRole('heading', { level: 1 });
    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    expect([...band.querySelectorAll('dt')].map((term) => term.textContent)).not.toContain('Closed');
    expect(within(band).getAllByText('Closed')).not.toHaveLength(0);
  });

  it('keeps the Closed fact when an edit came after closing', async () => {
    stub({ ...job, status: 'Closed', closedAt: '2026-10-20T10:00:00Z', lastUpdated: { at: '2026-10-22T10:00:00Z', by: 'Member', userId: user.id } } as JobDetail);

    await screen.findByRole('heading', { level: 1 });
    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    expect([...band.querySelectorAll('dt')].map((term) => term.textContent)).toContain('Closed');
  });

  it('says what saving the details did above the tabs, whichever one is open', async () => {
    page.state = { saved: 'Saved. The careers page shows it now.' };
    stub(job);

    await screen.findByRole('region', { name: 'Candidates' });
    expect(screen.getAllByRole('status').some((status) => status.textContent?.includes('Saved. The careers page shows it now'))).toBe(true);
  });

  describe('an edit the boards do not have yet', () => {
    const edited = { ...job, hasUnpublishedChanges: true, postings: [posting('careers', 'Careers page', { mode: 'Hosted' }), posting('sandbox', 'Sandbox board')] };
    const noticeText = 'Your changes are live on the careers page, not yet on the job boards.';

    it('says so on whichever tab is open, and Update boards sends the new version to the boards it is on', async () => {
      onPostingsTab();
      const fetchMock = stub(edited, { [`POST /v1/jobs/${jobId}/publish`]: () => Response.json(edited) });

      await screen.findByText(noticeText);
      await fireEvent.click(screen.getByRole('button', { name: 'Update boards' }));

      await waitFor(() => expect(screen.getAllByRole('status').some((status) => status.textContent?.includes('Sent your changes to the job boards.'))).toBe(true));
      const request = fetchMock.mock.calls.find(([url]) => url === `/v1/jobs/${jobId}/publish`);
      expect(request?.[1]).toEqual(expect.objectContaining({ method: 'POST' }));
      // No board named: the API sends it to every board the job is on.
      expect(request?.[1]?.body ?? null).toBeNull();
    });

    it('says why it failed, and keeps the notice', async () => {
      stub(edited, { [`POST /v1/jobs/${jobId}/publish`]: () => Response.json({ detail: 'The job changed meanwhile.' }, { status: 409 }) });

      await fireEvent.click(await screen.findByRole('button', { name: 'Update boards' }));

      await waitFor(() => expect(screen.getAllByRole('alert').some((alert) => alert.textContent?.includes('The job changed meanwhile.'))).toBe(true));
      expect(screen.getByText(noticeText)).toBeInTheDocument();
    });

    it('stays quiet with no change, for someone who may not edit jobs, and when no board has the job', async () => {
      stub({ ...edited, hasUnpublishedChanges: false });
      await screen.findByRole('region', { name: 'Candidates' });
      await waitFor(() => expect(tab(/^Job postings/)).toHaveAccessibleName(/^Job postings ?\d+$/));
      expect(screen.queryByText(noticeText)).not.toBeInTheDocument();
      cleanup();

      page.data = { user: userAs('Viewer') };
      stub(edited);
      await screen.findByRole('region', { name: 'Candidates' });
      await waitFor(() => expect(tab(/^Job postings/)).toHaveAccessibleName(/^Job postings ?\d+$/));
      expect(screen.queryByText(noticeText)).not.toBeInTheDocument();
      cleanup();

      page.data = { user };
      stub({ ...edited, postings: [posting('careers', 'Careers page', { mode: 'Hosted' })] });
      await screen.findByRole('region', { name: 'Candidates' });
      await waitFor(() => expect(tab(/^Job postings/)).toHaveAccessibleName(/^Job postings ?\d+$/));
      expect(screen.queryByText(noticeText)).not.toBeInTheDocument();
    });
  });

  it("says why publishing failed where the person is, not only on the Job postings tab", async () => {
    stub({ ...job, status: 'Draft', publishedAt: null, postings: [] }, { [`POST /v1/jobs/${jobId}/publish`]: () => Response.json({ detail: 'The job changed meanwhile.' }, { status: 409 }) });

    await fireEvent.click(await screen.findByRole('button', { name: 'Publish to 2 boards' }));

    await waitFor(() => expect(screen.getAllByRole('alert').some((alert) => alert.textContent?.includes('The job changed meanwhile.'))).toBe(true));
    expect(screen.getByRole('region', { name: 'Candidates' })).toBeInTheDocument();
  });

  it('shows the failure and a retry when the job boards do not load, never "no boards"', async () => {
    onPostingsTab();
    const fetchMock = stub(job, { 'GET /v1/boards': () => new Response(null, { status: 503 }) });

    const distribution = await screen.findByRole('region', { name: 'Job postings' });
    await waitFor(() => expect(within(distribution).getByRole('button', { name: /try again/i })).toBeInTheDocument());
    expect(distribution).not.toHaveTextContent('No job boards are set up.');

    fetchMock.mockImplementation(apiStub({ 'GET /v1/boards': () => Response.json(boards) }));
    await fireEvent.click(within(distribution).getByRole('button', { name: /try again/i }));
    // The loaded panel replaces the error one.
    await waitFor(() => expect(screen.getByRole('region', { name: 'Job postings' }).querySelector('[data-board=careers]')).not.toBeNull());
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
      'Edit pipeline',
      'Add candidate',
      'Edit hiring team',
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

  it('offers no Edit details, Close or Publish to a role that cannot edit jobs (A3)', async () => {
    page.data = { user: userAs('Viewer') };
    stub(job);
    await screen.findByRole('button', { name: /Hiring team/ });
    expect(screen.queryByRole('link', { name: 'Edit details' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument();
    cleanup();

    const fetchMock = stub({ ...job, status: 'Draft', publishedAt: null, postings: [] });
    await screen.findByRole('button', { name: /Hiring team/ });
    // Publish needs the boards, so wait for them: without them it would be missing for everyone.
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/v1/boards', expect.anything()));
    await expect(screen.findByRole('button', { name: /^Publish/ }, { timeout: 200 })).rejects.toThrow();
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

  describe('embed code for this job', () => {
    it('shows the snippet with the job address once a site may embed the careers page', async () => {
      onPostingsTab();
      stub(job, { 'GET /v1/organization': () => Response.json({ ...organization, embedOrigins: ['https://www.brightwater.example.com'] }) });

      expect(await screen.findByLabelText('Embed code for this job')).toHaveTextContent(
        '<script src="https://jobs.brightwater.example.com/embed.js" data-job="senior-dotnet-engineer" async></script>',
      );
      expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument();
    });

    it('sends someone with no listed site to add one, instead of showing code that would not work', async () => {
      onPostingsTab();
      stub(job);

      expect(await screen.findByText(/Add your website on/)).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'Careers page settings' })).toHaveAttribute('href', '/settings/careers-site');
      expect(screen.queryByLabelText('Embed code for this job')).toBeNull();
    });

    it('says the code could not load, with a way to try again, when the organization fails to load', async () => {
      onPostingsTab();
      stub(job, { 'GET /v1/organization': () => Response.json({ title: 'Server error' }, { status: 500 }) });

      expect(await screen.findByText(/Could not load the embed code/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    });

    it('is not offered for a job that is not open', async () => {
      onPostingsTab();
      stub({ ...job, status: 'Closed', closedAt: '2026-10-20T10:00:00Z' });

      expect((await screen.findAllByText('Careers page')).length).toBeGreaterThan(0);
      expect(screen.queryByRole('heading', { name: 'Embed on your website' })).toBeNull();
    });
  });

  describe('check before publishing', () => {
    const publish = `/v1/jobs/${jobId}/publish`;
    const checkPath = `/v1/jobs/${jobId}/publish-check`;
    const draft = { ...job, status: 'Draft', publishedAt: null, postings: [] } as unknown as JobDetail;
    const items = [
      { kind: 'TemplatePrompt', field: 'Description', text: 'The description still has the template\'s fill-in text, "Describe the work, the office and the team."', boards: [] },
      { kind: 'NoCity', field: 'City', text: 'Talent.com needs a city for an on-site or hybrid job.', boards: ['Talent.com'] },
    ];
    const routes = (body: unknown[] | 'fails') => ({
      [`GET ${checkPath}`]: () => (body === 'fails' ? Response.json({ title: 'Server error' }, { status: 500 }) : Response.json({ items: body })),
      [`POST ${publish}`]: () => Response.json({}),
    });
    const posted = (fetchMock: ReturnType<typeof stub>) => fetchMock.mock.calls.some(([url, init]) => url === publish && init?.method === 'POST');

    it('publishes at once when there is nothing to check', async () => {
      const fetchMock = stub(draft, routes([]));

      await fireEvent.click(await screen.findByRole('button', { name: 'Publish to 2 boards' }));

      await waitFor(() => expect(posted(fetchMock)).toBe(true));
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(screen.queryByText(/to check before publishing/)).not.toBeInTheDocument();
    });

    it('lists each problem with the field that fixes it, and publishes only on Publish anyway', async () => {
      const fetchMock = stub(draft, routes(items));

      await fireEvent.click(await screen.findByRole('button', { name: 'Publish to 2 boards' }));

      const dialog = await screen.findByRole('dialog', { name: '2 things to check before publishing' });
      expect(within(dialog).getByText(/fill-in text/)).toBeInTheDocument();
      expect(within(dialog).getByText('Talent.com needs a city for an on-site or hybrid job.')).toBeInTheDocument();
      expect(within(dialog).getByRole('link', { name: 'Edit details, description' })).toHaveAttribute('href', `/jobs/${jobId}/edit?focus=description`);
      expect(within(dialog).getByRole('link', { name: 'Edit details, city' })).toHaveAttribute('href', `/jobs/${jobId}/edit?focus=city`);
      // The way out goes to the first problem's field.
      expect(within(dialog).getByRole('link', { name: 'Edit details' })).toHaveAttribute('href', `/jobs/${jobId}/edit?focus=description`);
      expect(posted(fetchMock)).toBe(false);

      await fireEvent.click(within(dialog).getByRole('button', { name: 'Publish anyway' }));

      await waitFor(() => expect(posted(fetchMock)).toBe(true));
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    });

    it('does not stand in the way when the check cannot be read', async () => {
      const fetchMock = stub(draft, routes('fails'));

      await fireEvent.click(await screen.findByRole('button', { name: 'Publish to 2 boards' }));

      await waitFor(() => expect(posted(fetchMock)).toBe(true));
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('says in the header band, quietly, how many things to check while the job is a draft', async () => {
      const fetchMock = stub(draft, routes(items));

      const line = await screen.findByText('2 things to check before publishing');
      expect(posted(fetchMock)).toBe(false);

      // Underlined or bordered, a line that opens a dialog would break A5: it is plain text, and Publish opens the list.
      expect(line.tagName).toBe('SPAN');
      expect(line.closest('button, a')).toBeNull();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('says "1 thing" for one, and nothing at all for a job that is open or a role that cannot publish', async () => {
      stub(draft, routes([items[1]]));
      expect(await screen.findByText('1 thing to check before publishing')).toBeInTheDocument();
      cleanup();

      const open = stub(job, routes(items));
      await screen.findByRole('button', { name: /Hiring team/ });
      expect(open.mock.calls.some(([url]) => url === checkPath)).toBe(false);
      expect(screen.queryByText(/to check before publishing/)).not.toBeInTheDocument();
      cleanup();

      page.data = { user: userAs('Viewer') };
      const viewer = stub(draft, routes(items));
      await screen.findByRole('button', { name: /Hiring team/ });
      await waitFor(() => expect(viewer.mock.calls.some(([url]) => url === '/v1/boards')).toBe(true));
      expect(viewer.mock.calls.some(([url]) => url === checkPath)).toBe(false);
      expect(screen.queryByText(/to check before publishing/)).not.toBeInTheDocument();
    });
  });
});
