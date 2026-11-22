import type { APIRoute } from 'astro';
import { apiBaseUrl, getGetPublicHeadingFontFileUrl } from '../../../../../lib/api';
import { parseHeadingFontFile } from '../../../../../lib/brand';

/**
 * A hosted heading font's file on the careers origin (docs/architecture/branding.md section 3, ADR-0031), so the content
 * security policy keeps `font-src 'self'` and a candidate's browser never asks a font host for anything. The bytes come
 * from the API's public route, which serves only the files of a family it hosts. A family's files never change under
 * their name, so they are cached for a year. The file is the same for every organization, so the path's organization
 * is not checked.
 */
export const GET: APIRoute = async ({ url }) => {
  // From the path: params can be stale after a tenant-host rewrite.
  const file = parseHeadingFontFile(url.pathname);
  if (!file) {
    return new Response(null, { status: 404 });
  }

  // The generated client reads JSON; a font's bytes are fetched from its generated address. A failed answer is never
  // cached, so a moment's outage does not stick to a file that will be there.
  let response: Response;
  try {
    response = await fetch(`${apiBaseUrl}${getGetPublicHeadingFontFileUrl(file.id, file.fileName)}`);
  } catch {
    return new Response(null, { status: 502, headers: { 'Cache-Control': 'no-store' } });
  }
  const type = response.headers.get('content-type')?.split(';')[0].trim();
  if (response.status !== 200 || type !== 'font/woff2') {
    return new Response(null, { status: response.status >= 500 ? 502 : 404, headers: { 'Cache-Control': 'no-store' } });
  }

  return new Response(response.body, {
    headers: {
      'Content-Type': 'font/woff2',
      'Cache-Control': 'public, max-age=31536000, immutable',
      'X-Content-Type-Options': 'nosniff',
    },
  });
};
