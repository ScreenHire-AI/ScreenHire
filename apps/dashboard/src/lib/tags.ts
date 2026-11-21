import type { ApplicationSummary } from '@screenhire/api-client';

const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();

/** The longest tag the API takes (JobApplication.MaxTagLength). */
export const maxTagLength = 50;

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

/** A tag the organization has (`GET /v1/tags`) and how many applications use it. */
export interface KnownTag {
  name: string;
  applications: number;
}

/** What the add field offers for the text typed so far: tags to pick, then, when it is new, Create for the text itself. */
export interface TagChoices {
  /** Existing tags, best first; Enter picks the first. */
  matches: string[];
  /** The typed text, when no tag has that name in any case and the record does not have it; the list's last option. */
  create: string | null;
}

/**
 * The suggestions in a tag field (design system section 11, Tag list): the organization's tags that contain the text,
 * ignoring case, most used first, leaving out the record's own. A tag whose whole name is the text comes first, so
 * Enter takes "senior" to "Senior" and never to a more popular "Senior engineer". Nothing typed offers the most used.
 * Create is offered only for a new name, so "Senior", "senior" and "SENIOR" stay one tag.
 */
export function suggestTags(known: readonly KnownTag[], typed: string, have: readonly string[], limit = 6): TagChoices {
  const text = typed.trim();
  const needle = text.toLowerCase();
  const exact = known.find((tag) => same(tag.name, text));
  const matches = known
    .filter((tag) => tag.name.toLowerCase().includes(needle) && !have.some((own) => same(own, tag.name)))
    .sort((a, b) => Number(b === exact) - Number(a === exact) || b.applications - a.applications || a.name.localeCompare(b.name))
    .slice(0, limit)
    .map((tag) => tag.name);
  const create = text && !exact && !have.some((own) => same(own, text)) ? text : null;
  return { matches, create };
}
