import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobs } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// Creating a job opens it; superforms needs the rest of the module (beforeNavigate).
const navigation = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: navigation.goto }));

const cellTexts = (row: HTMLElement) => within(row).getAllByRole('cell').map((cell) => cell.textContent?.trim());

function renderPage() {
  return render(WithQueryClient, { props: { component: Page, props: {} } });
}

async function dataRows() {
  await screen.findByRole('link', { name: jobs[0]!.title });
  const [, ...rows] = screen.getAllByRole('row');
  return rows;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('jobs page', () => {
  it('shows a row per job with its location, status and publish date, linking to the job', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(jobs) })));
    renderPage();

    expect((await dataRows()).map(cellTexts)).toEqual([
      ['Customer Success Lead', 'Manchester, UK', 'Draft', '-', 'Publish'],
      ['Senior .NET Engineer', 'Remote (GB, IE)', 'Open', 'Oct 18, 2026', 'Close'],
    ]);
    expect(screen.getByRole('link', { name: 'Senior .NET Engineer' })).toHaveAttribute('href', `/jobs/${jobs[1]!.id}`);
  });

  it('counts all jobs and open jobs', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(jobs) })));
    renderPage();

    expect(await screen.findByText('2 jobs · 1 open')).toBeInTheDocument();
  });

  it('says so when there are no jobs', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json([]) })));
    renderPage();

    expect(await screen.findByRole('cell')).toHaveTextContent('No jobs yet.');
  });

  it('offers no action on a closed job', async () => {
    const closed = { ...jobs[1]!, status: 'Closed' as const };
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json([closed]) })));
    renderPage();

    await screen.findByText('1 jobs · 0 open');
    expect(screen.queryByRole('button', { name: /Close|Publish/ })).not.toBeInTheDocument();
  });

  it('shows the failure and a way to try again when the list does not load', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => new Response(null, { status: 503 }) })));
    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load jobs (HTTP 503).');
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});

describe('jobs page actions', () => {
  it.each([
    ['Publish Customer Success Lead', `/v1/jobs/${jobs[0]!.id}/publish`],
    ['Close Senior .NET Engineer', `/v1/jobs/${jobs[1]!.id}/close`],
  ])('%s posts to the API and refetches the list', async (button, url) => {
    const fetchMock = vi.fn(
      apiStub({
        'GET /v1/jobs': () => Response.json(jobs),
        [`POST ${url}`]: () => Response.json({}),
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
    renderPage();

    await fireEvent.click(await screen.findByRole('button', { name: button }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(url, expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(fetchMock.mock.calls.filter(([request]) => request === '/v1/jobs')).toHaveLength(2));
  });

  it("says so with the API's reason when it refuses", async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/jobs': () => Response.json(jobs),
          [`POST /v1/jobs/${jobs[0]!.id}/publish`]: () =>
            Response.json({ title: 'Conflict', detail: 'Adzuna needs a longer description.' }, { status: 409 }),
        }),
      ),
    );
    renderPage();

    await fireEvent.click(await screen.findByRole('button', { name: 'Publish Customer Success Lead' }));

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Adzuna needs a longer description.'));
  });

  it('says so when the request never reaches the API, and lets the person try again', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/jobs': () => Response.json(jobs),
          [`POST /v1/jobs/${jobs[1]!.id}/close`]: () => Promise.reject(new TypeError('Failed to fetch')),
        }),
      ),
    );
    renderPage();
    const close = await screen.findByRole('button', { name: 'Close Senior .NET Engineer' });

    await fireEvent.click(close);

    await waitFor(() =>
      expect(screen.getByRole('status')).toHaveTextContent('Could not close Senior .NET Engineer. Check your connection and try again.'),
    );
    expect(close).toBeEnabled();
  });
});

describe('new job dialog', () => {
  const created = { ...jobs[1]!, id: '01a157d4-0000-7000-8000-0000000000f1', status: 'Draft' as const };

  function stubCreate() {
    const bodies: unknown[] = [];
    const fetchMock = vi.fn(
      apiStub({
        'GET /v1/jobs': () => Response.json(jobs),
        'GET /v1/templates': () => Response.json([]),
        'POST /v1/jobs': (init) => {
          bodies.push(JSON.parse(String(init?.body)));
          return Response.json(created, { status: 201 });
        },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
    return { bodies, fetchMock };
  }

  async function openDialog() {
    renderPage();
    await fireEvent.click(await screen.findByRole('button', { name: 'New job' }));
    return screen.findByRole('dialog', { name: 'New job' });
  }

  it('creates a remote job with the countries candidates can work from and a description, then opens it', async () => {
    const { bodies } = stubCreate();
    const dialog = await openDialog();

    await fireEvent.input(within(dialog).getByLabelText('Title'), { target: { value: 'Platform Engineer' } });
    await fireEvent.change(within(dialog).getByLabelText('Workplace'), { target: { value: 'Remote' } });
    await fireEvent.input(await within(dialog).findByLabelText('Countries candidates can work from'), { target: { value: 'gb, ie' } });
    await fireEvent.input(within(dialog).getByLabelText('Description'), { target: { value: 'Run the platform.\n\nRemote in the UK & Ireland.' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Create job' }));

    await waitFor(() => expect(navigation.goto).toHaveBeenCalledWith(`/jobs/${created.id}`));
    expect(bodies).toEqual([
      {
        title: 'Platform Engineer',
        templateVersionId: null,
        workplaceType: 'Remote',
        applicantLocationRequirements: ['GB', 'IE'],
        locations: null,
        description: '<p>Run the platform.</p><p>Remote in the UK &amp; Ireland.</p>',
      },
    ]);
  });

  it('creates an on-site job with its city and country', async () => {
    const { bodies } = stubCreate();
    const dialog = await openDialog();

    await fireEvent.input(within(dialog).getByLabelText('Title'), { target: { value: 'Warehouse lead' } });
    await fireEvent.change(within(dialog).getByLabelText('Workplace'), { target: { value: 'Onsite' } });
    await fireEvent.input(await within(dialog).findByLabelText('City'), { target: { value: 'Leeds' } });
    await fireEvent.input(within(dialog).getByLabelText('Country'), { target: { value: 'gb' } });
    await fireEvent.input(within(dialog).getByLabelText('Description'), { target: { value: 'Lead the night shift.' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Create job' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ workplaceType: 'Onsite', applicantLocationRequirements: null, locations: [{ city: 'Leeds', region: null, country: 'GB' }] });
  });

  it('asks where the work happens, and for a description when no template brings one, before sending anything', async () => {
    const { fetchMock } = stubCreate();
    const dialog = await openDialog();

    await fireEvent.input(within(dialog).getByLabelText('Title'), { target: { value: 'Platform Engineer' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Create job' }));

    expect(await within(dialog).findByText('Choose where the work happens.')).toBeInTheDocument();
    expect(within(dialog).getByText('Describe the job, or choose a template that has a description.')).toBeInTheDocument();

    await fireEvent.change(within(dialog).getByLabelText('Workplace'), { target: { value: 'Remote' } });
    await fireEvent.input(await within(dialog).findByLabelText('Countries candidates can work from'), { target: { value: 'United Kingdom' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Create job' }));

    expect(await within(dialog).findByText('Use two-letter country codes, such as GB.')).toBeInTheDocument();
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0);
  });
});
