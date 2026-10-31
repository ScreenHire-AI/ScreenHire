import type { OrganizationProfile } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/careers-site') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).

const organization: OrganizationProfile = {
  id: '01a157d4-0000-7000-8000-0000000000bb',
  name: 'Brightwater Studio',
  slug: 'brightwater',
  lastUpdated: { at: '2026-10-01T09:00:00Z', by: null },
  homeRegion: 'eu',
  careersUrl: 'https://jobs.brightwater.example.com',
  createdAt: '2026-09-01T09:00:00Z',
  questionsPerPage: 8,
  embedOrigins: [],
};

function renderPage(routes: Parameters<typeof apiStub>[0] = {}) {
  const bodies: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/organization': () => Response.json(organization),
        'PUT /v1/organization': (init) => {
          const body = JSON.parse(String(init?.body)) as { name: string };
          bodies.push(body);
          return Response.json({ ...organization, name: body.name });
        },
        ...routes,
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return bodies;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('careers site settings page', () => {
  it('links to the careers address the API gives', async () => {
    renderPage();

    const link = await screen.findByRole('link', { name: /jobs\.brightwater\.example\.com/ });
    expect(link).toHaveAttribute('href', 'https://jobs.brightwater.example.com');
  });

  it('saves the questions per page a questionnaire without sections splits into', async () => {
    const bodies = renderPage();
    const field = (await screen.findByLabelText('Questions per page')) as HTMLInputElement;
    await waitFor(() => expect(field.value).toBe('8'));
    expect(screen.getByText('When a questionnaire has no sections, candidates see this many questions a page.')).toBeInTheDocument();

    await fireEvent.input(field, { target: { value: '12' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies).toEqual([{ name: 'Brightwater Studio', questionsPerPage: 12 }]));
  });

  it('keeps the questions per page from 1 to 50 and sends nothing outside that', async () => {
    const bodies = renderPage();
    const field = (await screen.findByLabelText('Questions per page')) as HTMLInputElement;
    await waitFor(() => expect(field.value).toBe('8'));

    await fireEvent.input(field, { target: { value: '80' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Choose from 1 to 50 questions a page.')).toBeInTheDocument();
    expect(bodies).toEqual([]);
  });

  it('adds a site that may embed the careers page and then shows the embed code', async () => {
    const sent: unknown[] = [];
    renderPage({
      'PUT /v1/organization/embedding': (init) => {
        const body = JSON.parse(String(init?.body)) as { origins: string[] };
        sent.push(body);
        return Response.json({ ...organization, embedOrigins: ['https://www.brightwater.example.com'] });
      },
    });
    expect(await screen.findByText('No sites yet.')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Embed code' })).toBeNull();

    await fireEvent.input(screen.getByLabelText('New site'), { target: { value: 'www.brightwater.example.com' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Add site' }));

    await waitFor(() => expect(sent).toEqual([{ origins: ['www.brightwater.example.com'] }]));
  });

  it('shows the embed code for the careers address, with Copy, once a site is on the list', async () => {
    renderPage({ 'GET /v1/organization': () => Response.json({ ...organization, embedOrigins: ['https://www.brightwater.example.com'] }) });

    expect(await screen.findByRole('list', { name: 'Sites that can embed the careers page' })).toHaveTextContent('https://www.brightwater.example.com');
    expect(screen.getByLabelText('Embed code')).toHaveTextContent(
      '<script src="https://jobs.brightwater.example.com/embed.js" async></script>',
    );
    expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument();
    expect(screen.getByText('Paste it into your site where the jobs should show.')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('removes a site by saving the list without it', async () => {
    const sent: unknown[] = [];
    renderPage({
      'GET /v1/organization': () => Response.json({ ...organization, embedOrigins: ['https://a.example.com', 'https://b.example.com'] }),
      'PUT /v1/organization/embedding': (init) => {
        sent.push(JSON.parse(String(init?.body)));
        return Response.json({ ...organization, embedOrigins: ['https://b.example.com'] });
      },
    });

    await fireEvent.click(await screen.findByRole('button', { name: 'Remove site https://a.example.com' }));

    await waitFor(() => expect(sent).toEqual([{ origins: ['https://b.example.com'] }]));
  });

  it("says why the API refused a site", async () => {
    renderPage({
      'PUT /v1/organization/embedding': () =>
        Response.json({ errors: { 'origins[0]': ['A site that embeds the careers page uses https.'] } }, { status: 400 }),
    });

    await fireEvent.input(await screen.findByLabelText('New site'), { target: { value: 'http://www.example.com' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Add site' }));

    expect(await screen.findByText('A site that embeds the careers page uses https.')).toBeInTheDocument();
  });

  it('says why the organization could not load, with a retry', async () => {
    renderPage({ 'GET /v1/organization': () => Response.json({ detail: 'Try again later.' }, { status: 500 }) });

    expect(await screen.findByText('Try again later.')).toBeInTheDocument();
  });
});
