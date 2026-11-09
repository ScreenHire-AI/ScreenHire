import type { HiringWorkflow, JobDetail, JobStatus } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobId, user } from '../../../../../test-support/fixtures.js';
import { stubViewport } from '../../../../../test-support/viewport.js';
import WithQueryClient from '../../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

const { pageState, goto } = vi.hoisted(() => ({
  pageState: { data: { user: undefined as unknown }, url: new URL('http://dashboard.test/') },
  goto: vi.fn(async (..._args: unknown[]) => {}),
}));
vi.mock('$app/state', () => ({ page: pageState, navigating: { to: null } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto }));

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone-screen', name: 'Phone screen', kind: 'Screening', slaDays: 3 },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: { questions: [] },
  automations: [],
};

// Only what the page reads from the job.
const jobIn = (status: JobStatus) =>
  ({
    id: jobId,
    title: 'Senior .NET Engineer',
    status,
    workflow,
    workflowVersion: 3,
    aiUse: { policy: 'NotAllowed', override: 'NotAllowed', organizationDefault: 'AllowedWithDisclosure' },
    template: null,
    lastUpdated: { at: '2026-10-18T12:00:00Z', by: 'Avery Quinn', userId: user.id, apiKeyId: null },
  }) as unknown as JobDetail;

function stubApi(status: JobStatus) {
  const fetchMock = vi.fn(
    apiStub({
      [`GET /v1/jobs/${jobId}`]: () => Response.json(jobIn(status)),
      'POST /v1/workflows/validate': () => Response.json({ errors: [] }),
      'GET /v1/organization/members': () => Response.json([]),
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

const renderPage = () => render(WithQueryClient, { props: { component: Page, props: { params: { jobId } } } });

// The tab is in the address (?tab=), which the page reads and a test sets; no tab is the first, Overview.
const atTab = (tab?: string) => {
  pageState.data = { user };
  pageState.url = new URL(tab ? `http://dashboard.test/?tab=${tab}` : 'http://dashboard.test/');
};

beforeEach(() => {
  goto.mockClear();
  atTab();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('job workflow page', () => {
  it('reads a closed job as content: the summary, the Closed status, no controls and no Save button', async () => {
    atTab('pipeline');
    stubApi('Closed');
    const { container } = renderPage();

    const pane = await screen.findByRole('tabpanel', { name: 'Pipeline' });
    expect(within(pane).getByRole('region', { name: 'Stages' })).toBeInTheDocument();
    expect(pane.querySelectorAll('input, select, textarea')).toHaveLength(0);
    expect(container.querySelectorAll(':disabled')).toHaveLength(0);
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Senior .NET Engineer workflow');
    expect(screen.getByText('Closed')).toHaveAttribute('data-slot', 'badge');
    expect(screen.queryByRole('button', { name: 'Save workflow' })).toBeNull();
  });

  it('opens on Overview, the first tab, where a closed job has its stages as text and nothing opens', async () => {
    stubApi('Closed');
    renderPage();

    const pane = await screen.findByRole('tabpanel', { name: 'Overview' });
    expect(within(pane).getByRole('region', { name: 'Candidate journey' })).toBeInTheDocument();
    expect(within(pane).getByRole('list', { name: 'Stages in order' })).toHaveTextContent('Phone screen');
    expect(within(pane).queryByRole('button')).toBeNull();
  });

  it('shows the builder with a Save button for an open job, whose status is the first badge beside the title', async () => {
    atTab('pipeline');
    stubApi('Open');
    renderPage();

    const stages = await screen.findByRole('region', { name: 'Stages' });
    // The stages are cards; the time limit is in the stage's sheet.
    expect(within(stages).getByRole('group', { name: 'Stage 2: Phone screen' })).toHaveTextContent('3-day limit');
    expect(within(stages).queryByRole('spinbutton')).toBeNull();
    expect(screen.getByRole('button', { name: 'Save workflow' })).toBeEnabled();
    expect(screen.getByText('Open')).toHaveAttribute('data-slot', 'badge');
    expect(screen.queryByText('Closed')).toBeNull();
  });

  it('puts Save workflow in the action bar on a phone (section 8)', async () => {
    stubViewport(360);
    stubApi('Open');
    renderPage();

    const save = await screen.findByRole('button', { name: 'Save workflow' });
    expect(save.closest('[data-slot=action-bar]')).not.toBeNull();
    expect(document.querySelector('[data-slot=header-band]')).not.toContainElement(save);
  });

  it('sends nothing and says so when Save is pressed with no changes', async () => {
    atTab('pipeline');
    const fetchMock = stubApi('Open');
    renderPage();
    await screen.findByRole('region', { name: 'Stages' });
    await waitFor(() => expect(screen.getByRole('region', { name: 'Validation' })).toHaveAttribute('aria-busy', 'false'));

    await fireEvent.click(screen.getByRole('button', { name: 'Save workflow' }));

    // The message sits in the band's line under the title, so it never pushes the actions onto a second row (S8).
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('No changes to save.'));
    expect(screen.getByRole('status').closest('[data-slot=header-band]')).not.toBeNull();
    expect(screen.getByRole('button', { name: 'Save workflow' }).parentElement).not.toContainElement(screen.getByRole('status'));
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'PUT')).toHaveLength(0);
  });

  it("shows the policy the job asks under on the Questions tab, with a link to the job's own settings", async () => {
    atTab('questions');
    stubApi('Open');
    renderPage();

    const row = await waitFor(() => {
      const found = document.querySelector<HTMLElement>('[data-ai-use-row]');
      expect(found).toHaveTextContent('Policy: Not allowed.');
      return found!;
    });
    expect(within(row).getByRole('link', { name: 'Change policy' })).toHaveAttribute('href', `/jobs/${jobId}/edit#ai-use`);
  });

  it('keeps the tab in the address: pressing Questions goes to ?tab=questions', async () => {
    stubApi('Open');
    renderPage();
    await screen.findByRole('tabpanel', { name: 'Overview' });

    await fireEvent.mouseDown(screen.getByRole('tab', { name: 'Questions' }), { button: 0 });
    await fireEvent.focus(screen.getByRole('tab', { name: 'Questions' }));

    expect(goto).toHaveBeenCalledTimes(1);
    expect(goto.mock.calls[0]![0]).toBe('/?tab=questions');
  });
});
