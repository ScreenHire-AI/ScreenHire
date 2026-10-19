let baseUrl = '';

/**
 * Sets the origin requests are sent to. Server-side callers (Astro careers pages) pass the API URL;
 * the dashboard leaves it empty and relies on its dev-server proxy / same-origin deployment.
 */
export function configureApiClient(options: { baseUrl: string }): void {
  baseUrl = options.baseUrl.replace(/\/+$/, '');
}

/**
 * Orval mutator. Generated functions expect `{ data, status, headers }` and discriminate on `status`,
 * so non-2xx responses are returned rather than thrown.
 */
export async function apiFetch<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${baseUrl}${url}`, {
    ...init,
    headers: { Accept: 'application/json', ...init?.headers },
  });

  const text = [204, 205, 304].includes(response.status) ? '' : await response.text();
  const data: unknown = text ? JSON.parse(text) : undefined;

  return { data, status: response.status, headers: response.headers } as T;
}
