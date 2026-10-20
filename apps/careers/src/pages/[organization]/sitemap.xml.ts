import type { APIRoute } from 'astro';
import { buildSitemap } from '../../lib/job-posting';
import { listPublicJobs } from '../../lib/api';

// One sitemap per tenant, on the careers origin its URLs live on (sitemaps may only list URLs of their own host). Rendered
// per request like the pages, so a closed job leaves it at once.
export const GET: APIRoute = async ({ params, url }) => {
  const response = await listPublicJobs(params.organization!);
  if (response.status !== 200) {
    return new Response(null, { status: 404 });
  }

  const board = response.data;
  return new Response(buildSitemap(`${url.origin}/${board.organizationSlug}`, board.jobs), {
    headers: { 'Content-Type': 'application/xml; charset=utf-8', 'Cache-Control': 'public, max-age=300' },
  });
};
