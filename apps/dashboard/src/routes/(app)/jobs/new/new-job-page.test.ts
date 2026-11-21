import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { pickCountry } from '../../../../test-support/countries.js';
import { apiStub, jobs, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';
import { editRichText } from '../../../../test-support/rich-text.js';

// Creating a job opens it; superforms needs the rest of the module (beforeNavigate).
const navigation = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: navigation.goto }));
vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/jobs/new') }, navigating: { to: null } }));

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('new job page', () => {
  const created = { ...jobs[1]!, id: '01a157d4-0000-7000-8000-0000000000f1', status: 'Draft' as const };

  function stubCreate(templates: unknown[] = [], routes: Parameters<typeof apiStub>[0] = {}) {
    const bodies: unknown[] = [];
    const fetchMock = vi.fn(
      apiStub({
        'GET /v1/templates': () => Response.json(templates),
        'POST /v1/jobs': (init) => {
          bodies.push(JSON.parse(String(init?.body)));
          return Response.json(created, { status: 201 });
        },
        ...routes,
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

  // A template's description can be a prompt to fill in, so the form says to edit it, and says what a template brings and why a city matters.
  it('says what a template brings, to edit its description before publishing, and why an on-site job wants a city', async () => {
    const versionId = '01a157d4-0000-7000-8000-0000000000a2';
    stubCreate([{ id: '01a157d4-0000-7000-8000-0000000000a1', name: 'Software engineer', latestPublishedVersionId: versionId, latestPublishedVersion: 2, archived: false }]);
    const form = await openForm();

    const template = within(form).getByLabelText('Hiring template');
    expect(template).toHaveAccessibleDescription('A template sets the stages, questions and emails.');
    await fireEvent.change(await within(form).findByRole('option', { name: 'Software engineer (v2)' }).then(() => template), { target: { value: versionId } });
    expect(within(form).getByText("Leave it empty to start from the template's description, and edit it before you publish.")).toBeInTheDocument();

    await fireEvent.change(within(form).getByLabelText('Workplace'), { target: { value: 'Onsite' } });
    expect(await within(form).findByLabelText('City')).toHaveAccessibleDescription('Some job boards refuse an on-site or hybrid job without a city.');
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

  describe("who's hiring", () => {
    const versionId = '01a157d4-0000-7000-8000-0000000000a2';
    const template = { id: '01a157d4-0000-7000-8000-0000000000a1', name: 'Line cook', latestPublishedVersionId: versionId, latestPublishedVersion: 2, archived: false };
    const member = (id: string, userId: string, displayName: string, seesAllJobs = true) => ({
      id,
      userId,
      email: `${id}@example.com`,
      displayName,
      role: seesAllJobs ? 'Recruiter' : 'Viewer',
      customRoleId: null,
      roleName: seesAllJobs ? 'Recruiter' : 'Viewer',
      joinedAt: '2026-10-01T09:00:00Z',
      jobAccess: seesAllJobs ? 'All' : 'HiringTeamOnly',
      seesAllJobs,
      jobCount: 0,
    });
    const avery = member('m-1', user.id, 'Avery Quinn');
    const jordan = member('m-2', 'u-2', 'Jordan Ellis');
    const robin = member('m-3', 'u-3', 'Robin Hale', false);
    const onTeam = (person: ReturnType<typeof member>) => ({
      memberId: person.id,
      userId: person.userId,
      email: person.email,
      displayName: person.displayName,
      roleName: person.roleName,
      seesAllJobs: person.seesAllJobs,
    });
    const kitchen = { id: 'team-kitchen', name: 'Kitchen', members: [onTeam(avery), onTeam(jordan)], createdAt: '2026-10-01T09:00:00Z', updatedAt: null };
    const bar = { id: 'team-bar', name: 'Bar', members: [onTeam(robin)], createdAt: '2026-10-01T09:00:00Z', updatedAt: null };

    async function fillIn(form: HTMLElement) {
      await fireEvent.input(within(form).getByLabelText('Title'), { target: { value: 'Line cook' } });
      await fireEvent.change(within(form).getByLabelText('Workplace'), { target: { value: 'Remote' } });
      await pickCountry(await within(form).findByLabelText('Countries candidates can work from'), 'United Kingdom');
    }

    it('starts from the team the last job from the template used, and sends the team and anyone added', async () => {
      const { bodies } = stubCreate([template], {
        'GET /v1/organization/members': () => Response.json([avery, jordan, robin]),
        'GET /v1/organization/teams': () => Response.json([bar, kitchen]),
        [`GET /v1/organization/teams/suggested?templateVersionId=${versionId}`]: () => Response.json({ teamId: kitchen.id }),
      });
      const form = await openForm();
      await fireEvent.change(await within(form).findByRole('option', { name: 'Line cook (v2)' }).then(() => within(form).getByLabelText('Hiring template')), {
        target: { value: versionId },
      });

      const team = await within(form).findByLabelText('Team');
      await waitFor(() => expect(team).toHaveValue(kitchen.id));
      // The team's people other than you, and why it was chosen.
      expect(team).toHaveAccessibleDescription('Used on the last job like this one. Jordan Ellis.');

      await fireEvent.click(within(form).getByRole('button', { name: 'Add a person' }));
      const robinOption = await screen.findByRole('option', { name: /Robin Hale/ });
      // Jordan comes with the Kitchen team, so the picker does not offer them a second time.
      expect(screen.queryByRole('option', { name: /Jordan Ellis/ })).toBeNull();
      await fireEvent.click(robinOption);
      expect(await within(form).findByRole('list', { name: 'Also hiring' })).toHaveTextContent('Robin Hale');
      await fillIn(form);
      await fireEvent.click(within(form).getByRole('button', { name: 'Create job' }));

      await waitFor(() => expect(bodies).toHaveLength(1));
      expect(bodies[0]).toMatchObject({ templateVersionId: versionId, teamId: kitchen.id, memberIds: [robin.id] });
    });

    it('keeps the team the person picked, and sends no team for No team', async () => {
      const { bodies, fetchMock } = stubCreate([template], {
        'GET /v1/organization/members': () => Response.json([avery, jordan]),
        'GET /v1/organization/teams': () => Response.json([kitchen]),
        [`GET /v1/organization/teams/suggested?templateVersionId=${versionId}`]: () => Response.json({ teamId: kitchen.id }),
      });
      const form = await openForm();
      const team = await within(form).findByLabelText('Team');
      await fireEvent.change(team, { target: { value: '' } });
      await fireEvent.change(await within(form).findByRole('option', { name: 'Line cook (v2)' }).then(() => within(form).getByLabelText('Hiring template')), {
        target: { value: versionId },
      });
      // The suggestion arrives for the template, but the person already chose.
      await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(`/v1/organization/teams/suggested?templateVersionId=${versionId}`, expect.anything()));
      expect(within(form).getByLabelText('Team')).toHaveValue('');
      expect(within(form).getByLabelText('Team')).toHaveAccessibleDescription('');
      await fillIn(form);
      await fireEvent.click(within(form).getByRole('button', { name: 'Create job' }));

      await waitFor(() => expect(bodies).toHaveLength(1));
      expect(bodies[0]).toMatchObject({ teamId: null, memberIds: [] });
    });

    // A list that fails to load says so with a retry, rather than quietly starting the job with no team.
    it('says when the teams could not be loaded', async () => {
      stubCreate([], {
        'GET /v1/organization/members': () => Response.json([avery, jordan]),
        'GET /v1/organization/teams': () => Response.json({ title: 'Server error', status: 500 }, { status: 500 }),
      });
      const form = await openForm();

      const who = await within(form).findByRole('group', { name: "Who's hiring?" });
      expect(await within(who).findByText(/Could not load the teams/)).toBeInTheDocument();
      expect(within(who).getByRole('button', { name: 'Try again' })).toBeInTheDocument();
      expect(within(who).queryByLabelText('Team')).toBeNull();
    });

    // Without the people nothing in the step can be sent, so a team that did load is not offered either.
    it('says when the people could not be loaded, and offers no team it could not send', async () => {
      stubCreate([], {
        'GET /v1/organization/members': () => Response.json({ title: 'Server error', status: 500 }, { status: 500 }),
        'GET /v1/organization/teams': () => Response.json([kitchen]),
      });
      const form = await openForm();

      const who = await within(form).findByRole('group', { name: "Who's hiring?" });
      expect(await within(who).findByText(/Could not load the team/)).toBeInTheDocument();
      expect(within(who).getByRole('button', { name: 'Try again' })).toBeInTheDocument();
      expect(within(who).queryByLabelText('Team')).toBeNull();
      expect(within(who).queryByRole('button', { name: 'Add a person' })).toBeNull();
    });

    it('does not ask when nobody but the creator is in the organization, and the job starts with them alone', async () => {
      const { bodies, fetchMock } = stubCreate([], {
        'GET /v1/organization/members': () => Response.json([avery]),
        'GET /v1/organization/teams': () => Response.json([]),
      });
      const form = await openForm();
      await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/v1/organization/members', expect.anything()));
      await fillIn(form);
      editRichText(within(form).getByRole('textbox', { name: 'Description' }), '<p>Cook the line.</p>');
      await fireEvent.click(within(form).getByRole('button', { name: 'Create job' }));

      await waitFor(() => expect(bodies).toHaveLength(1));
      expect(within(form).queryByRole('group', { name: "Who's hiring?" })).toBeNull();
      expect(bodies[0]).not.toHaveProperty('teamId');
      expect(bodies[0]).not.toHaveProperty('memberIds');
    });
  });
});
