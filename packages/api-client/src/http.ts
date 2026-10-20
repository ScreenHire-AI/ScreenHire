let baseUrl = '';

/** The readable cookie the API puts its antiforgery request token in, and the header it expects it back in. */
const antiforgeryCookie = 'XSRF-TOKEN';
const antiforgeryHeader = 'X-XSRF-TOKEN';
const safeMethods = new Set(['GET', 'HEAD', 'OPTIONS']);

/**
 * Sets the origin requests are sent to. Server-side callers (Astro careers pages) pass the API URL;
 * the dashboard leaves it empty and relies on its dev-server proxy / same-origin deployment.
 */
export function configureApiClient(options: { baseUrl: string }): void {
  baseUrl = options.baseUrl.replace(/\/+$/, '');
}

/**
 * The API's antiforgery token for this browser, or undefined outside a browser or before the API has issued one
 * (`GET /v1/auth/providers` and `GET /v1/auth/me` do).
 */
function antiforgeryToken(): string | undefined {
  if (typeof document === 'undefined') {
    return undefined;
  }

  const prefix = `${antiforgeryCookie}=`;
  const cookie = document.cookie.split('; ').find((entry) => entry.startsWith(prefix));
  return cookie ? decodeURIComponent(cookie.slice(prefix.length)) : undefined;
}

/**
 * Orval mutator. Generated functions expect `{ data, status, headers }` and discriminate on `status`,
 * so non-2xx responses are returned rather than thrown. Requests that change state carry the antiforgery header
 * that the API requires on cookie-authenticated POSTs.
 */
export async function apiFetch<T>(url: string, init?: RequestInit): Promise<T> {
  const method = (init?.method ?? 'GET').toUpperCase();
  const token = safeMethods.has(method) ? undefined : antiforgeryToken();

  const response = await fetch(`${baseUrl}${url}`, {
    ...init,
    headers: { Accept: 'application/json', ...(token ? { [antiforgeryHeader]: token } : {}), ...init?.headers },
  });

  const text = [204, 205, 304].includes(response.status) ? '' : await response.text();
  const data: unknown = text ? JSON.parse(text) : undefined;

  return { data, status: response.status, headers: response.headers } as T;
}
