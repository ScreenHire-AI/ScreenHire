import type { TemplateUse } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { copyName, filterCounts, matchesFilter, subjectParts, templateHref, templatesHref, usageHref, usageLabel, usedByLabel } from './templates.js';

const use = (count: number, notUsedRecently = false): TemplateUse => ({
  templateId: 't',
  usedBy: Array.from({ length: count }, (_, index) => ({ kind: 'Job' as const, id: `j-${index}`, name: `Job ${index}` })),
  lastUsedAt: null,
  notUsedRecently,
});

describe('templates', () => {
  it('names a copy "(copy)" within the kind\'s longest name', () => {
    expect(copyName('Technical interview', 80)).toBe('Technical interview (copy)');
    const long = 'x'.repeat(80);
    expect(copyName(long, 80)).toHaveLength(80);
    expect(copyName(long, 80).endsWith(' (copy)')).toBe(true);
  });

  it('leaves archived templates out of All, In use and Not used, and shows only them under Archived', () => {
    const templates = [
      { archived: false, usage: use(2) },
      { archived: false, usage: use(0, true) },
      { archived: false, usage: use(1, true) },
      { archived: true, usage: use(0) },
      { archived: false, usage: undefined },
    ];

    expect(filterCounts(templates)).toEqual({ all: 4, 'in-use': 2, 'not-used': 2, archived: 1 });
    expect(matchesFilter(templates[3]!, 'all')).toBe(false);
    expect(matchesFilter(templates[4]!, 'in-use')).toBe(false);
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
