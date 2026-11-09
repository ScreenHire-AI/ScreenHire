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
  aiUse: { policy: 'AllowedWithDisclosure', override: null, organizationDefault: 'AllowedWithDisclosure' },
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
    // The header names the job and its status first (S2); the panel and the crumb say what the page does.
    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    expect(within(band).getByRole('heading', { level: 1 })).toHaveTextContent(job.title);
    expect(within(band).getByText('Open')).toHaveAttribute('data-slot', 'badge');
    expect(screen.getByRole('region', { name: 'Edit details' })).toBeInTheDocument();
    // Save says how far it reaches before it is pressed (E3).
    expect(within(form).getByText('Updates the careers page now. Other boards keep what they were sent until you update them.')).toBeInTheDocument();
    await fireEvent.input(within(form).getByLabelText('To'), { target: { value: '95000' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Save details' }));

    await waitFor(() =>
      expect(navigation.goto).toHaveBeenCalledWith(`/jobs/${jobId}`, {
        state: { saved: 'Saved. The careers page shows it now.' },
      }),
    );
  });

  describe('short summary', () => {
    const following = { ...job, summary: 'Build things.', summaryFollowsDescription: true } as unknown as JobDetail;
    const written = { ...job, summary: 'Own the payments service.', summaryFollowsDescription: false } as unknown as JobDetail;

    const savedBody = (fetchMock: ReturnType<typeof renderPage>) => {
      const put = fetchMock.mock.calls.find(([, init]) => init?.method === 'PUT');
      return JSON.parse(String(put?.[1]?.body)) as { summary: string | null };
    };

    it('shows the first sentence of the description with the hint until someone edits it', async () => {
      renderPage({ [`GET /v1/jobs/${jobId}`]: () => Response.json(following) });

      const form = await screen.findByRole('form', { name: 'Edit job details' });
      const field = within(form).getByLabelText(/^Short summary/);
      expect(field).toHaveValue('Build things.');
      expect(field).toHaveAttribute('maxlength', '160');
      expect(field).toHaveAccessibleDescription(/About the job, not the company\. Up to 160 characters\..*Until you edit it, it is the description's first sentence\./);
    });

    it('saves an untouched default as nothing, so it keeps following the description', async () => {
      const fetchMock = renderPage({ [`GET /v1/jobs/${jobId}`]: () => Response.json(following), [`PUT /v1/jobs/${jobId}`]: () => Response.json(following) });

      const form = await screen.findByRole('form', { name: 'Edit job details' });
      await fireEvent.click(within(form).getByRole('button', { name: 'Save details' }));

      await waitFor(() => expect(navigation.goto).toHaveBeenCalled());
      expect(savedBody(fetchMock).summary).toBeNull();
    });

    it('saves what was typed, and says no more about following the description once it is written', async () => {
      const fetchMock = renderPage({ [`GET /v1/jobs/${jobId}`]: () => Response.json(written), [`PUT /v1/jobs/${jobId}`]: () => Response.json(written) });

      const form = await screen.findByRole('form', { name: 'Edit job details' });
      const field = within(form).getByLabelText(/^Short summary/);
      expect(field).toHaveValue('Own the payments service.');
      expect(field).not.toHaveAccessibleDescription(/Until you edit it/);
      await fireEvent.input(field, { target: { value: '  Run the payments service.  ' } });
      await fireEvent.click(within(form).getByRole('button', { name: 'Save details' }));

      await waitFor(() => expect(navigation.goto).toHaveBeenCalled());
      expect(savedBody(fetchMock).summary).toBe('Run the payments service.');
    });

    it('saves a cleared summary as nothing, which goes back to following the description', async () => {
      const fetchMock = renderPage({ [`GET /v1/jobs/${jobId}`]: () => Response.json(written), [`PUT /v1/jobs/${jobId}`]: () => Response.json(written) });

      const form = await screen.findByRole('form', { name: 'Edit job details' });
      await fireEvent.input(within(form).getByLabelText(/^Short summary/), { target: { value: '' } });
      await fireEvent.click(within(form).getByRole('button', { name: 'Save details' }));

      await waitFor(() => expect(navigation.goto).toHaveBeenCalled());
      expect(savedBody(fetchMock).summary).toBeNull();
    });

    it('refuses more than 160 characters in the field, and saves nothing', async () => {
      const fetchMock = renderPage({ [`GET /v1/jobs/${jobId}`]: () => Response.json(written) });

      const form = await screen.findByRole('form', { name: 'Edit job details' });
      await fireEvent.input(within(form).getByLabelText(/^Short summary/), { target: { value: 'a'.repeat(161) } });
      await fireEvent.click(within(form).getByRole('button', { name: 'Save details' }));

      expect(await within(form).findByText('Keep the summary to 160 characters or fewer.')).toBeInTheDocument();
      expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(false);
    });
  });

  describe('AI use while applying', () => {
    const own = { ...job, aiUse: { policy: 'Allowed', override: 'Allowed', organizationDefault: 'AllowedWithDisclosure' } } as unknown as JobDetail;
    const aiUseBody = (fetchMock: ReturnType<typeof renderPage>) => {
      const put = fetchMock.mock.calls.find(([path, init]) => path === `/v1/jobs/${jobId}/ai-use` && init?.method === 'PUT');
      return put ? (JSON.parse(String(put[1]?.body)) as { policy: string | null }) : undefined;
    };

    it("follows the organization's policy by default, names it, and saves its own on a button of its own", async () => {
      const fetchMock = renderPage({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(job),
        [`PUT /v1/jobs/${jobId}/ai-use`]: () => Response.json({ ...job, aiUse: { policy: 'NotAllowed', override: 'NotAllowed', organizationDefault: 'AllowedWithDisclosure' } }),
      });

      const select = await screen.findByRole('combobox', { name: 'AI use while applying' });
      expect(select).toHaveValue('');
      expect(within(select).getByRole('option', { name: 'Follow the organization (Allowed with disclosure)' })).toBeInTheDocument();
      const save = screen.getByRole('button', { name: 'Save AI use' });
      expect(save).toBeDisabled();

      await fireEvent.change(select, { target: { value: 'NotAllowed' } });
      await fireEvent.click(save);

      await waitFor(() => expect(aiUseBody(fetchMock)).toEqual({ policy: 'NotAllowed' }));
      expect(await screen.findByText('Saved.')).toBeInTheDocument();
      expect(navigation.goto).not.toHaveBeenCalled();
    });

    it("goes back to the organization's policy by saving none", async () => {
      const fetchMock = renderPage({ [`GET /v1/jobs/${jobId}`]: () => Response.json(own), [`PUT /v1/jobs/${jobId}/ai-use`]: () => Response.json(job) });

      const select = await screen.findByRole('combobox', { name: 'AI use while applying' });
      expect(select).toHaveValue('Allowed');
      await fireEvent.change(select, { target: { value: '' } });
      await fireEvent.click(screen.getByRole('button', { name: 'Save AI use' }));

      await waitFor(() => expect(aiUseBody(fetchMock)).toEqual({ policy: null }));
    });
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
