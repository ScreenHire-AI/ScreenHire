import type { BrandingImage, ColorContrast, OrganizationBranding, OrganizationProfile } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/branding') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const logo: BrandingImage = {
  id: '0f8b3a52-6a4e-4a9b-9c1e-2d7f3e4b5a6c',
  kind: 'Logo',
  scanStatus: 'NotScanned',
  contentType: 'image/png',
  width: 400,
  height: 100,
  sizeBytes: 24_576,
  url: 'https://brightwater.careers.example.com/brand/images/0f8b3a52-6a4e-4a9b-9c1e-2d7f3e4b5a6c.png',
};
const saved: OrganizationBranding = {
  brandColor: '#1f4e5a',
  secondColor: null,
  headingFont: 'Fraunces',
  logo,
  favicon: null,
  sharingImage: null,
  lastUpdated: { at: null, by: 'ScreenHire' },
};
const unset: OrganizationBranding = { ...saved, brandColor: null, headingFont: null, logo: null };

const organization: OrganizationProfile = {
  id: '01a157d4-0000-7000-8000-0000000000bb',
  name: 'Brightwater Studio',
  slug: 'brightwater',
  lastUpdated: { at: '2026-10-01T09:00:00Z', by: null },
  homeRegion: 'eu',
  careersUrl: 'https://brightwater.careers.example.com',
  createdAt: '2026-09-01T09:00:00Z',
  questionsPerPage: 8,
};

const passing = (color: string): ColorContrast => ({
  color,
  passes: true,
  textColor: '#ffffff',
  pairs: [],
  suggestion: null,
  problems: [],
});

type Route = (init: RequestInit | undefined, url: URL) => Response | Promise<Response>;

// The contrast check answers for whatever colours it is asked about; other routes come from `routes`.
function renderPage(branding: OrganizationBranding, routes: Record<string, Route> = {}) {
  const bodies: Record<string, unknown>[] = [];
  const stub = apiStub({
    'GET /v1/organization/branding': () => Response.json(branding),
    'GET /v1/organization': () => Response.json(organization),
    'PUT /v1/organization/branding': (init) => {
      const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
      bodies.push(body);
      return Response.json({ ...branding, ...body });
    },
  });
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://dashboard.test');
    const key = `${(init?.method ?? 'GET').toUpperCase()} ${url.origin === 'http://dashboard.test' ? url.pathname : url.href}`;
    if (routes[key]) return routes[key](init, url);
    if (key === 'GET /v1/organization/branding/contrast') {
      const brand = url.searchParams.get('brandColor');
      const second = url.searchParams.get('secondColor');
      return Response.json({ brandColor: brand ? passing(brand) : null, secondColor: second ? passing(second) : null });
    }
    return stub(input, init);
  });
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return { bodies, fetchMock };
}

const brandField = () => screen.findByLabelText('Brand colour') as Promise<HTMLInputElement>;
const save = () => screen.findByRole('button', { name: 'Save branding' });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('branding page', () => {
  it('loads the saved colours, font and logo', async () => {
    renderPage(saved);

    await waitFor(async () => expect(await brandField()).toHaveValue('#1f4e5a'));
    expect(screen.getByLabelText<HTMLInputElement>('Second colour').value).toBe('');
    expect(screen.getByRole('radio', { name: 'Fraunces' })).toBeChecked();
    expect(screen.getAllByAltText('The logo')[0]).toHaveAttribute('src', logo.url);
    expect(screen.getByText(/^400 by 100 pixels, 25 kB$/)).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("offers ScreenHire's heading font while none is picked", async () => {
    renderPage(unset);

    expect(await screen.findByRole('radio', { name: /Bricolage Grotesque/ })).toBeChecked();
    expect(screen.getByText('No logo')).toBeInTheDocument();
    // An empty brand colour is ScreenHire's; an empty second colour is no header band at all.
    expect(screen.getByLabelText('Brand colour')).toHaveAttribute('placeholder', "ScreenHire's");
    expect(screen.getByLabelText('Second colour')).toHaveAttribute('placeholder', 'None');
  });

  it('says which pair a colour fails and fills in the nearest passing shade', async () => {
    renderPage(saved, {
      'GET /v1/organization/branding/contrast': (_, url) =>
        Response.json({
          brandColor:
            url.searchParams.get('brandColor') === '#ffd400'
              ? {
                  color: '#ffd400',
                  passes: false,
                  textColor: '#231f1a',
                  pairs: [],
                  suggestion: '#8a6d00',
                  problems: ['Links in #ffd400 are hard to read on the page: 1.4:1, and they need 4.5:1. #8a6d00 is the nearest shade that passes.'],
                }
              : passing(url.searchParams.get('brandColor') ?? ''),
          secondColor: null,
        }),
    });
    await fireEvent.input(await brandField(), { target: { value: 'ffd400' } });

    expect(await screen.findByText(/Links in #ffd400 are hard to read on the page/, {}, { timeout: 3000 })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Use #8a6d00' }));

    expect(await brandField()).toHaveValue('#8a6d00');
  });

  it('saves the whole branding, colours lower-case with their #', async () => {
    const { bodies } = renderPage(saved);
    await waitFor(async () => expect(await brandField()).toHaveValue('#1f4e5a'));
    await fireEvent.input(await brandField(), { target: { value: '2A6F7B' } });
    await fireEvent.input(screen.getByLabelText('Second colour'), { target: { value: '#E9DCC4' } });
    await fireEvent.click(screen.getByRole('radio', { name: 'Inter' }));

    await fireEvent.click(await save());

    await waitFor(() =>
      expect(bodies).toEqual([
        { brandColor: '#2a6f7b', secondColor: '#e9dcc4', headingFont: 'Inter', logoId: logo.id, faviconId: null, sharingImageId: null },
      ]),
    );
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
  });

  it('removes the logo on Save', async () => {
    const { bodies } = renderPage(saved);

    await fireEvent.click(await screen.findByRole('button', { name: 'Remove logo' }));
    await fireEvent.click(await save());

    await waitFor(() => expect(bodies[0]).toMatchObject({ logoId: null, headingFont: 'Fraunces' }));
  });

  it('uploads an image straight to storage and waits for its scan before Save', async () => {
    const ticket = {
      uploadId: '7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f',
      uploadUrl: 'http://storage.test/brand-images/upload?sig=x',
      method: 'PUT',
      headers: { 'x-ms-blob-type': 'BlockBlob' },
      expiresAt: '2026-10-30T10:00:00Z',
      maxBytes: 262_144,
    };
    const scanned = { ...logo, id: ticket.uploadId, kind: 'Favicon', width: 64, height: 64, sizeBytes: 2048 } satisfies BrandingImage;
    let scans = 0;
    const stored: BodyInit[] = [];
    const { bodies } = renderPage(unset, {
      'POST /v1/organization/branding/images': (init) => {
        expect(JSON.parse(String(init?.body))).toEqual({ kind: 'Favicon', fileName: 'icon.png', sizeBytes: 4 });
        return Response.json(ticket);
      },
      [`PUT ${ticket.uploadUrl}`]: (init) => {
        stored.push(init!.body!);
        return new Response(null, { status: 201 });
      },
      [`POST /v1/organization/branding/images/${ticket.uploadId}/verify`]: () =>
        Response.json({ ...scanned, scanStatus: 'Pending', url: null }),
      [`GET /v1/organization/branding/images/${ticket.uploadId}`]: () =>
        Response.json(++scans > 1 ? scanned : { ...scanned, scanStatus: 'Pending', url: null }),
    });
    const input = (await screen.findByLabelText('Favicon')) as HTMLInputElement;

    await fireEvent.change(input, { target: { files: [new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'icon.png', { type: 'image/png' })] } });

    expect(await screen.findByText('Scanning')).toBeInTheDocument();
    expect(stored).toHaveLength(1);
    expect(await save()).toBeDisabled();
    expect(screen.getByText('Save waits for the malware scan.')).toBeInTheDocument();

    await waitFor(() => expect(screen.queryByText('Scanning')).not.toBeInTheDocument(), { timeout: 4000 });
    await fireEvent.click(await save());
    await waitFor(() => expect(bodies[0]).toMatchObject({ faviconId: ticket.uploadId }));
  });

  it('says when the malware scan blocked an upload', async () => {
    renderPage({ ...saved, logo: { ...logo, scanStatus: 'Infected', url: null } });

    expect(await screen.findByText('This file did not pass the malware scan. Upload another logo.')).toBeInTheDocument();
  });

  it("puts a refused save's problems on their fields", async () => {
    renderPage(saved, {
      'PUT /v1/organization/branding': () =>
        Response.json(
          { title: 'One or more validation errors occurred.', errors: { secondColor: ['Text on a header in #888888 is hard to read: 3.5:1, and it needs 4.5:1.'] } },
          { status: 400 },
        ),
    });
    await fireEvent.input(await screen.findByLabelText('Second colour'), { target: { value: '#888888' } });

    await fireEvent.click(await save());

    expect(await screen.findByText(/Text on a header in #888888 is hard to read/)).toBeInTheDocument();
  });

  it('shows the field error and sends nothing for a value that is not a hex colour', async () => {
    const { bodies } = renderPage(saved);
    await fireEvent.input(await brandField(), { target: { value: 'teal' } });

    await fireEvent.click(await save());

    expect(await screen.findByText('Enter the brand colour as six hex digits, such as 1f4e5a.')).toBeInTheDocument();
    expect(bodies).toEqual([]);
  });

  it('previews the careers page and links to it', async () => {
    renderPage(saved);

    const preview = await screen.findByRole('region', { name: 'Careers page' });
    expect(within(preview).getAllByText(/Brightwater Studio/).length).toBeGreaterThan(0);
    expect(within(preview).getByText('Apply for this role')).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: /View/ })).toHaveAttribute('href', organization.careersUrl);
  });

  it('says why the branding could not load, with a retry', async () => {
    renderPage(saved, { 'GET /v1/organization/branding': () => Response.json({ detail: 'Try again later.' }, { status: 500 }) });

    expect(await screen.findByText('Try again later.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /retry|try again/i })).toBeInTheDocument();
  });
});
