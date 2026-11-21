import type { TemplateUse, UsageItem } from '@screenhire/api-client';

// The Templates page (design system section 21): one tab per kind of template, each listing its templates with where
// they are used, filtered by All, In use, Drafts (hiring templates only) and Archived. Stale templates are found by sorting Last used: a "Not used in 6
// months" pill counted 0 beside rows that said Never (a template younger than that never counted), so it went (the owner's
// walk-through, 2026-11-21).

export type TemplateKind = 'hiring' | 'scorecards' | 'emails';

export const templateKinds: readonly TemplateKind[] = ['hiring', 'scorecards', 'emails'];

/** The tab's label and what its templates are called. */
export const templateKindViews: Record<TemplateKind, { label: string; noun: string }> = {
  hiring: { label: 'Hiring', noun: 'Hiring templates' },
  scorecards: { label: 'Scorecards', noun: 'Scorecards' },
  emails: { label: 'Emails', noun: 'Email templates' },
};

/**
 * The longest name each kind takes: the API's limits, from `HiringTemplateConfiguration`,
 * `ScorecardTemplate.MaxNameLength` and `EmailTemplate.MaxNameLength`.
 */
export const maxNameLength: Record<TemplateKind, number> = { hiring: 120, scorecards: 80, emails: 100 };

/** The Templates page's address, on a kind's tab. */
export const templatesHref = (kind: TemplateKind) => (kind === 'hiring' ? '/templates' : `/templates?tab=${kind}`);

/** A template's editor. */
export function templateHref(kind: TemplateKind, id: string): string {
  if (kind === 'hiring') return `/templates/${id}`;
  return `/templates/${kind}/${id}`;
}

/** The name a copy takes: "<name> (copy)", the name cut so the whole fits in `max` characters. */
export function copyName(name: string, max: number): string {
  const suffix = ' (copy)';
  const room = max - suffix.length;
  return `${name.length > room ? name.slice(0, room).trimEnd() : name}${suffix}`;
}

export type TemplateFilter = 'all' | 'in-use' | 'drafts' | 'archived';

export const templateFilters: readonly { value: TemplateFilter; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'in-use', label: 'In use' },
  { value: 'drafts', label: 'Drafts' },
  { value: 'archived', label: 'Archived' },
];

/** The filters a kind's tab offers: only hiring templates have versions, so only they have Drafts. */
export const filtersFor = (kind: TemplateKind) => templateFilters.filter((filter) => filter.value !== 'drafts' || kind === 'hiring');

/** What a filter needs to know of a template. */
export interface FilterableTemplate {
  archived: boolean;
  usage: TemplateUse | undefined;
  /** A hiring template's draft version, when it has one. */
  draft?: number | null;
}

/**
 * Whether a template shows under a filter. All, In use and Drafts leave archived templates out; Archived shows only them.
 * In use: something uses it now. Drafts: a hiring template with a draft version.
 */
export function matchesFilter(template: FilterableTemplate, filter: TemplateFilter): boolean {
  if (filter === 'archived') return template.archived;
  if (template.archived) return false;
  if (filter === 'in-use') return (template.usage?.usedBy.length ?? 0) > 0;
  if (filter === 'drafts') return template.draft != null;
  return true;
}

/** How many templates each filter shows, for its count badge. */
export function filterCounts(templates: readonly FilterableTemplate[]): Record<TemplateFilter, number> {
  return Object.fromEntries(templateFilters.map(({ value }) => [value, templates.filter((template) => matchesFilter(template, value)).length])) as Record<
    TemplateFilter,
    number
  >;
}

/** What a hiring template's Status says: its icon and colour (`#lib/status.ts`) and the words. */
export function templateStatus(template: { archived: boolean; published?: number | null; draft?: number | null }): {
  status: 'published' | 'draft' | 'archived';
  label: string;
} {
  if (template.archived) return { status: 'archived', label: 'Archived' };
  if (template.published == null) return { status: 'draft', label: 'Draft' };
  return template.draft == null ? { status: 'published', label: 'Published' } : { status: 'published', label: 'Published, draft open' };
}

/** What Used by counts for each kind. */
export const usedByNouns: Record<TemplateKind, { one: string; other: string }> = {
  hiring: { one: 'job', other: 'jobs' },
  scorecards: { one: 'hiring template', other: 'hiring templates' },
  emails: { one: 'automation', other: 'automations' },
};

/** Used by in a table cell: "None", "1 job", "3 jobs". */
export function usedByLabel(kind: TemplateKind, count: number): string {
  if (count === 0) return 'None';
  const nouns = usedByNouns[kind];
  return `${count} ${count === 1 ? nouns.one : nouns.other}`;
}

/** The jobs a hiring template's newest published version is newer than: they have an update to review. */
export function olderJobs(usedBy: readonly UsageItem[], liveVersion: number | null | undefined): UsageItem[] {
  if (liveVersion == null) return [];
  return usedBy.filter((item) => item.kind === 'Job' && item.templateVersion != null && item.templateVersion < liveVersion);
}

/** Used by with the jobs left on an older version: "3 jobs, 1 on an older version". Other kinds say the count. */
export function usedByWords(kind: TemplateKind, usedBy: readonly UsageItem[], liveVersion?: number | null): string {
  const count = usedByLabel(kind, usedBy.length);
  const older = kind === 'hiring' ? olderJobs(usedBy, liveVersion).length : 0;
  if (older === 0) return count;
  return `${count}, ${older} on ${older === 1 ? 'an older version' : 'older versions'}`;
}

/** The notice above a hiring template's tabs while jobs are on an older version: "1 job uses an older version." */
export const olderJobsWords = (count: number): string => (count === 1 ? '1 job uses an older version.' : `${count} jobs use an older version.`);

/**
 * What publishing does, beside Publish: the version new jobs get, and that the template's jobs keep theirs until someone
 * updates them. A template no job uses says only the first sentence.
 */
export function publishWords(version: number, jobs: readonly Pick<UsageItem, 'kind' | 'templateVersion'>[]): string {
  const first = `Publishing makes version ${version} the version new jobs get.`;
  const own = jobs.filter((item) => item.kind === 'Job');
  if (own.length === 0) return first;
  const versions = new Set(own.map((item) => item.templateVersion));
  const [only] = versions;
  if (own.length === 1) return `${first} Your 1 job stays on version ${only ?? 1} until you update it.`;
  if (versions.size === 1 && only != null) return `${first} Your ${own.length} jobs stay on version ${only} until you update them.`;
  return `${first} Your ${own.length} jobs stay on the versions they have until you update them.`;
}

/** Where a thing that uses a template lives: a job, its workflow, or a hiring template. */
export function usageHref(item: UsageItem): string {
  switch (item.kind) {
    case 'Job':
      return `/jobs/${item.id}`;
    case 'JobAutomation':
      return `/jobs/${item.id}/workflow`;
    default:
      return `/templates/${item.id}`;
  }
}

/** A thing that uses a template, in words: the job or template, and for an automation its name after a colon. */
export const usageLabel = (item: UsageItem): string => (item.automationName ? `${item.name}: ${item.automationName}` : item.name);

/** A subject in pieces: plain text, and each `{{ variable }}` as its label, which reads as a muted word (L1). */
export function subjectParts(subject: string, labels: ReadonlyMap<string, string>): { text: string; variable: boolean }[] {
  const parts: { text: string; variable: boolean }[] = [];
  let last = 0;
  for (const match of subject.matchAll(/\{\{\s*([\w.]+)\s*\}\}/g)) {
    if (match.index > last) parts.push({ text: subject.slice(last, match.index), variable: false });
    const name = match[1];
    parts.push({ text: (labels.get(name) ?? name).toLocaleLowerCase(), variable: true });
    last = match.index + match[0].length;
  }
  if (last < subject.length) parts.push({ text: subject.slice(last), variable: false });
  return parts;
}

// The API paths whose queries a template change makes stale, for `invalidatePath`.

/** The scorecard templates list. */
export const scorecardTemplatesPath = '/v1/scorecard-templates';
/** Where every template is used. */
export const templateUsagePath = '/v1/template-usage';
