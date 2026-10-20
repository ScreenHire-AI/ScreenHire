import type { CurrentUser, JobSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import Page from './+page.svelte';
import { load } from './+page.js';

const navigation = vi.hoisted(() => ({ invalidate: vi.fn(async () => {}) }));
vi.mock('$app/navigation', () => navigation);

const jobs: JobSummary[] = [
  {
    id: '01a157d4-0000-7000-8000-000000000001',
    title: 'Customer Success Lead',
    slug: 'customer-success-lead',
    location: 'Manchester, UK',
    workplaceType: 'Onsite',
    status: 'Draft',
    createdAt: '2026-10-19T09:00:00Z',
    publishedAt: null,
  },
  {
    id: '01a157d4-0000-7000-8000-000000000002',
    title: 'Senior .NET Engineer',
    slug: 'senior-dotnet-engineer',
    location: 'Remote — UK & EU',
    workplaceType: 'Remote',
    status: 'Open',
    createdAt: '2026-10-18T09:00:00Z',
    publishedAt: '2026-10-18T12:00:00Z',
  },
];

const user: CurrentUser = {
  id: '01a157d4-0000-7000-8000-0000000000aa',
  email: 'avery@brightwater.example.com',
  displayName: 'Avery Quinn',
  organization: { id: '01a157d4-0000-7000-8000-0000000000bb', name: 'Brightwater Studio', slug: 'brightwater', role: 'Owner' },
};

const cellTexts = (row: HTMLElement) => within(row).getAllByRole('cell').map((cell) => cell.textContent?.trim());

describe('jobs page', () => {
  it('shows a row per job with its location, status and publish date', () => {
    render(Page, { props: { params: {}, data: { user, jobs } } });

    const [, ...rows] = screen.getAllByRole('row');
    expect(rows.map(cellTexts)).toEqual([
      ['Customer Success Lead', 'Manchester, UK', 'Draft', '—', 'Publish'],
      ['Senior .NET Engineer', 'Remote — UK & EU', 'Open', '18 Oct', 'Close'],
    ]);
  });

  it('counts all jobs and open jobs', () => {
    render(Page, { props: { params: {}, data: { user, jobs } } });

    expect(screen.getByText('2 jobs · 1 open')).toBeInTheDocument();
  });

  it('says so when there are no jobs', () => {
    render(Page, { props: { params: {}, data: { user, jobs: [] } } });

    expect(screen.getByRole('cell')).toHaveTextContent('No jobs yet.');
  });

  it('offers no action on a closed job', () => {
    const closed: JobSummary = { ...jobs[1]!, id: '01a157d4-0000-7000-8000-000000000003', status: 'Closed' };
    render(Page, { props: { params: {}, data: { user, jobs: [closed] } } });

    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});

describe('jobs page actions', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    navigation.invalidate.mockClear();
  });

  it.each([
    ['Publish Customer Success Lead', '/v1/jobs/01a157d4-0000-7000-8000-000000000001/publish'],
    ['Close Senior .NET Engineer', '/v1/jobs/01a157d4-0000-7000-8000-000000000002/close'],
  ])('%s posts to the API and refetches the list', async (button, url) => {
    const fetchMock = vi.fn<typeof fetch>(async () => Response.json(jobs[0]));
    vi.stubGlobal('fetch', fetchMock);
    render(Page, { props: { params: {}, data: { user, jobs } } });

    await fireEvent.click(screen.getByRole('button', { name: button }));

    expect(fetchMock).toHaveBeenCalledWith(url, expect.objectContaining({ method: 'POST' }));
    await waitFor(() => expect(navigation.invalidate).toHaveBeenCalledWith('api:jobs'));
  });

  it('says so when the API refuses', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response(null, { status: 409 })));
    render(Page, { props: { params: {}, data: { user, jobs } } });

    await fireEvent.click(screen.getByRole('button', { name: 'Publish Customer Success Lead' }));

    await waitFor(() =>
      expect(screen.getByRole('status')).toHaveTextContent('Could not publish Customer Success Lead (HTTP 409).'),
    );
  });

  it('says so when the request never reaches the API, and lets the person try again', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => Promise.reject(new TypeError('Failed to fetch'))));
    render(Page, { props: { params: {}, data: { user, jobs } } });
    const close = screen.getByRole('button', { name: 'Close Senior .NET Engineer' });

    await fireEvent.click(close);

    await waitFor(() =>
      expect(screen.getByRole('status')).toHaveTextContent(
        'Could not close Senior .NET Engineer. Check your connection and try again.',
      ),
    );
    expect(close).toBeEnabled();
  });
});

describe('jobs page load', () => {
  const depends = vi.fn();
  const event = { depends } as unknown as Parameters<typeof load>[0];

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns the jobs from the API', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => Response.json(jobs));
    vi.stubGlobal('fetch', fetchMock);

    await expect(load(event)).resolves.toEqual({ jobs });
    expect(fetchMock).toHaveBeenCalledWith('/v1/jobs', expect.objectContaining({ method: 'GET' }));
  });

  it('depends on the jobs key that live job events invalidate', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => Response.json(jobs)));

    await load(event);

    expect(depends).toHaveBeenCalledWith('api:jobs');
  });

  it('fails with the HTTP status when the API does not answer 200', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response(null, { status: 503 })));

    await expect(load(event)).rejects.toThrow('Could not load jobs (HTTP 503).');
  });
});
