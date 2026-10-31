import type { APIRoute } from 'astro';
import { apiBaseUrl, getGetPublicBrandImageUrl } from '../../../../lib/api';
import { parseBrandImageFile } from '../../../../lib/brand';
import { organizationOf } from '../../../../lib/host';

/**
 * A brand image (the logo, the favicon, the sharing image) on the careers origin, so the content security policy keeps
 * `img-src 'self'` (docs/architecture/branding.md). The bytes come from the API's public route, which serves only an
 * image that passed its scan. The address names one image whose bytes never change, so it is cached for a year, and it
 * is the same for every visitor and every email: nothing in it names a person.
 */
export const GET: APIRoute = async ({ params, request, url }) => {
  // From the path, like the organization from the host: params can be stale after a tenant-host rewrite.
  const file = parseBrandImageFile(url.pathname);
  if (!file) {
    return new Response(null, { status: 404 });
  }

  // The generated client reads JSON; an image's bytes are fetched from its generated address.
  const response = await fetch(`${apiBaseUrl}${getGetPublicBrandImageUrl(organizationOf(request, params), file.id)}`);
  const type = response.headers.get('content-type')?.split(';')[0].trim();
  if (response.status !== 200 || type !== file.contentType) {
    return new Response(null, { status: 404 });
  }

  return new Response(response.body, {
    headers: {
      'Content-Type': file.contentType,
      'Cache-Control': 'public, max-age=31536000, immutable',
    },
  });
};
