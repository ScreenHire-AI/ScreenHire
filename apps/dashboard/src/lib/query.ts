import { ApiError } from '@screenhire/api-client/svelte-query';
import { QueryClient } from '@tanstack/svelte-query';

/**
 * The dashboard's TanStack Query client. Data stays fresh for 30 s, so moving between pages reuses it, and live updates
 * (#lib/realtime.ts) mark it stale the moment something changes. A 4xx is an answer, not an outage, so it is not retried.
 */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: (failureCount, error) => !(error instanceof ApiError && error.status < 500) && failureCount < 2,
      },
    },
  });
}

/**
 * Marks stale, and refetches if shown, every query under an API path. The generated query keys are the path split into
 * segments (`/v1/jobs/{id}` is `['v1', 'jobs', id]`), so `/v1/jobs` covers the list, each job and its applications.
 */
export function invalidatePath(client: QueryClient, path: string): Promise<void> {
  return client.invalidateQueries({ queryKey: path.split('/').filter(Boolean) });
}

/** A sentence for a failed request: the API's own problem detail when it gave one. */
export function describeError(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    return error.body && typeof error.body === 'object' && 'detail' in error.body ? error.message : `${fallback} (HTTP ${error.status}).`;
  }

  return `${fallback}. Check your connection and try again.`;
}
