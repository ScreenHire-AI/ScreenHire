// Tenant routing with this server's configuration: the same keys the API reads (ScreenHire:Domains:*), as environment
// variables. The rules themselves are in ./tenancy, which has no environment and unit-tests cleanly.
import { tenantBase, tenantFromHost } from './tenancy';

const hostPattern = process.env.ScreenHire__Domains__CareersHostPattern;
const careersDomain = process.env.ScreenHire__Domains__CareersDomain;

/**
 * The organization slug the request's host names, or null when the organization is in the path. Read from the Host
 * header, not `Astro.url`: Astro builds that URL from the Host header only for `security.allowedDomains`, a build-time
 * list, and otherwise uses `localhost`. The header only chooses which organization's pages render and how their links
 * look; nothing builds an absolute URL from it.
 */
export const hostTenant = (request: Request): string | null =>
  tenantFromHost(request.headers.get('host') ?? '', hostPattern, careersDomain);

/**
 * The prefix for an organization's links: empty on its own host, `/{organization}` otherwise. The request's headers
 * survive the middleware's rewrite (request locals do not).
 */
export const basePath = (request: Request, organization: string): string => tenantBase(hostTenant(request), organization);
