import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { pickCountry } from '../../../../test-support/countries.js';
import { apiStub, jobs } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';
import { editRichText } from '../../../../test-support/rich-text.js';

// Creating a job opens it; superforms needs the rest of the module (beforeNavigate).
const navigation = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: navigation.goto }));

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('new job page', () => {
  const created = { ...jobs[1]!, id: '01a157d4-0000-7000-8000-0000000000f1', status: 'Draft' as const };

  function stubCreate(templates: unknown[] = []) {
    const bodies: unknown[] = [];
    const fetchMock = vi.fn(
      apiStub({
        'GET /v1/templates': () => Response.json(templates),
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
    const countries = await within(form).findByLabelText('Countries candidates can work from');
    await pickCountry(countries, 'United Kingdom');
    await pickCountry(countries, 'Ireland');
    editRichText(within(form).getByRole('textbox', { name: 'Description' }), '<p>Run the platform.</p><p>Remote in the UK &amp; Ireland.</p>');
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
    await fireEvent.change(within(form).getByLabelText('Country'), { target: { value: 'GB' } });
    editRichText(within(form).getByRole('textbox', { name: 'Description' }), '<p>Lead the night shift.</p>');
    await fireEvent.click(within(form).getByRole('button', { name: 'Create job' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ workplaceType: 'Onsite', applicantLocationRequirements: null, locations: [{ city: 'Leeds', region: null, country: 'GB' }] });
  });

  // An editor left empty writes nothing, so the template's own description still applies on the server.
  it('sends no description when the editor is left empty and a template brings one', async () => {
    const versionId = '01a157d4-0000-7000-8000-0000000000a2';
    const { bodies } = stubCreate([
      { id: '01a157d4-0000-7000-8000-0000000000a1', name: 'Software engineer', latestPublishedVersionId: versionId, latestPublishedVersion: 2, archived: false },
    ]);
    const form = await openForm();

    await fireEvent.input(within(form).getByLabelText('Title'), { target: { value: 'Backend Engineer' } });
    await fireEvent.change(await within(form).findByRole('option', { name: 'Software engineer (v2)' }).then(() => within(form).getByLabelText('Hiring template')), {
      target: { value: versionId },
    });
    await fireEvent.change(within(form).getByLabelText('Workplace'), { target: { value: 'Remote' } });
    await pickCountry(await within(form).findByLabelText('Countries candidates can work from'), 'United Kingdom');
    await fireEvent.click(within(form).getByRole('button', { name: 'Create job' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ templateVersionId: versionId, description: null });
  });

  it('asks where the work happens, and for a description when no template brings one, before sending anything', async () => {
    const { fetchMock } = stubCreate();
    const form = await openForm();

    await fireEvent.input(within(form).getByLabelText('Title'), { target: { value: 'Platform Engineer' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Create job' }));

    expect(await within(form).findByText('Choose where the work happens.')).toBeInTheDocument();
    expect(within(form).getByText('Describe the job, or choose a template that has a description.')).toBeInTheDocument();

    await fireEvent.change(within(form).getByLabelText('Workplace'), { target: { value: 'Remote' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Create job' }));

    expect(await within(form).findByText('Choose at least one country candidates can work from.')).toBeInTheDocument();

    await fireEvent.change(within(form).getByLabelText('Workplace'), { target: { value: 'Hybrid' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Create job' }));

    expect(await within(form).findByText('Choose the country the job is in.')).toBeInTheDocument();
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0);
  });
});
