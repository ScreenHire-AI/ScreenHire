import type { APIRoute } from 'astro';
import { buildSitemap } from '../../lib/job-posting';
import { listPublicJobs } from '../../lib/api';
import { organizationOf } from '../../lib/host';

// One sitemap per tenant, listing the URLs the API gives for its board and jobs (a sitemap may only list URLs of its own
// host, which the deployment's domain configuration makes this one). Rendered per request like the pages, so a closed
// job leaves it at once.
export const GET: APIRoute = async ({ params, request }) => {
  const response = await listPublicJobs(organizationOf(request, params));
  if (response.status !== 200) {
    return new Response(null, { status: 404 });
  }

  const board = response.data;
  return new Response(buildSitemap(board.url, board.jobs), {
    headers: { 'Content-Type': 'application/xml; charset=utf-8', 'Cache-Control': 'public, max-age=300' },
  });
};
