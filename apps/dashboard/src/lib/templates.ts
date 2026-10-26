import type { TemplateUse, UsageItem } from '@screenhire/api-client';

// The Templates page (design system section 21): one tab per kind of template, each listing its templates with where
// they are used, filtered by All, In use, Not used in 6 months and Archived.

export type TemplateKind = 'hiring' | 'scorecards' | 'emails';

export const templateKinds: readonly TemplateKind[] = ['hiring', 'scorecards', 'emails'];

/** The tab's label, what its templates are called, and the one sentence that says what they are for. */
export const templateKindViews: Record<TemplateKind, { label: string; noun: string; sentence: string }> = {
  hiring: { label: 'Hiring', noun: 'Hiring templates', sentence: 'Where a job starts: its stages, questions, scorecards and automations.' },
  scorecards: { label: 'Scorecards', noun: 'Scorecards', sentence: "What reviewers fill in at a stage, and the questions behind the candidate's rating." },
  emails: { label: 'Emails', noun: 'Email templates', sentence: 'What candidates receive, by hand or from an automation.' },
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

export type TemplateFilter = 'all' | 'in-use' | 'not-used' | 'archived';

export const templateFilters: readonly { value: TemplateFilter; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'in-use', label: 'In use' },
  { value: 'not-used', label: 'Not used in 6 months' },
  { value: 'archived', label: 'Archived' },
];

/** What a filter needs to know of a template. */
export interface FilterableTemplate {
  archived: boolean;
  usage: TemplateUse | undefined;
}

/**
 * Whether a template shows under a filter. All, In use and Not used in 6 months leave archived templates out; Archived
 * shows only them. In use: something uses it now. Not used in 6 months: nothing used it in 180 days (the API decides,
 * and a template younger than that never counts), so the two can overlap.
 */
export function matchesFilter(template: FilterableTemplate, filter: TemplateFilter): boolean {
  if (filter === 'archived') return template.archived;
  if (template.archived) return false;
  if (filter === 'in-use') return (template.usage?.usedBy.length ?? 0) > 0;
  if (filter === 'not-used') return template.usage?.notUsedRecently ?? false;
  return true;
}

/** How many templates each filter shows, for its count badge. */
export function filterCounts(templates: readonly FilterableTemplate[]): Record<TemplateFilter, number> {
  return Object.fromEntries(templateFilters.map(({ value }) => [value, templates.filter((template) => matchesFilter(template, value)).length])) as Record<
    TemplateFilter,
    number
  >;
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
