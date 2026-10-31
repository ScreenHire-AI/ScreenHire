import type { APIRoute } from 'astro';
import { getPublicOrganization } from '../../../lib/api';
import { brandStylesheet } from '../../../lib/brand';
import { organizationOf } from '../../../lib/host';

/**
 * The organization's brand stylesheet (docs/architecture/branding.md), linked by CareersLayout. The name carries a hash of the
 * content, so a match is cached for a year; a stale hash (the brand changed since the page rendered) still gets the
 * current colours, uncached.
 */
export const GET: APIRoute = async ({ params, request, url }) => {
  const response = await getPublicOrganization(organizationOf(request, params));
  // From the path, like the organization from the host: params can be stale after a tenant-host rewrite.
  const requested = /\/brand\/([0-9a-f]+)\.css$/.exec(url.pathname)?.[1] ?? params.hash;
  const sheet = response.status === 200 ? brandStylesheet(response.data.branding) : null;
  if (!sheet) {
    return new Response(null, { status: 404 });
  }

  const { css, hash } = sheet;
  return new Response(css, {
    headers: {
      'Content-Type': 'text/css; charset=utf-8',
      'Cache-Control': hash === requested ? 'public, max-age=31536000, immutable' : 'no-cache',
    },
  });
};
