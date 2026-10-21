import type { HiringWorkflow, JobDetail, JobStatus } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobId, user } from '../../../../../test-support/fixtures.js';
import { stubViewport } from '../../../../../test-support/viewport.js';
import WithQueryClient from '../../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/') }, navigating: { to: null } }));

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone-screen', name: 'Phone screen', kind: 'Screening', slaDays: 3 },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: { questions: [] },
  integrityPolicy: { aiUse: 'AllowedWithDisclosure' },
  automations: [],
};

// Only what the page reads from the job.
const jobIn = (status: JobStatus) =>
  ({ id: jobId, title: 'Senior .NET Engineer', status, workflow, workflowVersion: 3, template: null }) as unknown as JobDetail;

function stubApi(status: JobStatus) {
  const fetchMock = vi.fn(
    apiStub({
      [`GET /v1/jobs/${jobId}`]: () => Response.json(jobIn(status)),
      'POST /v1/workflows/validate': () => Response.json({ errors: [] }),
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

const renderPage = () => render(WithQueryClient, { props: { component: Page, props: { params: { jobId } } } });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('job workflow page', () => {
  it('reads a closed job as content: the summary, the Job closed pill, no controls and no Save button', async () => {
    stubApi('Closed');
    const { container } = renderPage();

    const pane = await screen.findByRole('tabpanel', { name: 'Pipeline' });
    expect(within(pane).getByRole('region', { name: 'Stages' })).toBeInTheDocument();
    expect(pane.querySelectorAll('input, select, textarea')).toHaveLength(0);
    expect(container.querySelectorAll(':disabled')).toHaveLength(0);
    expect(screen.getByText('Job closed')).toHaveAttribute('data-slot', 'badge');
    expect(screen.queryByRole('button', { name: 'Save workflow' })).toBeNull();
  });

  it('shows the builder with a Save button for an open job, and no Job closed pill', async () => {
    stubApi('Open');
    renderPage();

    const stages = await screen.findByRole('region', { name: 'Stages' });
    expect(within(stages).getAllByRole('spinbutton', { name: 'Time limit (days)' }).length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: 'Save workflow' })).toBeEnabled();
    expect(screen.queryByText('Job closed')).toBeNull();
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
    const fetchMock = stubApi('Open');
    renderPage();
    await screen.findByRole('region', { name: 'Stages' });
    await waitFor(() => expect(screen.getByRole('region', { name: 'Validation' })).toHaveAttribute('aria-busy', 'false'));

    await fireEvent.click(screen.getByRole('button', { name: 'Save workflow' }));

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('No changes to save.'));
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'PUT')).toHaveLength(0);
  });
});
