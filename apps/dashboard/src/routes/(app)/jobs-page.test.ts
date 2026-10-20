import type { CurrentUser, JobSummary } from '@screenhire/api-client';
import { render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import Page from './+page.svelte';
import { load } from './+page.js';

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
    employmentType: 'FullTime',
    department: null,
    templateUpdateAvailable: false,
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
    employmentType: 'FullTime',
    department: 'Engineering',
    templateUpdateAvailable: false,
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
      ['Customer Success Lead', 'Manchester, UK', 'Draft', '—'],
      ['Senior .NET Engineer', 'Remote — UK & EU', 'Open', '18 Oct'],
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
});

describe('jobs page load', () => {
  const event = {} as Parameters<typeof load>[0];

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns the jobs from the API', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => Response.json(jobs));
    vi.stubGlobal('fetch', fetchMock);

    await expect(load(event)).resolves.toEqual({ jobs });
    expect(fetchMock).toHaveBeenCalledWith('/v1/jobs', expect.objectContaining({ method: 'GET' }));
  });

  it('fails with the HTTP status when the API does not answer 200', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response(null, { status: 503 })));

    await expect(load(event)).rejects.toThrow('Could not load jobs (HTTP 503).');
  });
});
