import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch, apiQueryFetch, ApiError, configureApiClient } from './http';

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

  it('sends the antiforgery cookie back as a header on requests that change state', async () => {
    vi.stubGlobal('document', { cookie: 'theme=dark; XSRF-TOKEN=CfDJ8%2Babc; sh.session=x' });
    fetchMock.mockImplementation(async () => new Response(null, { status: 204 }));

    await apiFetch<Result>('/v1/auth/sign-out', { method: 'POST' });
    await apiFetch<Result>('/v1/jobs');

    const headers = fetchMock.mock.calls.map(([, init]) => init?.headers as Record<string, string>);
    expect(headers[0]).toEqual({ Accept: 'application/json', 'X-XSRF-TOKEN': 'CfDJ8+abc' });
    expect(headers[1]).toEqual({ Accept: 'application/json' });
  });

  it('sends no antiforgery header before the API has issued a token', async () => {
    vi.stubGlobal('document', { cookie: 'theme=dark' });
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));

    await apiFetch<Result>('/v1/auth/sign-out', { method: 'POST' });

    expect(fetchMock.mock.calls[0]?.[1]?.headers).toEqual({ Accept: 'application/json' });
  });
});

describe('apiQueryFetch', () => {
  it('returns the same result as apiFetch for a 2xx response', async () => {
    fetchMock.mockResolvedValue(Response.json([{ id: 'a' }]));

    const result = await apiQueryFetch<Result>('/v1/jobs');

    expect(result.status).toBe(200);
    expect(result.data).toEqual([{ id: 'a' }]);
  });

  it("throws ApiError with the status and the problem's detail for any other status", async () => {
    fetchMock.mockResolvedValue(Response.json({ title: 'Conflict', detail: 'Someone else moved this application.' }, { status: 409 }));

    const error = await apiQueryFetch<Result>('/v1/applications/a/move', { method: 'POST' }).catch((thrown: unknown) => thrown);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, message: 'Someone else moved this application.' });
  });

  it('names the status when the body has no detail', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 503 }));

    await expect(apiQueryFetch<Result>('/v1/jobs')).rejects.toThrow('The API answered HTTP 503.');
  });
});
