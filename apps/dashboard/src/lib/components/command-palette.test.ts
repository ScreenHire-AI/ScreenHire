import type { SearchResults } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { goto } from '$app/navigation';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { user } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import CommandPalette from './command-palette.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/') } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

// bits-ui scrolls the highlighted option into view, which jsdom does not implement.
Element.prototype.scrollIntoView ??= () => {};

afterEach(() => {
  vi.unstubAllGlobals();
  vi.mocked(goto).mockClear();
});

const results: SearchResults = {
  candidates: [
    {
      applicationId: 'application-1',
      candidateId: 'candidate-1',
      candidateName: 'Casey Morgan',
      email: 'cm.hires@example.com',
      jobId: 'job-1',
      jobTitle: 'Senior .NET Engineer',
      stage: { id: 'screening', name: 'Screening', kind: 'Screening' },
    },
  ],
  jobs: [{ id: 'job-2', title: 'Customer Success Lead', status: 'Open' }],
};

function renderPalette(answer: (url: string) => Response = () => Response.json(results)) {
  const searches: string[] = [];
  vi.stubGlobal('fetch', async (input: RequestInfo | URL) => {
    const url = String(input);
    if (!url.startsWith('/v1/search')) throw new Error(`No stub for ${url}`);
    searches.push(url);
    return answer(url);
  });
  render(WithQueryClient, { props: { component: CommandPalette, props: { open: true } } });
  const palette = screen.getByRole('dialog', { name: 'Command palette' });
  return { searches, palette, input: within(palette).getByRole('combobox') };
}

describe('command palette', () => {
  it('searches the server once typing pauses, from two characters, and never loads the lists', async () => {
    const { searches, input } = renderPalette();

    await fireEvent.input(input, { target: { value: 'c' } });
    await new Promise((resolve) => setTimeout(resolve, 300));
    expect(searches).toEqual([]);

    for (const value of ['cm', 'cm.', 'cm.h']) await fireEvent.input(input, { target: { value } });
    await waitFor(() => expect(searches).toEqual(['/v1/search?q=cm.h']));
  });

  it('shows a candidate the server found by email, and a job, and opens what is chosen', async () => {
    const { palette, input } = renderPalette();

    await fireEvent.input(input, { target: { value: 'cm.hires' } });
    const candidate = await within(palette).findByRole('option', { name: /Casey Morgan/ });
    expect(candidate).toHaveTextContent('Senior .NET Engineer');
    expect(candidate).toHaveTextContent('Screening');
    await expectNoAxeViolations();

    await fireEvent.click(candidate);
    await waitFor(() => expect(goto).toHaveBeenCalledWith('/applications/application-1'));
  });

  it('opens a job the server found by title', async () => {
    const { palette, input } = renderPalette();

    await fireEvent.input(input, { target: { value: 'customer' } });
    await fireEvent.click(await within(palette).findByRole('option', { name: /Customer Success Lead/ }));

    await waitFor(() => expect(goto).toHaveBeenCalledWith('/jobs/job-2'));
  });

  it('says so when the search fails, and still offers the actions', async () => {
    const { palette, input } = renderPalette(() => Response.json({ title: 'Error' }, { status: 500 }));

    await fireEvent.input(input, { target: { value: 'sign' } });

    expect(await within(palette).findByText('Could not search jobs and candidates. Try again.')).toBeInTheDocument();
    expect(within(palette).getByRole('option', { name: 'Sign out' })).toBeInTheDocument();
  });
});
