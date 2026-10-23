import type { ApplicationSummary } from '@screenhire/api-client';

const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();

/** Every tag on these applications, each once (tags compare ignoring case, as the API does), in alphabetical order. */
export function tagsOf(applications: readonly Pick<ApplicationSummary, 'tags'>[]): string[] {
  const tags: string[] = [];
  for (const tag of applications.flatMap((application) => application.tags)) {
    if (!tags.some((known) => same(known, tag))) tags.push(tag);
  }
  return tags.sort((a, b) => a.localeCompare(b));
}

/** Whether the application has `tag`; no tag (null) matches every application. */
export const matchesTag = (application: Pick<ApplicationSummary, 'tags'>, tag: string | null): boolean =>
  tag === null || application.tags.some((candidate) => same(candidate, tag));
