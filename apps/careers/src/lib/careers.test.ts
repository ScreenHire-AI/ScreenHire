import { describe, expect, it } from 'vitest';
import type { PublicBranding } from '@screenhire/api-client';
import { brandImagePath, brandStylesheet, parseBrandImageFile } from './brand';
import { acceptedLabel, describeSize, fileProblem, parseUploadRef } from '@screenhire/ui/questions/files';
import { isFrameworkPath, tenantBase, tenantFromHost, tenantRoute } from './tenancy';

describe('tenantFromHost', () => {
  const pattern = '{tenant}.{CareersDomain}';

  it('reads the organization from a tenant host, port included', () => {
    expect(tenantFromHost('brightwater.careers.example.com', pattern, 'careers.example.com')).toBe('brightwater');
    expect(tenantFromHost('Acme-Labs.localhost:4322', pattern, 'localhost:4322')).toBe('acme-labs');
  });

  it('leaves the careers domain itself and other hosts to path-based routing', () => {
    expect(tenantFromHost('localhost:4322', pattern, 'localhost:4322')).toBeNull();
    expect(tenantFromHost('brightwater.localhost:4999', pattern, 'localhost:4322')).toBeNull();
    expect(tenantFromHost('a.b.localhost:4322', pattern, 'localhost:4322')).toBeNull();
    expect(tenantFromHost('evil.example.com', pattern, 'careers.example.com')).toBeNull();
  });

  it('turns host routing off for a path pattern or missing settings', () => {
    expect(tenantFromHost('brightwater.localhost:4322', '{CareersDomain}/{tenant}', 'localhost:4322')).toBeNull();
    expect(tenantFromHost('brightwater.localhost:4322', undefined, 'localhost:4322')).toBeNull();
    expect(tenantFromHost('brightwater.localhost:4322', pattern, undefined)).toBeNull();
  });

  it("renders a tenant host's paths under the organization, and sends prefixed paths to the canonical one", () => {
    expect(tenantRoute('/', 'brightwater')).toEqual({ render: '/brightwater' });
    expect(tenantRoute('/jobs/designer', 'brightwater')).toEqual({ render: '/brightwater/jobs/designer' });
    expect(tenantRoute('/brightwater/jobs/designer', 'brightwater')).toEqual({ canonical: '/jobs/designer' });
    expect(tenantRoute('/brightwater', 'brightwater')).toEqual({ canonical: '/' });
    expect(tenantRoute('/brightwaterish', 'brightwater')).toEqual({ render: '/brightwater/brightwaterish' });
  });

  it('never redirects a prefixed path off the tenant host', () => {
    expect(tenantRoute('/brightwater//evil.example/x', 'brightwater')).toEqual({ canonical: '/evil.example/x' });
    expect(tenantRoute('/brightwater/\\evil.example', 'brightwater')).toEqual({ canonical: '/evil.example' });
    expect(tenantRoute('/brightwater/', 'brightwater')).toEqual({ canonical: '/' });
  });

  it('never rewrites what Astro serves itself, and prefixes links only without a tenant host', () => {
    expect(isFrameworkPath('/_astro/client.js')).toBe(true);
    expect(isFrameworkPath('/_actions/apply')).toBe(true);
    expect(isFrameworkPath('/jobs/designer')).toBe(false);
    expect(tenantBase('brightwater', 'brightwater')).toBe('');
    expect(tenantBase(null, 'brightwater')).toBe('/brightwater');
  });
});

describe('brandStylesheet', () => {
  const none: PublicBranding = { colors: null, header: null, headingFont: null, logo: null, favicon: null, sharingImage: null };
  const colors = { brand: '#1f4e5a', onBrand: '#ffffff', brandText: '#1f4e5a', brandUi: '#1f4e5a' };
  const branding: PublicBranding = { ...none, colors };

  it('sets the candidate brand tokens and names itself by its content', () => {
    const { css, hash } = brandStylesheet(branding)!;

    expect(css).toContain('--candidate-cta:#1f4e5a;');
    expect(css).toContain('--candidate-cta-foreground:#ffffff;');
    expect(css).toContain('--candidate-brand-ui:#1f4e5a');
    expect(hash).toMatch(/^[0-9a-f]{16}$/);
    expect(brandStylesheet(branding)!.hash).toBe(hash);
    expect(brandStylesheet({ ...branding, colors: { ...colors, brand: '#000000' } })!.hash).not.toBe(hash);
  });

  it("has no stylesheet while everything is ScreenHire's", () => {
    expect(brandStylesheet(none)).toBeNull();
  });

  it('paints the header band in the second colour with its text colour', () => {
    const { css } = brandStylesheet({ ...none, header: { background: '#E9DCC4', foreground: '#231f1a' } })!;

    expect(css).toContain('--candidate-header:#e9dcc4;');
    expect(css).toContain('--candidate-header-foreground:#231f1a');
    expect(css).not.toContain('--candidate-cta');
  });

  it("sets the heading font from ScreenHire's own list, and the name changes with it", () => {
    const { css, hash } = brandStylesheet({ ...none, headingFont: 'Fraunces' })!;

    expect(css).toContain('--sh-font-display:"Fraunces Variable", Georgia');
    expect(brandStylesheet({ ...none, headingFont: 'Inter' })!.hash).not.toBe(hash);
  });

  it('refuses anything but #rrggbb and a known font, so nothing else reaches CSS', () => {
    expect(() => brandStylesheet({ ...branding, colors: { ...colors, brand: 'red;background:url(x)' } })).toThrow();
    expect(() => brandStylesheet({ ...none, header: { background: '#fff', foreground: '#000000' } })).toThrow();
    expect(() => brandStylesheet({ ...none, headingFont: 'Comic;}body{' as PublicBranding['headingFont'] })).toThrow();
  });
});

describe('brand images', () => {
  const logo = { id: '0f8b3a52-6a4e-4a9b-9c1e-2d7f3e4b5a6c', contentType: 'image/png', width: 400, height: 100, url: 'https://acme.careers.example.com/x' };

  it('are linked on the careers origin under the base path', () => {
    expect(brandImagePath('', logo)).toBe(`/brand/images/${logo.id}.png`);
    expect(brandImagePath('/acme', { ...logo, contentType: 'image/jpeg' })).toBe(`/acme/brand/images/${logo.id}.jpg`);
    expect(() => brandImagePath('', { ...logo, contentType: 'image/svg+xml' })).toThrow();
  });

  it('are read back from a path that names a uuid and a PNG or JPEG only', () => {
    expect(parseBrandImageFile(`/acme/brand/images/${logo.id}.png`)).toEqual({ id: logo.id, contentType: 'image/png' });
    expect(parseBrandImageFile(`/brand/images/${logo.id}.jpg`)).toEqual({ id: logo.id, contentType: 'image/jpeg' });
    expect(parseBrandImageFile(`/brand/images/${logo.id}.svg`)).toBeNull();
    expect(parseBrandImageFile('/brand/images/../secret.png')).toBeNull();
  });
});

describe('files', () => {
  it('describes types and sizes for candidates', () => {
    expect(acceptedLabel(['.pdf', '.docx'])).toBe('PDF or DOCX');
    expect(describeSize(10 * 1024 * 1024)).toBe('10 MB');
    expect(describeSize(1536 * 1024)).toBe('1.5 MB');
    expect(describeSize(300)).toBe('1 KB');
  });

  it('refuses a wrong type, an empty file and an oversized one before anything is sent', () => {
    const max = 10 * 1024 * 1024;
    expect(fileProblem({ name: 'cv.exe', size: 10 }, ['.pdf'], max)).toBe('Upload a PDF file.');
    expect(fileProblem({ name: 'cv.PDF', size: 0 }, ['.pdf'], max)).toBe('This file is empty.');
    expect(fileProblem({ name: 'cv.pdf', size: max + 1 }, ['.pdf'], max)).toBe('This file is larger than 10 MB.');
    expect(fileProblem({ name: 'cv.pdf', size: max }, ['.pdf'], max)).toBeNull();
  });

  it('accepts only well-formed upload references', () => {
    const ref = { uploadId: '0199e8a0-0000-4000-8000-000000000001', fileName: 'cv.pdf', sizeBytes: 10 };
    expect(parseUploadRef(JSON.stringify(ref))).toEqual(ref);
    expect(parseUploadRef({ ...ref, uploadId: 'nope' })).toBeNull();
    expect(parseUploadRef('{')).toBeNull();
  });
});
