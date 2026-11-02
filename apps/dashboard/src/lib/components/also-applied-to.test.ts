import type { ApplicationSummary, CandidateDetail } from '@screenhire/api-client';
import { render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, applications } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import AlsoAppliedTo from './also-applied-to.svelte';

const candidateId = '01a157d4-0000-7000-8000-000000000204';
const thisApplication = '01a157d4-0000-7000-8000-000000000300';

const summary = (id: string, jobTitle: string): ApplicationSummary => ({ ...applications[0]!, id, candidateId, jobTitle });

const detail = (others: ApplicationSummary[]): CandidateDetail => ({
  id: candidateId,
  fullName: 'Casey Morgan',
  firstName: 'Casey',
  lastName: 'Morgan',
  email: 'casey@example.com',
  phone: null,
  createdAt: '2026-10-01T09:00:00Z',
  applications: [summary(thisApplication, 'Support Engineer'), ...others],
});

const renderLink = () => render(WithQueryClient, { props: { component: AlsoAppliedTo, props: { candidateId, applicationId: thisApplication } } });

afterEach(() => vi.unstubAllGlobals());

describe('also applied to', () => {
  it('links to the candidate profile before the count arrives, and shows no count yet', async () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})));
    renderLink();

    const link = await screen.findByRole('link', { name: 'Also applied to' });

    expect(link).toHaveAttribute('href', `/candidates/${candidateId}`);
    expect(link).not.toHaveTextContent(':');
  });

  it('says there are no other jobs when this is the only application', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ [`GET /v1/candidates/${candidateId}`]: () => Response.json(detail([])) })));
    renderLink();

    expect(await screen.findByText('no other jobs')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Also applied to' })).toHaveAttribute('href', `/candidates/${candidateId}`);
  });

  it('counts one other job in the singular', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ [`GET /v1/candidates/${candidateId}`]: () => Response.json(detail([summary('a2', 'Senior .NET Engineer')])) })));
    renderLink();

    expect(await screen.findByText('1 other job')).toBeInTheDocument();
  });

  it('counts the other applications in the plural, leaving this one out', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(apiStub({ [`GET /v1/candidates/${candidateId}`]: () => Response.json(detail([summary('a2', 'Senior .NET Engineer'), summary('a3', 'Product Designer')])) })),
    );
    renderLink();

    expect(await screen.findByText('2 other jobs')).toBeInTheDocument();
  });

  it('keeps the link without a count when the candidate cannot be loaded', async () => {
    const fetchMock = vi.fn(apiStub({ [`GET /v1/candidates/${candidateId}`]: () => Response.json({ title: 'Not found' }, { status: 404 }) }));
    vi.stubGlobal('fetch', fetchMock);
    renderLink();

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const link = await screen.findByRole('link', { name: 'Also applied to' });
    expect(link).not.toHaveTextContent(':');
    expect(link).toHaveAttribute('href', `/candidates/${candidateId}`);
  });
});
