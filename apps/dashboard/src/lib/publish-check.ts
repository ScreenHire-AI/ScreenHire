import type { PublishCheckField, PublishCheckItem } from '@screenhire/api-client';
import { getGetJobPublishCheckQueryOptions } from '@screenhire/api-client/svelte-query';
import type { QueryClient } from '@tanstack/svelte-query';

/**
 * What to check before a job goes live (docs/architecture/job-distribution.md, "Check before publishing"). The API owns
 * the list, so the dashboard, an API key and the MCP server read the same one. It warns and never blocks.
 */

/** The `focus` value on the edit page for each field the check names (`JobDetailsForm` focuses its input). */
const focusNames: Record<PublishCheckField, string> = { Description: 'description', City: 'city', Salary: 'salary' };

/** Where Edit details goes for one field: the edit page, with that field focused. */
export function editHref(jobId: string, field: PublishCheckField): string {
  return `/jobs/${jobId}/edit?focus=${focusNames[field]}`;
}

/** The fields' words, for a link's accessible name ("Edit details, city"). */
export const fieldWords: Record<PublishCheckField, string> = { Description: 'description', City: 'city', Salary: 'salary' };

/** The header line's words: "1 thing to check before publishing". */
export function thingsToCheck(count: number): string {
  return `${count === 1 ? '1 thing' : `${count} things`} to check before publishing`;
}

/**
 * The job's check, fetched fresh, because the person may have just edited the job. A check that cannot be read gives
 * nothing to warn about: the publish must never wait on it.
 */
export async function fetchPublishCheck(queryClient: QueryClient, jobId: string): Promise<PublishCheckItem[]> {
  try {
    const result = await queryClient.fetchQuery(getGetJobPublishCheckQueryOptions(jobId, { query: { staleTime: 0, retry: false } }));
    return result.data.items;
  } catch {
    return [];
  }
}
