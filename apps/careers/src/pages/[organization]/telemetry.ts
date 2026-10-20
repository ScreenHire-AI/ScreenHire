import type { APIRoute } from 'astro';
import { sendPublicTelemetry } from '../../lib/api';
import { forwardedFor } from '../../lib/apply-server';
import { uuid } from '@screenhire/ui/questions/files';
import { organizationOf } from '../../lib/host';
import { MAX_TELEMETRY_BYTES, parseSnapshot } from '../../lib/telemetry';

// The telemetry module's beacons (packages/telemetry; integrity-engine.md §10.2), passed on to the API's ingest with the
// candidate's address. navigator.sendBeacon posts a string, so the body arrives as text/plain. Only this site's own pages
// may post here (Sec-Fetch-Site), and a snapshot binds to nothing without the session token the API issued for the job.
// The answer is always 204: a beacon never reads it, and the page never learns anything from it.
const done = () => new Response(null, { status: 204, headers: { 'Cache-Control': 'no-store' } });

export const POST: APIRoute = async ({ params, request, clientAddress, url }) => {
  const site = request.headers.get('sec-fetch-site');
  if (site !== null && site !== 'same-origin') return new Response(null, { status: 403 });
  if (!request.headers.get('content-type')?.startsWith('text/plain')) return new Response(null, { status: 415 });
  // sendBeacon always sends a length. Without one (a chunked body), request.text() would buffer whatever arrives.
  const length = Number(request.headers.get('content-length') ?? Number.NaN);
  if (!Number.isFinite(length)) return new Response(null, { status: 411 });
  if (length > MAX_TELEMETRY_BYTES) return new Response(null, { status: 413 });

  const jobId = url.searchParams.get('job') ?? '';
  const body = await request.text();
  const snapshot = uuid.test(jobId) ? parseSnapshot(body) : null;
  if (!snapshot) return new Response(null, { status: 400 });

  await sendPublicTelemetry(organizationOf(request, params), jobId, snapshot, forwardedFor(clientAddress)).catch(() => undefined);
  return done();
};
