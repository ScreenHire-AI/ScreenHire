import { describe, expect, it } from 'vitest';
import { tenantFromHost, tenantPath } from './tenant-host';

describe('tenantFromHost', () => {
  const pattern = '{tenant}.careers.example.com';

  it('finds the tenant in its own host', () => {
    expect(tenantFromHost('acme.careers.example.com', pattern)).toBe('acme');
    expect(tenantFromHost('Harbour-Co.Careers.Example.com', pattern)).toBe('harbour-co');
  });

  it('matches a pattern with a port', () => {
    expect(tenantFromHost('acme.careers.localhost:4322', '{tenant}.careers.localhost:4322')).toBe('acme');
  });

  it.each([
    ['careers.example.com', 'the bare careers domain'],
    ['a.b.careers.example.com', 'a deeper subdomain'],
    ['acme.example.com', 'another domain'],
    ['-acme.careers.example.com', 'a label that cannot be a slug'],
  ])('ignores %s (%s)', (host) => {
    expect(tenantFromHost(host, pattern)).toBeNull();
  });

  it('never matches a path-based pattern, so local development routes by path', () => {
    expect(tenantFromHost('localhost:4322', 'localhost:4322/{tenant}')).toBeNull();
  });

  it('does nothing without a pattern or a host', () => {
    expect(tenantFromHost('acme.careers.example.com', undefined)).toBeNull();
    expect(tenantFromHost(null, pattern)).toBeNull();
  });
});

describe('tenantPath', () => {
  it('maps the board and its pages onto the path routes', () => {
    expect(tenantPath('acme', '/')).toBe('/acme');
    expect(tenantPath('acme', '/jobs/engineer')).toBe('/acme/jobs/engineer');
    expect(tenantPath('acme', '/sitemap.xml')).toBe('/acme/sitemap.xml');
  });
});
