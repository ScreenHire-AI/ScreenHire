import type { APIRoute } from 'astro';
import { z } from 'astro/zod';
import { checkUpload, forwardedFor, requestUpload } from '../../lib/apply-server';
import { organizationOf } from '../../lib/host';

// The upload island's two calls: `start` gets a link to send a file straight to storage, `verify` has the API check the
// file once it is there. A plain JSON endpoint rather than an Astro Action so the island ships a bare fetch instead of
// the actions client, which keeps the apply page inside its JavaScript budget. Browsers only send cross-origin JSON
// after a CORS preflight, which this route never answers, so other sites cannot call it.
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const request = z.discriminatedUnion('step', [
  z.object({
    step: z.literal('start'),
    jobId: z.string().regex(uuid),
    questionId: z.string().min(1).max(40),
    fileName: z.string().min(1).max(255),
    sizeBytes: z.number().int().positive(),
  }),
  z.object({
    step: z.literal('verify'),
    jobId: z.string().regex(uuid),
    questionId: z.string().min(1).max(40),
    uploadId: z.string().regex(uuid),
  }),
]);

export const POST: APIRoute = async ({ params, request: http, clientAddress }) => {
  if (!http.headers.get('content-type')?.startsWith('application/json')) {
    return new Response(null, { status: 415 });
  }

  const parsed = request.safeParse(await http.json().catch(() => null));
  if (!parsed.success) {
    return Response.json({ error: 'We could not upload this file. Try again in a moment.' }, { status: 400 });
  }

  const body = parsed.data;
  const organization = organizationOf(http, params);
  const forwarded = forwardedFor(clientAddress);
  const result =
    body.step === 'start'
      ? await requestUpload(organization, body.jobId, body.questionId, { fileName: body.fileName, sizeBytes: body.sizeBytes }, forwarded)
      : await checkUpload(organization, body.jobId, body.questionId, body.uploadId, forwarded);
  return Response.json(result, { status: 'error' in result ? 400 : 200, headers: { 'Cache-Control': 'no-store' } });
};
