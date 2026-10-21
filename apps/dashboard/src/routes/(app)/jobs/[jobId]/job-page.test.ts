import type { JobDetail } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { page } from '$app/state';
import { apiStub, applications, board, jobId, posting, stages } from '../../../../test-support/fixtures.js';
import { stubViewport } from '../../../../test-support/viewport.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { state: {}, url: new URL('http://dashboard.test/') }, navigating: { to: null } }));

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
  workflow: { stages, questionnaire: { questions: [] }, integrityPolicy: { aiUse: 'Allowed' }, automations: [] },
  workflowVersion: 1,
  template: null,
  publishedVersion: 1,
  hasUnpublishedChanges: false,
  createdAt: '2026-10-18T09:00:00Z',
  updatedAt: null,
  publishedAt: '2026-10-18T12:00:00Z',
  closedAt: null,
  postings: [posting('careers', 'Careers page'), posting('sandbox', 'Sandbox board')],
} as unknown as JobDetail;

const boards = [board('careers', 'Careers page', { canUnpublishPerBoard: false }), board('sandbox', 'Sandbox board')];

function stub(detail: JobDetail, routes: Parameters<typeof apiStub>[0] = {}) {
  const fetchMock = vi.fn(
    apiStub({
      [`GET /v1/jobs/${jobId}`]: () => Response.json(detail),
      [`GET /v1/jobs/${jobId}/applications`]: () => Response.json(applications),
      'GET /v1/boards': () => Response.json(boards),
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
});

describe('job page', () => {
  it('heads the page with the title, its status beside it and a labelled facts row', async () => {
    stub(job);

    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Senior .NET Engineer');
    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    expect(within(band).getByText('Open')).toBeInTheDocument();
    const facts = within(band).getAllByRole('term').map((term) => term.textContent);
    expect(facts).toEqual(['Location', 'Type', 'Salary', 'Opened']);
    expect(within(band).getByRole('link', { name: 'Edit details' })).toHaveAttribute('href', `/jobs/${jobId}/edit`);
    expect(within(band).getByRole('link', { name: 'Workflow' })).toHaveAttribute('href', `/jobs/${jobId}/workflow`);
    expect(within(band).queryByRole('button', { name: /^Publish/ })).not.toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Distribution' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Pipeline' })).toBeInTheDocument();
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

  it('says what saving the details did, in the Distribution panel', async () => {
    page.state = { saved: 'Saved. The careers page shows it now; publish again to update the boards.' };
    stub(job);

    const distribution = await screen.findByRole('region', { name: 'Distribution' });
    await waitFor(() => expect(within(distribution).getByRole('status')).toHaveTextContent('Saved. The careers page shows it now'));
  });

  it('shows the failure and a retry when the job boards do not load, never "no boards"', async () => {
    const fetchMock = stub(job, { 'GET /v1/boards': () => new Response(null, { status: 503 }) });

    const distribution = await screen.findByRole('region', { name: 'Distribution' });
    await waitFor(() => expect(within(distribution).getByRole('button', { name: /try again/i })).toBeInTheDocument());
    expect(distribution).not.toHaveTextContent('No job boards are set up.');

    fetchMock.mockImplementation(apiStub({ 'GET /v1/boards': () => Response.json(boards) }));
    await fireEvent.click(within(distribution).getByRole('button', { name: /try again/i }));
    // The loaded panel replaces the error one.
    await waitFor(() => expect(screen.getByRole('region', { name: 'Distribution' }).querySelector('[data-board=careers]')).not.toBeNull());
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
      'Workflow',
      'Close & remove everywhere',
    ]);
    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    expect(within(band).queryByRole('link', { name: 'Edit details' })).not.toBeInTheDocument();
  });
});
