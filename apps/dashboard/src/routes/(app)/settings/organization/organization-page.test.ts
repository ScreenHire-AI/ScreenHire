import type { OrganizationProfile } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/organization') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate); the page reloads the session with invalidateAll after a rename.
const navigation = vi.hoisted(() => ({ invalidateAll: vi.fn(async () => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), invalidateAll: navigation.invalidateAll }));

const organization: OrganizationProfile = {
  id: '01a157d4-0000-7000-8000-0000000000bb',
  name: 'Brightwater Studio',
  slug: 'brightwater',
  lastUpdated: { at: '2026-10-01T09:00:00Z', by: null },
  homeRegion: 'eu',
  careersUrl: 'https://jobs.brightwater.example.com',
  createdAt: '2026-09-01T09:00:00Z',
  questionsPerPage: 8,
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

const name = () => screen.findByLabelText('Name') as Promise<HTMLInputElement>;

afterEach(() => {
  vi.unstubAllGlobals();
  navigation.invalidateAll.mockClear();
});

describe('organization settings page', () => {
  it('loads the name, with Updated and History on the title row, so every settings header is one row (T6, S8)', async () => {
    renderPage();

    expect(await screen.findByDisplayValue('Brightwater Studio')).toBe(await name());
    const updated = document.querySelector('[data-last-updated]')!;
    expect(updated).toHaveTextContent(/^Last updated:\s*Updated /);
    expect(updated.closest('[data-slot="header-band"]')?.querySelector('dl')).toBeNull();
    expect(screen.getByRole('button', { name: /^History/ })).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('says when it was created once: in the header until a rename, then in the careers panel (W7)', async () => {
    renderPage({ 'GET /v1/organization': () => Response.json({ ...organization, lastUpdated: { at: organization.createdAt, by: null, created: true } }) });

    await screen.findByDisplayValue('Brightwater Studio');
    expect(document.querySelector('[data-last-updated]')).toHaveTextContent(/^Created:\s*Created /);
    expect(screen.queryByText('Created', { selector: 'dt' })).toBeNull();
  });

  it('keeps Created in the careers panel once the header says Updated', async () => {
    renderPage();

    await screen.findByDisplayValue('Brightwater Studio');
    expect(screen.getByText('Created', { selector: 'dt' })).toBeInTheDocument();
  });

  it('links to the careers address the API gives, and shows the data region', async () => {
    renderPage();

    const link = await screen.findByRole('link', { name: /jobs\.brightwater\.example\.com/ });
    expect(link).toHaveAttribute('href', 'https://jobs.brightwater.example.com');
    expect(screen.getByText('EU')).toBeInTheDocument();
  });

  it('saves a new name with a PUT, then reloads the session so the sidebar shows it', async () => {
    const bodies = renderPage();
    await fireEvent.input(await name(), { target: { value: '  Brightwater Labs ' } });

    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(bodies).toEqual([{ name: 'Brightwater Labs', questionsPerPage: 8 }]));
    await waitFor(() => expect(navigation.invalidateAll).toHaveBeenCalledOnce());
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
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

  it('asks for a name and sends nothing when it is empty', async () => {
    const bodies = renderPage();
    await fireEvent.input(await name(), { target: { value: '   ' } });

    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText("Enter your organization's name.")).toBeInTheDocument();
    expect(bodies).toEqual([]);
    expect(navigation.invalidateAll).not.toHaveBeenCalled();
  });

  it('says why when the API refuses the rename', async () => {
    renderPage({ 'PUT /v1/organization': () => Response.json({ detail: 'Only owners and admins can rename.' }, { status: 403 }) });
    await fireEvent.input(await name(), { target: { value: 'Brightwater Labs' } });

    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Only owners and admins can rename.')).toBeInTheDocument();
    expect(navigation.invalidateAll).not.toHaveBeenCalled();
  });

  it('says why the organization could not load, with a retry', async () => {
    renderPage({ 'GET /v1/organization': () => Response.json({ detail: 'Try again later.' }, { status: 500 }) });

    expect(await screen.findByText('Try again later.')).toBeInTheDocument();
  });
});
