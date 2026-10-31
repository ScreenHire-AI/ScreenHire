import type { HiringWorkflow } from '@screenhire/api-client';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiStub } from '../test-support/fixtures.js';
import WorkflowDraftHarness from '../test-support/workflow-draft-harness.svelte';
import WithQueryClient from '../test-support/with-query-client.svelte';

const workflow = (screening: string): HiringWorkflow => ({
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'screening', name: screening, kind: 'Screening' },
  ],
  questionnaire: { questions: [] },
  verificationPolicy: { aiUse: 'Allowed' },
  automations: [],
});

const text = (id: string) => screen.getByTestId(id).textContent;
const click = (name: string) => fireEvent.click(screen.getByRole('button', { name }));

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn(apiStub({ 'POST /v1/workflows/validate': () => Response.json({ valid: true, errors: [] }) })));
  render(WithQueryClient, { props: { component: WorkflowDraftHarness, props: { first: workflow('Screening'), theirs: workflow('Recruiter call') } } });
});

describe('createWorkflowDraft', () => {
  it('takes a newer stored version while nothing is unsaved', async () => {
    await click('Someone else saves');

    expect(text('second-stage')).toBe('Recruiter call');
    expect(text('outdated')).toBe('current');
  });

  it('never loads a newer version over unsaved changes, and loads it when asked', async () => {
    await click('Rename');
    expect(text('dirty')).toBe('unsaved');

    await click('Someone else saves');

    expect(text('second-stage')).toBe('Phone screen');
    expect(text('outdated')).toBe('outdated');
    // A save names the version the edit started from, so the API refuses it instead of overwriting theirs.
    expect(text('source')).toBe('job:1');

    await click('Load theirs');

    expect(text('second-stage')).toBe('Recruiter call');
    expect(text('source')).toBe('job:2');
    expect(text('dirty')).toBe('saved');
    expect(text('outdated')).toBe('current');
  });
});
