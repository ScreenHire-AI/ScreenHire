import type { TemplateUse } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import type { UsageItem } from '@screenhire/api-client';
import {
  copyName,
  filterCounts,
  filtersFor,
  matchesFilter,
  olderJobs,
  olderJobsWords,
  publishWords,
  subjectParts,
  templateHref,
  templatesHref,
  templateStatus,
  usageHref,
  usageLabel,
  usedByLabel,
  usedByWords,
} from './templates.js';

const use = (count: number, notUsedRecently = false): TemplateUse => ({
  templateId: 't',
  usedBy: Array.from({ length: count }, (_, index) => ({ kind: 'Job' as const, id: `j-${index}`, name: `Job ${index}` })),
  lastUsedAt: null,
  notUsedRecently,
});

const job = (id: string, templateVersion: number | null): UsageItem => ({ kind: 'Job', id, name: `Job ${id}`, templateVersion, workflowVersion: 1 });

describe('templates', () => {
  it('names a copy "(copy)" within the kind\'s longest name', () => {
    expect(copyName('Technical interview', 80)).toBe('Technical interview (copy)');
    const long = 'x'.repeat(80);
    expect(copyName(long, 80)).toHaveLength(80);
    expect(copyName(long, 80).endsWith(' (copy)')).toBe(true);
  });

  it('leaves archived templates out of All, In use and Drafts, and shows only them under Archived', () => {
    const templates = [
      { archived: false, usage: use(2) },
      { archived: false, usage: use(0, true), draft: 2 },
      { archived: false, usage: use(1, true) },
      { archived: true, usage: use(0), draft: 1 },
      { archived: false, usage: undefined },
    ];

    expect(filterCounts(templates)).toEqual({ all: 4, 'in-use': 2, drafts: 1, archived: 1 });
    expect(matchesFilter(templates[3]!, 'all')).toBe(false);
    expect(matchesFilter(templates[4]!, 'in-use')).toBe(false);
  });

  it('offers Drafts only on the hiring tab, the one kind with versions', () => {
    expect(filtersFor('hiring').map((filter) => filter.label)).toEqual(['All', 'In use', 'Drafts', 'Archived']);
    expect(filtersFor('emails').map((filter) => filter.label)).toEqual(['All', 'In use', 'Archived']);
  });

  it("says a hiring template's status in one word or two, never a version number", () => {
    expect(templateStatus({ archived: false, published: 2, draft: null })).toEqual({ status: 'published', label: 'Published' });
    expect(templateStatus({ archived: false, published: 2, draft: 3 })).toEqual({ status: 'published', label: 'Published, draft open' });
    expect(templateStatus({ archived: false, published: null, draft: 1 })).toEqual({ status: 'draft', label: 'Draft' });
    expect(templateStatus({ archived: true, published: 2, draft: 3 })).toEqual({ status: 'archived', label: 'Archived' });
  });

  it('counts the jobs left on an older version in Used by', () => {
    const jobs = [job('a', 2), job('b', 1), job('c', 2)];

    expect(usedByWords('hiring', jobs, 2)).toBe('3 jobs, 1 on an older version');
    expect(usedByWords('hiring', [job('a', 1), job('b', 1)], 3)).toBe('2 jobs, 2 on older versions');
    expect(usedByWords('hiring', jobs, null)).toBe('3 jobs');
    expect(usedByWords('hiring', [], 2)).toBe('None');
    expect(olderJobs(jobs, 2).map((item) => item.id)).toEqual(['b']);
    expect(olderJobsWords(1)).toBe('1 job uses an older version.');
    expect(olderJobsWords(3)).toBe('3 jobs use an older version.');
  });

  it('says what publishing does to new jobs and to the jobs the template already has', () => {
    expect(publishWords(1, [])).toBe('Publishing makes version 1 the version new jobs get.');
    expect(publishWords(2, [job('a', 1)])).toBe('Publishing makes version 2 the version new jobs get. Your 1 job stays on version 1 until you update it.');
    expect(publishWords(3, [job('a', 2), job('b', 2), job('c', 2)])).toBe(
      'Publishing makes version 3 the version new jobs get. Your 3 jobs stay on version 2 until you update them.',
    );
    // A job with no recorded version is not given one.
    expect(publishWords(2, [job('a', null)])).toBe('Publishing makes version 2 the version new jobs get. Your 1 job stays on the version it has until you update it.');
    expect(publishWords(3, [job('a', 1), job('b', 2)])).toBe(
      'Publishing makes version 3 the version new jobs get. Your 2 jobs stay on the versions they have until you update them.',
    );
  });

  it('says Used by as a count of what each kind counts', () => {
    expect(usedByLabel('hiring', 0)).toBe('None');
    expect(usedByLabel('hiring', 1)).toBe('1 job');
    expect(usedByLabel('scorecards', 2)).toBe('2 hiring templates');
    expect(usedByLabel('emails', 3)).toBe('3 automations');
  });

  it('links each user of a template to where it lives', () => {
    expect(usageHref({ kind: 'Job', id: 'j', name: 'Barista' })).toBe('/jobs/j');
    expect(usageHref({ kind: 'JobAutomation', id: 'j', name: 'Barista', automationName: 'Invite' })).toBe('/jobs/j/workflow');
    expect(usageHref({ kind: 'TemplateAutomation', id: 't', name: 'Hourly', automationName: 'Invite' })).toBe('/templates/t');
    expect(usageLabel({ kind: 'JobAutomation', id: 'j', name: 'Barista', automationName: 'Invite' })).toBe('Barista: Invite');
    expect(templateHref('emails', 'stage-change')).toBe('/templates/emails/stage-change');
    expect(templateHref('hiring', 't')).toBe('/templates/t');
    expect(templatesHref('hiring')).toBe('/templates');
    expect(templatesHref('scorecards')).toBe('/templates?tab=scorecards');
  });

  it('reads each subject variable as its label in lower case', () => {
    expect(subjectParts('Interview for {{ jobTitle }} at {{organizationName}}', new Map([['jobTitle', 'Job title']]))).toEqual([
      { text: 'Interview for ', variable: false },
      { text: 'job title', variable: true },
      { text: ' at ', variable: false },
      { text: 'organizationname', variable: true },
    ]);
  });
});
