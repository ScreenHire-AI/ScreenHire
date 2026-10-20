// Host-based tenant routing (ADR-0003). In production each tenant's careers board is its own host, such as
// acme.careers.example.com, which is how the API builds every careers URL (DomainOptions.CareersJobUrl, from
// ScreenHire:Domains:CareersHostPattern). The pages are routed by path (/{tenant}/jobs/{slug}), so the middleware maps
// a tenant host onto those routes. Pure, so it unit-tests without a server.

/**
 * The tenant whose host this is, for a pattern such as `{tenant}.careers.example.com`, or null when the host does not
 * match. A path-based pattern (`localhost:4322/{tenant}`, local development and tests) is not a host pattern, so it
 * never matches and path routing applies as it is.
 */
export function tenantFromHost(host: string | null, pattern: string | undefined): string | null {
  if (!host || !pattern || pattern.includes('/') || !pattern.includes('{tenant}')) {
    return null;
  }

  const [before, after] = pattern.toLowerCase().split('{tenant}');
  const candidate = host.toLowerCase();
  if (!candidate.startsWith(before) || !candidate.endsWith(after)) {
    return null;
  }

  const tenant = candidate.slice(before.length, candidate.length - after.length);
  // One DNS label of an organization slug: never a dot, so a deeper subdomain is not a tenant.
  return /^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$/.test(tenant) ? tenant : null;
}

/** The path route for a request on a tenant host: `/jobs/x` on acme's host is `/acme/jobs/x`. */
export function tenantPath(tenant: string, pathname: string): string {
  return pathname === '/' ? `/${tenant}` : `/${tenant}${pathname}`;
}
