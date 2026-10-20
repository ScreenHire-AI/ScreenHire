import type { APIRoute } from 'astro';
import { getPublicOrganization } from '../../../lib/api';
import { brandStylesheet } from '../../../lib/brand';
import { organizationOf } from '../../../lib/host';

/**
 * The organization's brand stylesheet (design-system §7), linked by CareersLayout. The name carries a hash of the
 * content, so a match is cached for a year; a stale hash (the brand changed since the page rendered) still gets the
 * current colours, uncached.
 */
export const GET: APIRoute = async ({ params, request, url }) => {
  const response = await getPublicOrganization(organizationOf(request, params));
  // From the path, like the organization from the host: params can be stale after a tenant-host rewrite.
  const requested = /\/brand\/([0-9a-f]+)\.css$/.exec(url.pathname)?.[1] ?? params.hash;
  if (response.status !== 200 || !response.data.branding) {
    return new Response(null, { status: 404 });
  }

  const { css, hash } = brandStylesheet(response.data.branding);
  return new Response(css, {
    headers: {
      'Content-Type': 'text/css; charset=utf-8',
      'Cache-Control': hash === requested ? 'public, max-age=31536000, immutable' : 'no-cache',
    },
  });
};
