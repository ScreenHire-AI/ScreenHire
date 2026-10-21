import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobs } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// Creating a job opens it; superforms needs the rest of the module (beforeNavigate).
const navigation = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: navigation.goto }));

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('new job page', () => {
  const created = { ...jobs[1]!, id: '01a157d4-0000-7000-8000-0000000000f1', status: 'Draft' as const };

  function stubCreate() {
    const bodies: unknown[] = [];
    const fetchMock = vi.fn(
      apiStub({
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

  async function openForm() {
    render(WithQueryClient, { props: { component: Page, props: {} } });
    return screen.findByRole('form', { name: 'New job' });
  }

  it('creates a remote job with the countries candidates can work from and a description, then opens it', async () => {
    const { bodies } = stubCreate();
    const form = await openForm();

    await fireEvent.input(within(form).getByLabelText('Title'), { target: { value: 'Platform Engineer' } });
    await fireEvent.change(within(form).getByLabelText('Workplace'), { target: { value: 'Remote' } });
    await fireEvent.input(await within(form).findByLabelText('Countries candidates can work from'), { target: { value: 'gb, ie' } });
    await fireEvent.input(within(form).getByLabelText('Description'), { target: { value: 'Run the platform.\n\nRemote in the UK & Ireland.' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Create job' }));

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
    const form = await openForm();

    await fireEvent.input(within(form).getByLabelText('Title'), { target: { value: 'Warehouse lead' } });
    await fireEvent.change(within(form).getByLabelText('Workplace'), { target: { value: 'Onsite' } });
    await fireEvent.input(await within(form).findByLabelText('City'), { target: { value: 'Leeds' } });
    await fireEvent.input(within(form).getByLabelText('Country code'), { target: { value: 'gb' } });
    await fireEvent.input(within(form).getByLabelText('Description'), { target: { value: 'Lead the night shift.' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Create job' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ workplaceType: 'Onsite', applicantLocationRequirements: null, locations: [{ city: 'Leeds', region: null, country: 'GB' }] });
  });

  it('asks where the work happens, and for a description when no template brings one, before sending anything', async () => {
    const { fetchMock } = stubCreate();
    const form = await openForm();

    await fireEvent.input(within(form).getByLabelText('Title'), { target: { value: 'Platform Engineer' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Create job' }));

    expect(await within(form).findByText('Choose where the work happens.')).toBeInTheDocument();
    expect(within(form).getByText('Describe the job, or choose a template that has a description.')).toBeInTheDocument();

    await fireEvent.change(within(form).getByLabelText('Workplace'), { target: { value: 'Remote' } });
    await fireEvent.input(await within(form).findByLabelText('Countries candidates can work from'), { target: { value: 'United Kingdom' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Create job' }));

    expect(await within(form).findByText('Use two-letter country codes, such as GB.')).toBeInTheDocument();
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0);
  });
});
