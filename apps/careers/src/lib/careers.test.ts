import { describe, expect, it } from 'vitest';
import type { PublicBranding, PublicHeadingFont } from '@screenhire/api-client';
import { brandImagePath, brandStylesheet, headingFontPreload, parseBrandImageFile, parseHeadingFontFile } from './brand';
import { acceptedLabel, describeSize, fileProblem, parseUploadRef } from '@screenhire/ui/questions/files';
import { isFrameworkPath, tenantBase, tenantFromHost, tenantRoute } from './tenancy';

// A hosted heading font as the API sends it (docs/architecture/branding.md section 3).
const latin = 'U+0000-00FF,U+0131,U+2000-206F';
const lora: PublicHeadingFont = {
  id: 'lora',
  family: 'Lora',
  category: 'serif',
  bundled: false,
  files: [
    { fileName: 'latin-600.woff2', subset: 'latin', weight: '600', unicodeRange: latin },
    { fileName: 'latin-700.woff2', subset: 'latin', weight: '700', unicodeRange: latin },
    { fileName: 'latin-ext-700.woff2', subset: 'latin-ext', weight: '700', unicodeRange: 'U+0100-02BA' },
  ],
};

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

  const bundled = (id: string): PublicHeadingFont => ({ id, family: id, category: 'serif', bundled: true, files: [] });

  it("sets a bundled heading font from ScreenHire's own list, and the name changes with it", () => {
    const { css, hash } = brandStylesheet({ ...none, headingFont: bundled('fraunces') })!;

    expect(css).toContain('--sh-font-display:"Fraunces Variable", Georgia');
    expect(css).not.toContain('@font-face');
    expect(brandStylesheet({ ...none, headingFont: bundled('inter') })!.hash).not.toBe(hash);
  });

  it('declares a hosted font face per file on this origin, each for its subset, under a name from its id', () => {
    const { css } = brandStylesheet({ ...none, headingFont: { ...lora, family: 'Anything the API says' } })!;

    expect(css.match(/@font-face/g)).toHaveLength(3);
    expect(css).toContain(
      '@font-face{font-family:"ScreenHire heading lora";src:url("fonts/lora/latin-700.woff2") format("woff2");font-weight:700;font-style:normal;font-display:swap;unicode-range:U+0000-00FF,U+0131,U+2000-206F}',
    );
    expect(css).toContain('--sh-font-display:"ScreenHire heading lora", Georgia');
    expect(css).not.toContain('Anything the API says');
    expect(css).not.toMatch(/https?:/);
  });

  it('refuses anything but #rrggbb and a font of the shape the API sends, so nothing else reaches CSS', () => {
    expect(() => brandStylesheet({ ...branding, colors: { ...colors, brand: 'red;background:url(x)' } })).toThrow();
    expect(() => brandStylesheet({ ...none, header: { background: '#fff', foreground: '#000000' } })).toThrow();
    expect(() => brandStylesheet({ ...none, headingFont: bundled('Comic;}body{') })).toThrow();
    expect(() => brandStylesheet({ ...none, headingFont: bundled('lora') })).toThrow();
    expect(() => brandStylesheet({ ...none, headingFont: { ...lora, id: 'lora"){}' } })).toThrow();
    const file = lora.files[0];
    for (const bad of [
      { ...file, fileName: '../x.woff2' },
      { ...file, fileName: 'latin-700.woff2") ; x' },
      { ...file, weight: '700;color:red' },
      { ...file, unicodeRange: 'U+0000-00FF}body{color:red' },
    ]) {
      expect(() => brandStylesheet({ ...none, headingFont: { ...lora, files: [bad] } })).toThrow();
    }
  });
});

describe('heading font files', () => {
  it('preloads the latin file nearest 700 under the base, and nothing for a bundled font', () => {
    expect(headingFontPreload('/acme', { colors: null, header: null, headingFont: lora, logo: null, favicon: null, sharingImage: null })).toBe(
      '/acme/brand/fonts/lora/latin-700.woff2',
    );
    expect(
      headingFontPreload('', {
        colors: null,
        header: null,
        headingFont: { ...lora, files: [lora.files[0]] },
        logo: null,
        favicon: null,
        sharingImage: null,
      }),
    ).toBe('/brand/fonts/lora/latin-600.woff2');
    expect(
      headingFontPreload('/acme', {
        colors: null,
        header: null,
        headingFont: { id: 'inter', family: 'Inter', category: 'sans-serif', bundled: true, files: [] },
        logo: null,
        favicon: null,
        sharingImage: null,
      }),
    ).toBeNull();
  });

  it('reads the font and file from a careers path, and nothing else', () => {
    expect(parseHeadingFontFile('/acme/brand/fonts/roboto-slab/latin-ext-700.woff2')).toEqual({ id: 'roboto-slab', fileName: 'latin-ext-700.woff2' });
    expect(parseHeadingFontFile('/brand/fonts/lora/latin-600.woff2')).toEqual({ id: 'lora', fileName: 'latin-600.woff2' });
    expect(parseHeadingFontFile('/acme/brand/fonts/lora/LICENSE.txt')).toBeNull();
    expect(parseHeadingFontFile('/acme/brand/fonts/Lora/latin-600.woff2')).toBeNull();
    expect(parseHeadingFontFile('/acme/brand/fonts/lora/cyrillic-600.woff2')).toBeNull();
    expect(parseHeadingFontFile('/acme/brand/fonts/lora/latin-650.woff2')).toBeNull();
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
    expect(acceptedLabel(['.png', '.jpg', '.jpeg'])).toBe('PNG, JPG or JPEG');
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
    // No types or no limit given (the dashboard's resume, whose types the API names) takes any.
    expect(fileProblem({ name: 'cv.anything', size: max + 1 }, [])).toBeNull();
    expect(fileProblem({ name: 'cv.pdf', size: 0 }, [])).toBe('This file is empty.');
  });

  it('accepts only well-formed upload references', () => {
    const ref = { uploadId: '0199e8a0-0000-4000-8000-000000000001', fileName: 'cv.pdf', sizeBytes: 10 };
    expect(parseUploadRef(JSON.stringify(ref))).toEqual(ref);
    expect(parseUploadRef({ ...ref, uploadId: 'nope' })).toBeNull();
    expect(parseUploadRef('{')).toBeNull();
  });
});
