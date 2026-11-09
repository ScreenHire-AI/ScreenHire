// The workflow page's tabs (workflow-tabs.ts): named once, kept in the address as ?tab=, with the old names still opening
// the right tab for an address saved before the rename.
import { describe, expect, it } from 'vitest';
import { defaultWorkflowTab, tabFrom, tabHref, workflowTabs } from './workflow-tabs.js';

describe('workflowTabs', () => {
  it('are Overview, Pipeline, Questions, Automations and Test, in that order', () => {
    expect(workflowTabs.map((tab) => [tab.value, tab.label])).toEqual([
      ['overview', 'Overview'],
      ['pipeline', 'Pipeline'],
      ['questions', 'Questions'],
      ['automations', 'Automations'],
      ['test', 'Test'],
    ]);
    expect(defaultWorkflowTab).toBe('overview');
  });
});

describe('tabFrom', () => {
  it.each(['overview', 'pipeline', 'questions', 'automations', 'test'] as const)('reads %s as itself', (value) => {
    expect(tabFrom(value)).toBe(value);
  });

  it('opens the new tab for an old name', () => {
    expect(tabFrom('graph')).toBe('automations');
    expect(tabFrom('questionnaire')).toBe('questions');
    expect(tabFrom('simulate')).toBe('test');
  });

  it('opens Overview for nothing or for a name that is not a tab', () => {
    expect(tabFrom(null)).toBe('overview');
    expect(tabFrom(undefined)).toBe('overview');
    expect(tabFrom('')).toBe('overview');
    expect(tabFrom('settings')).toBe('overview');
  });
});

describe('tabHref', () => {
  const url = new URL('http://dashboard.test/templates/t-1?version=2&tab=pipeline');

  it('sets ?tab= and keeps the other parameters', () => {
    expect(tabHref(url, 'automations')).toBe('/templates/t-1?version=2&tab=automations');
  });

  it('makes the first tab the bare address, still keeping the other parameters', () => {
    expect(tabHref(url, 'overview')).toBe('/templates/t-1?version=2');
    expect(tabHref(new URL('http://dashboard.test/jobs/j-1/workflow?tab=test'), 'overview')).toBe('/jobs/j-1/workflow');
  });

  it('drops ?show=, which belongs to the tab it lit a path on', () => {
    expect(tabHref(new URL('http://dashboard.test/x?tab=automations&show=a'), 'test')).toBe('/x?tab=test');
  });

  it('adds the extra parameters it is given', () => {
    expect(tabHref(new URL('http://dashboard.test/x'), 'automations', { show: 'tag-screen' })).toBe('/x?tab=automations&show=tag-screen');
  });
});
