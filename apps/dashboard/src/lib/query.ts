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

/**
 * A sentence for a failed request: the API's own problem detail when it gave one, or a validation problem's messages
 * (a publish the job is not ready for says what is missing).
 */
export function describeError(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    if (error.body && typeof error.body === 'object' && 'detail' in error.body) return error.message;
    const messages = validationMessages(error.body);
    return messages.length > 0 ? `${fallback}: ${messages.join(' ')}` : `${fallback} (HTTP ${error.status}).`;
  }

  return `${fallback}. Check your connection and try again.`;
}

// Every message of a validation problem (`errors`: field name to messages), each once, in the API's order.
function validationMessages(body: unknown): string[] {
  if (!body || typeof body !== 'object' || !('errors' in body) || !body.errors || typeof body.errors !== 'object') return [];
  const messages = Object.values(body.errors as Record<string, unknown>)
    .flatMap((value) => (Array.isArray(value) ? value : []))
    .filter((message): message is string => typeof message === 'string');
  return [...new Set(messages)];
}
