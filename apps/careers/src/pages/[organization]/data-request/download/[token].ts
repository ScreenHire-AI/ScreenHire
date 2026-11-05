import type { APIRoute } from 'astro';
import { basePath, organizationOf } from '../../../../lib/host';
import { downloadLink } from '../../../../lib/data-request';

// The link in the email that says a copy of the candidate's data is ready. The careers server asks the API for a short-lived
// read link to the file and sends the browser there, so the address in the email stays on the careers site and the file's
// own address is never in an email. A link that no longer works lands on the expired page. Never cached, never sent on as a referrer.
export const GET: APIRoute = async ({ params, request, clientAddress }) => {
  const organization = organizationOf(request, params);
  const target = await downloadLink(organization, params.token, clientAddress);
  const base = basePath(request, organization);
  return new Response(null, {
    status: 302,
    headers: {
      Location: target ?? `${base}/data-request/expired`,
      'Cache-Control': 'no-store',
      'Referrer-Policy': 'no-referrer',
    },
  });
};
