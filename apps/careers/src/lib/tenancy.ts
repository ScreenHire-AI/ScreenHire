// Tenant routing for careers pages. Pure functions: no environment, so they unit-test cleanly.
//
// ScreenHire:Domains:CareersHostPattern (the same setting the API reads, ADR-0003) says where a tenant's pages live.
// `{tenant}.{CareersDomain}` puts each organization on its own host; anything with a path in it, such as
// `{CareersDomain}/{tenant}`, keeps the organization in the path. The path form also always works as a fallback, which is
// how local development without per-tenant hosts reaches a board.

const slug = '[a-z0-9]+(?:-[a-z0-9]+)*';

/** The organization slug that `host` names under `pattern`, or null when the host is not a tenant host. */
export function tenantFromHost(host: string, pattern: string | undefined, careersDomain: string | undefined): string | null {
  if (!pattern || !careersDomain) return null;
  const resolved = pattern.replaceAll('{CareersDomain}', careersDomain);
  if (resolved.includes('/') || !resolved.includes('{tenant}')) return null;
  const [before, after] = resolved.split('{tenant}', 2) as [string, string];
  const match = new RegExp(`^${escape(before)}(${slug})${escape(after)}$`, 'i').exec(host);
  return match ? match[1]!.toLowerCase() : null;
}

/** Paths Astro serves itself; they are the same on every host and are never rewritten to a tenant. */
export const isFrameworkPath = (path: string): boolean => /^\/(_astro|_actions|_server-islands)\//.test(path) || path === '/favicon.ico';

/**
 * What a request on `tenant`'s own host maps to: the path to render (`/jobs/x` renders `/{tenant}/jobs/x`), or, for a
 * path that already names the tenant (`/{tenant}/jobs/x`, a path-style link followed on a tenant host), the canonical
 * path without it.
 */
export function tenantRoute(path: string, tenant: string): { render: string } | { canonical: string } {
  const prefix = `/${tenant}`;
  if (path === prefix || path.startsWith(`${prefix}/`)) {
    // One leading slash only: `/{tenant}//evil.example` must not become `//evil.example`, which browsers follow to
    // another site.
    return { canonical: `/${path.slice(prefix.length).replace(/^[/\\]+/, '')}` };
  }

  return { render: `${prefix}${path === '/' ? '' : path}` };
}

/**
 * The path prefix of an organization's pages: empty on its own host, `/{organization}` otherwise. Every link a careers
 * page renders starts with it.
 */
export const tenantBase = (hostTenant: string | null, organization: string): string => (hostTenant ? '' : `/${organization}`);

const escape = (text: string): string => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
