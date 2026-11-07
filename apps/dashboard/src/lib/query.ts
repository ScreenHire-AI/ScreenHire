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

const planProblemTypes = {
  'urn:screenhire:problem:plan-limit': 'plan-limit',
  'urn:screenhire:problem:subscription-ended': 'subscription-ended',
} as const;

/** Why a draft could not be published for billing reasons: the plan's open job limit, or the subscription that ended. */
export interface PlanProblem {
  kind: (typeof planProblemTypes)[keyof typeof planProblemTypes];
  /** The API's plain-language sentence, which already says who can fix it. */
  detail: string;
}

/**
 * The billing problem in a failed publish (HTTP 402 with the plan-limit or subscription-ended problem type), or null for any
 * other error. A publish site shows it as a notice with an upgrade link for an owner, in place of its plain failure line.
 */
export function planProblem(error: unknown): PlanProblem | null {
  if (!(error instanceof ApiError) || error.status !== 402 || !error.body || typeof error.body !== 'object') return null;
  const { type, detail } = error.body as { type?: unknown; detail?: unknown };
  const kind = typeof type === 'string' ? (planProblemTypes as Record<string, PlanProblem['kind'] | undefined>)[type] : undefined;
  if (!kind) return null;
  const fallback = kind === 'plan-limit' ? 'Your plan has no room for another open job.' : 'Your subscription has ended, so no new job can be published.';
  return { kind, detail: typeof detail === 'string' && detail ? detail : fallback };
}
