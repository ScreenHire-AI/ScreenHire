import type { JobDetail } from '@screenhire/api-client';
import { QueryClient } from '@tanstack/svelte-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobId } from '../../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// Saving goes back to the job; superforms needs the rest of the module (beforeNavigate).
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
  salary: { min: 70000, max: 90000, currency: 'GBP', period: 'Year' },
  validThrough: null,
} as unknown as JobDetail;

function renderPage(routes: Parameters<typeof apiStub>[0], client?: QueryClient) {
  const fetchMock = vi.fn(apiStub(routes));
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: { params: { jobId } }, client } });
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
  navigation.goto.mockClear();
});

describe('edit job details page', () => {
  it('saves the details and goes back to the job, which says what saving did', async () => {
    renderPage({ [`GET /v1/jobs/${jobId}`]: () => Response.json(job), [`PUT /v1/jobs/${jobId}`]: () => Response.json(job) });

    const form = await screen.findByRole('form', { name: 'Edit job details' });
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Edit job details');
    await fireEvent.input(within(form).getByLabelText('To'), { target: { value: '95000' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Save details' }));

    await waitFor(() =>
      expect(navigation.goto).toHaveBeenCalledWith(`/jobs/${jobId}`, {
        state: { saved: 'Saved. The careers page shows it now; publish again to update the boards.' },
      }),
    );
  });

  it('keeps the form and what was typed when a refetch fails', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    let failing = false;
    renderPage({ [`GET /v1/jobs/${jobId}`]: () => (failing ? new Response(null, { status: 503 }) : Response.json(job)) }, client);

    const form = await screen.findByRole('form', { name: 'Edit job details' });
    await fireEvent.input(within(form).getByLabelText('To'), { target: { value: '95000' } });
    failing = true;
    await client.refetchQueries();

    await waitFor(() => expect(client.getQueryState(['v1', 'jobs', jobId])?.status).toBe('error'));
    expect(screen.getByRole('form', { name: 'Edit job details' })).toBe(form);
    expect(within(form).getByLabelText('To')).toHaveValue('95000');
  });

  it('offers no form for a closed job', async () => {
    renderPage({ [`GET /v1/jobs/${jobId}`]: () => Response.json({ ...job, status: 'Closed' }) });

    expect(await screen.findByText('This job is closed')).toBeInTheDocument();
    expect(screen.queryByRole('form')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to the job' })).toHaveAttribute('href', `/jobs/${jobId}`);
  });

  it('says when the job does not exist', async () => {
    renderPage({ [`GET /v1/jobs/${jobId}`]: () => Response.json({ title: 'Not Found' }, { status: 404 }) });

    expect(await screen.findByText('It does not exist, or it belongs to another organization.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to jobs' })).toHaveAttribute('href', '/');
    expect(screen.queryByRole('form')).not.toBeInTheDocument();
  });
});
