// The workflow page's tabs (workflow-tabs.ts): named once, kept in the address as ?tab=, with the old names still opening
// the right tab for an address saved before the rename.
import { describe, expect, it } from 'vitest';
import { defaultWorkflowTab, tabFrom, tabHref, workflowTabs } from './workflow-tabs.js';

describe('workflowTabs', () => {
  it('are Pipeline, Forms, Automations and Test, in that order', () => {
    expect(workflowTabs.map((tab) => [tab.value, tab.label])).toEqual([
      ['pipeline', 'Pipeline'],
      ['forms', 'Forms'],
      ['automations', 'Automations'],
      ['test', 'Test'],
    ]);
    expect(defaultWorkflowTab).toBe('pipeline');
  });
});

describe('tabFrom', () => {
  it.each(['pipeline', 'forms', 'automations', 'test'] as const)('reads %s as itself', (value) => {
    expect(tabFrom(value)).toBe(value);
  });

  it('opens the new tab for an old name', () => {
    expect(tabFrom('overview')).toBe('pipeline');
    expect(tabFrom('graph')).toBe('automations');
    expect(tabFrom('questionnaire')).toBe('forms');
    expect(tabFrom('questions'), 'Questions became Forms').toBe('forms');
    expect(tabFrom('simulate')).toBe('test');
  });

  it('opens Pipeline for nothing or for a name that is not a tab', () => {
    expect(tabFrom(null)).toBe('pipeline');
    expect(tabFrom(undefined)).toBe('pipeline');
    expect(tabFrom('')).toBe('pipeline');
    expect(tabFrom('settings')).toBe('pipeline');
  });
});

describe('tabHref', () => {
  const url = new URL('http://dashboard.test/templates/t-1?version=2&tab=questions');

  it('sets ?tab= and keeps the other parameters', () => {
    expect(tabHref(url, 'automations')).toBe('/templates/t-1?version=2&tab=automations');
  });

  it('makes the first tab the bare address, still keeping the other parameters', () => {
    expect(tabHref(url, 'pipeline')).toBe('/templates/t-1?version=2');
    expect(tabHref(new URL('http://dashboard.test/jobs/j-1/workflow?tab=test'), 'pipeline')).toBe('/jobs/j-1/workflow');
  });

  it('drops ?show=, which belongs to the tab it lit a path on', () => {
    expect(tabHref(new URL('http://dashboard.test/x?tab=automations&show=a'), 'test')).toBe('/x?tab=test');
  });

  it('adds the extra parameters it is given', () => {
    expect(tabHref(new URL('http://dashboard.test/x'), 'automations', { show: 'tag-screen' })).toBe('/x?tab=automations&show=tag-screen');
  });
});
