import { describe, expect, it } from 'vitest';
import { brandStylesheet } from './brand';
import { acceptedLabel, describeSize, fileProblem, parseUploadRef } from './files';
import { isFrameworkPath, tenantBase, tenantFromHost } from './tenancy';

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

  it('never rewrites what Astro serves itself, and prefixes links only without a tenant host', () => {
    expect(isFrameworkPath('/_astro/client.js')).toBe(true);
    expect(isFrameworkPath('/_actions/apply')).toBe(true);
    expect(isFrameworkPath('/jobs/designer')).toBe(false);
    expect(tenantBase('brightwater', 'brightwater')).toBe('');
    expect(tenantBase(null, 'brightwater')).toBe('/brightwater');
  });
});

describe('brandStylesheet', () => {
  const branding = { brand: '#1f4e5a', onBrand: '#ffffff', brandText: '#1f4e5a', brandUi: '#1f4e5a' };

  it('sets the candidate brand tokens and names itself by its content', () => {
    const { css, hash } = brandStylesheet(branding);

    expect(css).toContain('--candidate-cta:#1f4e5a;');
    expect(css).toContain('--candidate-cta-foreground:#ffffff;');
    expect(css).toContain('--candidate-brand-ui:#1f4e5a');
    expect(hash).toMatch(/^[0-9a-f]{16}$/);
    expect(brandStylesheet(branding).hash).toBe(hash);
    expect(brandStylesheet({ ...branding, brand: '#000000' }).hash).not.toBe(hash);
  });

  it('refuses anything but #rrggbb, so nothing else reaches CSS', () => {
    expect(() => brandStylesheet({ ...branding, brand: 'red;background:url(x)' })).toThrow();
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
