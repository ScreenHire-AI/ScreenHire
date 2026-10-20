import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch, configureApiClient } from './http';

type Result = { data: unknown; status: number; headers: Headers };

const fetchMock = vi.fn<typeof fetch>();

beforeEach(() => {
  configureApiClient({ baseUrl: '' });
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  fetchMock.mockReset();
  vi.unstubAllGlobals();
});

describe('apiFetch', () => {
  it('returns the parsed body, status and headers', async () => {
    fetchMock.mockResolvedValue(Response.json({ title: 'Senior .NET Engineer' }, { headers: { 'x-request-id': 'abc' } }));

    const result = await apiFetch<Result>('/v1/jobs');

    expect(result.status).toBe(200);
    expect(result.data).toEqual({ title: 'Senior .NET Engineer' });
    expect(result.headers.get('x-request-id')).toBe('abc');
  });

  it('is same-origin until configured, then prefixes the base URL without doubling slashes', async () => {
    fetchMock.mockImplementation(async () => Response.json([]));

    await apiFetch<Result>('/v1/jobs');
    configureApiClient({ baseUrl: 'http://api.internal:5080//' });
    await apiFetch<Result>('/v1/jobs');

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual(['/v1/jobs', 'http://api.internal:5080/v1/jobs']);
  });

  it("asks for JSON and keeps the caller's method and headers", async () => {
    fetchMock.mockResolvedValue(Response.json({}));

    await apiFetch<Result>('/v1/jobs', { method: 'POST', headers: { 'Content-Type': 'application/json' } });

    expect(fetchMock).toHaveBeenCalledWith('/v1/jobs', {
      method: 'POST',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
    });
  });

  it('returns error responses instead of throwing, so generated clients can discriminate on status', async () => {
    fetchMock.mockResolvedValue(Response.json({ title: 'Not Found' }, { status: 404 }));

    const result = await apiFetch<Result>('/v1/public/organizations/missing/jobs');

    expect(result.status).toBe(404);
    expect(result.data).toEqual({ title: 'Not Found' });
  });

  it.each([204, 205, 304])('leaves data undefined for a %i response', async (status) => {
    fetchMock.mockResolvedValue(new Response(null, { status }));

    const result = await apiFetch<Result>('/v1/jobs');

    expect(result.status).toBe(status);
    expect(result.data).toBeUndefined();
  });

  it('leaves data undefined for an empty 200 body', async () => {
    fetchMock.mockResolvedValue(new Response('', { status: 200 }));

    const result = await apiFetch<Result>('/v1/jobs');

    expect(result.data).toBeUndefined();
  });
});
