import { describe, expect, it } from 'vitest';
import { POST } from '../pages/[organization]/telemetry';
import { MAX_TELEMETRY_BYTES } from './telemetry';

const JOB = '0199e8a0-0000-7000-8000-00000000bbbb';

// The refusals that happen before anything is passed on to the API.
const relay = (headers: Record<string, string>, body = '{}') => {
  const url = new URL(`http://careers.test/brightwater/telemetry?job=${JOB}`);
  const request = new Request(url, { method: 'POST', headers: { 'content-type': 'text/plain;charset=UTF-8', ...headers }, body });
  return POST({ params: { organization: 'brightwater' }, request, clientAddress: '203.0.113.5', url } as unknown as Parameters<typeof POST>[0]);
};

describe('telemetry relay', () => {
  it('refuses a body without a length, which it would otherwise buffer whole', async () => {
    expect((await relay({})).status).toBe(411);
  });

  it('refuses a body over the API limit', async () => {
    expect((await relay({ 'content-length': String(MAX_TELEMETRY_BYTES + 1) })).status).toBe(413);
  });

  it('refuses another site and anything but a snapshot', async () => {
    expect((await relay({ 'content-length': '2', 'sec-fetch-site': 'cross-site' })).status).toBe(403);
    expect((await relay({ 'content-length': '2' })).status).toBe(400);
  });
});
