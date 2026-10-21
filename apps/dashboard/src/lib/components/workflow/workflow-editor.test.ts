// The Validation region tells assistive technology, and the end-to-end tests, whether a check of the latest change is
// still on its way, so neither reads the previous change's result as the answer.
import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import WorkflowEditor from './workflow-editor.svelte';

const workflow: HiringWorkflow = {
  stages: [{ id: 'applied', name: 'Applied', kind: 'Applied' }],
  questionnaire: { questions: [] },
  integrityPolicy: { aiUse: 'Allowed' },
  automations: [],
};

const renderEditor = (checking: boolean, errors: DefinitionError[] = []) =>
  render(WithQueryClient, { props: { component: WorkflowEditor, props: { workflow, errors, checking } } });

describe('WorkflowEditor validation region', () => {
  it('is busy while a check is on its way', () => {
    renderEditor(true);

    const region = screen.getByRole('region', { name: 'Validation' });
    expect(region.getAttribute('aria-busy')).toBe('true');
    expect(region.textContent).toContain('Checking…');
  });

  it('is settled once the latest change is checked', () => {
    renderEditor(false, [{ path: 'stages', message: 'A workflow needs a hired stage.' }]);

    const region = screen.getByRole('region', { name: 'Validation' });
    expect(region.getAttribute('aria-busy')).toBe('false');
    const text = region.textContent?.replace(/\s+/g, ' ');
    expect(text).toContain('1 problem to fix before this can be saved');
    expect(text).not.toContain('checking your latest change');
  });
});
