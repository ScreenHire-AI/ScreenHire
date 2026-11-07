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
  fileName: 'brightwater-logo.png',
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
  embedOrigins: [],
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
    // The drop area names the file and its size, with Remove; a click or a drop on it replaces the image.
    const tile = document.querySelector<HTMLElement>('[data-image-tile="logo"]')!;
    expect(within(tile).getByText('brightwater-logo.png')).toBeInTheDocument();
    expect(within(tile).getByText('24 KB')).toBeInTheDocument();
    expect(within(tile).getByText('400 by 100 pixels')).toBeInTheDocument();
    expect(within(tile).getByText('Replace logo')).toBeInTheDocument();
    expect(within(tile).getByRole('button', { name: 'Remove logo' })).toBeInTheDocument();
    expect(tile).not.toHaveAttribute('data-empty');
    expect(tile).toHaveAccessibleName('Logo');
    await expectNoAxeViolations();
  });

  it('draws each image where it is used: the logo on a light checkerboard, the favicon at 32px in a tab, the sharing image as a shared link', async () => {
    const image = (kind: BrandingImage['kind'], width: number, height: number, name: string): BrandingImage => ({
      ...logo,
      id: `${kind}-id`,
      kind,
      width,
      height,
      fileName: name,
      url: `https://brightwater.careers.example.com/brand/images/${kind}.png`,
    });
    renderPage({ ...saved, favicon: image('Favicon', 64, 64, 'tab.png'), sharingImage: image('SharingImage', 1200, 630, 'share.jpg') });

    const logoTile = await screen.findByText('brightwater-logo.png').then((name) => name.closest<HTMLElement>('[data-image-tile]')!);
    expect(logoTile.querySelector('[class*="conic-gradient"]')).not.toBeNull();
    const faviconTile = document.querySelector<HTMLElement>('[data-image-tile="favicon"]')!;
    expect(within(faviconTile).getByAltText('The favicon')).toHaveClass('size-8');
    expect(within(faviconTile).getByText('Jobs at Brightwater Studio')).toBeInTheDocument();
    const sharingTile = document.querySelector<HTMLElement>('[data-image-tile="sharing"]')!;
    expect(within(sharingTile).getByAltText('The sharing image').className).toContain('aspect-[1.91/1]');
    expect(within(sharingTile).getByText('Open roles at Brightwater Studio.')).toBeInTheDocument();
    expect(within(sharingTile).getByText('share.jpg')).toBeInTheDocument();
  });

  it("offers ScreenHire's heading font while none is picked", async () => {
    renderPage(unset);

    expect(await screen.findByRole('radio', { name: /Bricolage Grotesque/ })).toBeChecked();
    expect(screen.getByText('No logo yet')).toBeInTheDocument();
    // An empty tile is a drop area with Upload, not Replace or Remove.
    const tile = document.querySelector<HTMLElement>('[data-image-tile="logo"]')!;
    expect(tile).toHaveAttribute('data-empty');
    expect(tile.querySelector('[data-drop-area]')).toBeInTheDocument();
    expect(within(tile).getByText('Upload logo')).toBeInTheDocument();
    expect(within(tile).queryByText('Replace logo')).toBeNull();
    expect(within(tile).queryByRole('button', { name: 'Remove logo' })).toBeNull();
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
    const input = (await screen.findByLabelText('Favicon', { selector: 'input' })) as HTMLInputElement;
    expect(input).toHaveAttribute('type', 'file');

    await fireEvent.change(input, { target: { files: [new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'icon.png', { type: 'image/png' })] } });

    expect(await screen.findByText('Scanning')).toBeInTheDocument();
    expect(stored).toHaveLength(1);
    expect(await save()).toBeDisabled();
    expect(screen.getByText('Save waits for the malware scan.')).toBeInTheDocument();

    await waitFor(() => expect(screen.queryByText('Scanning')).not.toBeInTheDocument(), { timeout: 4000 });
    await fireEvent.click(await save());
    await waitFor(() => expect(bodies[0]).toMatchObject({ faviconId: ticket.uploadId }));
  });

  it('takes a file dropped on the drop area, empty or set, and refuses a wrong type or a file that is too large in words', async () => {
    const ticket = {
      uploadId: '7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f',
      uploadUrl: 'http://storage.test/brand-images/upload?sig=x',
      method: 'PUT',
      headers: {},
      expiresAt: '2026-10-30T10:00:00Z',
      maxBytes: 1_048_576,
    };
    const asked: unknown[] = [];
    renderPage(unset, {
      'POST /v1/organization/branding/images': (init) => {
        asked.push(JSON.parse(String(init?.body)));
        return Response.json(ticket);
      },
      [`PUT ${ticket.uploadUrl}`]: () => new Response(null, { status: 201 }),
      [`POST /v1/organization/branding/images/${ticket.uploadId}/verify`]: () => Response.json({ ...logo, id: ticket.uploadId, fileName: 'dropped.png' }),
    });
    await screen.findByText('No logo yet');
    const tile = document.querySelector<HTMLElement>('[data-image-tile="logo"]')!;
    const area = () => tile.querySelector<HTMLElement>('[data-drop-area]')!;
    const picture = (name: string, bytes = 4) => new File([new Uint8Array(bytes)], name, { type: 'image/png' });

    await fireEvent.drop(area(), { dataTransfer: { files: [picture('dropped.png')] } });

    await waitFor(() => expect(asked).toEqual([{ kind: 'Logo', fileName: 'dropped.png', sizeBytes: 4 }]));
    expect(await screen.findByText('dropped.png')).toBeInTheDocument();

    // A set tile takes a drop too: the new file replaces the image.
    await fireEvent.drop(area(), { dataTransfer: { files: [picture('second.png', 8)] } });
    await waitFor(() => expect(asked).toHaveLength(2));
    await waitFor(() => expect(screen.queryByText('Uploading...')).toBeNull());

    // A wrong type and a file over the logo's 1 MB are refused in the field's error, and nothing is sent.
    await fireEvent.drop(area(), { dataTransfer: { files: [new File(['x'], 'notes.txt', { type: 'text/plain' })] } });
    expect(await screen.findByText('Upload a PNG, JPG or JPEG file.')).toBeInTheDocument();
    await fireEvent.drop(area(), { dataTransfer: { files: [picture('huge.png', 1_048_577)] } });
    expect(await screen.findByText('This file is larger than 1 MB.')).toBeInTheDocument();
    expect(asked).toHaveLength(2);
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

  it('opens the preview on Phone, and on Desktop once the pane has room for a tablet frame', async () => {
    // The test setup gives every element a 1024px box, room enough for a tablet frame.
    renderPage(saved);

    const frame = await screen.findByRole('region', { name: 'Careers page' }).then((region) => region.querySelector('[data-preview-device]')!);
    await waitFor(() => expect(frame).toHaveAttribute('data-preview-device', 'desktop'));

    // A size the person picks stays.
    await fireEvent.click(screen.getAllByRole('button', { name: 'Phone' })[0]!);
    expect(frame).toHaveAttribute('data-preview-device', 'phone');
  });

  it('opens the preview on Phone where the pane is narrow', async () => {
    const width = vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(500);
    try {
      renderPage(saved);

      const region = await screen.findByRole('region', { name: 'Careers page' });
      expect(region.querySelector('[data-preview-device]')).toHaveAttribute('data-preview-device', 'phone');
    } finally {
      width.mockRestore();
    }
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
