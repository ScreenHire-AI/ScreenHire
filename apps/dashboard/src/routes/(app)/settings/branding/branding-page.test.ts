import type { OrganizationBranding, OrganizationProfile } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/branding') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const palette = { brand: '#1f4e5a', onBrand: '#ffffff', brandText: '#1a4550', brandUi: '#4a8c9c' };
const saved: OrganizationBranding = { brandColor: '#1f4e5a', palette, brandAdjusted: false, lastUpdated: { at: null, by: 'ScreenHire' } };
const unset: OrganizationBranding = { brandColor: null, palette: null, brandAdjusted: false, lastUpdated: { at: null, by: 'ScreenHire' } };

const organization: OrganizationProfile = {
  id: '01a157d4-0000-7000-8000-0000000000bb',
  name: 'Brightwater Studio',
  slug: 'brightwater',
  lastUpdated: { at: '2026-10-01T09:00:00Z', by: null },
  homeRegion: 'eu',
  careersUrl: 'https://brightwater.careers.example.com',
  createdAt: '2026-09-01T09:00:00Z',
};

function renderPage(branding: OrganizationBranding, routes: Parameters<typeof apiStub>[0] = {}) {
  const bodies: unknown[] = [];
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/organization/branding': () => Response.json(branding),
      'GET /v1/organization': () => Response.json(organization),
      'PUT /v1/organization/branding': (init) => {
        const body = JSON.parse(String(init?.body)) as { brandColor: string | null };
        bodies.push(body);
        return Response.json({ brandColor: body.brandColor, palette: body.brandColor ? palette : null, brandAdjusted: false });
      },
      ...routes,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return { bodies, fetchMock };
}

const field = () => screen.findByLabelText('Hex colour') as Promise<HTMLInputElement>;
const save = () => screen.findByRole('button', { name: 'Save colour' });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('careers branding page', () => {
  it('loads the saved colour into the field', async () => {
    renderPage(saved);

    // The saved colour arrives after the first render. The colour picker holds it too, so ask the labelled field.
    await waitFor(async () => expect(await field()).toHaveValue('#1f4e5a'));
    expect(screen.getByLabelText('Pick a colour')).toHaveValue('#1f4e5a');
    await expectNoAxeViolations();
  });

  it('leaves the field empty and says which colours are used while none is saved', async () => {
    renderPage(unset);

    expect((await field()).value).toBe('');
    expect(await screen.findByText("ScreenHire's colours, until you save one.")).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Use the default' })).not.toBeInTheDocument();
  });

  it('shows the palette the careers pages will use', async () => {
    renderPage(saved);

    const panel = await screen.findByRole('region', { name: 'On your careers pages' });
    for (const [label, value] of [
      ['Buttons', palette.brand],
      ['Text on buttons', palette.onBrand],
      ['Links and headings', palette.brandText],
      ['Borders and focus', palette.brandUi],
    ] as const) {
      expect(within(panel).getByText(label)).toBeInTheDocument();
      expect(within(panel).getByText(value)).toBeInTheDocument();
    }
  });

  it('links to the careers page from the organization', async () => {
    renderPage(saved);

    const link = await screen.findByRole('link', { name: /View/ });
    expect(link).toHaveAttribute('href', organization.careersUrl);
  });

  it('shows the field error and sends nothing for a value that is not a hex colour', async () => {
    const { bodies } = renderPage(saved);
    await fireEvent.input(await field(), { target: { value: 'teal' } });

    await fireEvent.click(await save());

    expect(await screen.findByText('Enter a hex colour of six digits, such as 1f4e5a.')).toBeInTheDocument();
    expect(bodies).toEqual([]);
  });

  it('saves a colour as lower-case #rrggbb', async () => {
    const { bodies } = renderPage(saved);
    await fireEvent.input(await field(), { target: { value: '#2A6F7B' } });

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toEqual([{ brandColor: '#2a6f7b' }]));
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
  });

  it('adds the # to a colour typed without one', async () => {
    const { bodies } = renderPage(unset);
    await fireEvent.input(await field(), { target: { value: '2a6f7b' } });

    await fireEvent.click(await save());

    await waitFor(() => expect(bodies).toEqual([{ brandColor: '#2a6f7b' }]));
  });

  it('says when the server darkened the colour so text on it stays readable', async () => {
    renderPage(saved, {
      'PUT /v1/organization/branding': () => Response.json({ brandColor: '#ffee00', palette, brandAdjusted: true }),
    });
    await fireEvent.input(await field(), { target: { value: 'ffee00' } });

    await fireEvent.click(await save());

    expect(await screen.findByText(/Darkened so text on it stays readable/)).toBeInTheDocument();
  });

  it('says why when the API refuses the colour', async () => {
    renderPage(saved, { 'PUT /v1/organization/branding': () => Response.json({ detail: 'Branding is unavailable.' }, { status: 503 }) });
    await fireEvent.input(await field(), { target: { value: '2a6f7b' } });

    await fireEvent.click(await save());

    expect(await screen.findByText('Branding is unavailable.')).toBeInTheDocument();
  });

  it('clears the saved colour with Use the default, sending null', async () => {
    const { bodies } = renderPage(saved);

    await fireEvent.click(await screen.findByRole('button', { name: 'Use the default' }));

    await waitFor(() => expect(bodies).toEqual([{ brandColor: null }]));
    expect(await screen.findByText("Saved. Careers pages use ScreenHire's colours.")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByLabelText<HTMLInputElement>('Hex colour').value).toBe(''));
  });

  it('says why the branding could not load, with a retry', async () => {
    renderPage(saved, { 'GET /v1/organization/branding': () => Response.json({ detail: 'Try again later.' }, { status: 500 }) });

    expect(await screen.findByText('Try again later.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /retry|try again/i })).toBeInTheDocument();
  });
});
