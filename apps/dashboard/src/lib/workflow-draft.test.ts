import type { HiringWorkflow } from '@screenhire/api-client';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/svelte';
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

// Test and Publish wait for the workflow to be valid; Save never does (a draft saves with problems).
describe('ready', () => {
  const renderWith = (validate: () => Response) => {
    cleanup();
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'POST /v1/workflows/validate': validate })));
    render(WithQueryClient, { props: { component: WorkflowDraftHarness, props: { first: workflow('Screening'), theirs: workflow('Recruiter call') } } });
  };

  it('is not ready until the validator has checked what is on screen, then is', async () => {
    renderWith(() => Response.json({ valid: true, errors: [] }));

    expect(text('ready')).toBe('not ready');
    await waitFor(() => expect(text('ready')).toBe('ready'));
  });

  it('is not ready again the moment the workflow changes, until that change is checked', async () => {
    renderWith(() => Response.json({ valid: true, errors: [] }));
    await waitFor(() => expect(text('ready')).toBe('ready'));

    await click('Rename');

    expect(text('ready')).toBe('not ready');
    await waitFor(() => expect(text('ready')).toBe('ready'));
  });

  it('is not ready while the validator reports a problem, which is shown', async () => {
    renderWith(() => Response.json({ valid: false, errors: [{ path: 'stages[1].name', message: 'Give the stage a name.' }] }));

    await waitFor(() => expect(text('errors')).toBe('Give the stage a name.'));
    expect(text('ready')).toBe('not ready');
  });

  it('is not ready after the API refused a save with a problem, until the workflow changes and is checked again', async () => {
    renderWith(() => Response.json({ valid: true, errors: [] }));
    await waitFor(() => expect(text('ready')).toBe('ready'));

    await click('Fail the save');

    expect(text('errors')).toBe('Screening still has 1 candidate.');
    expect(text('ready')).toBe('not ready');

    await click('Rename');
    await waitFor(() => expect(text('ready')).toBe('ready'));
    expect(text('errors')).toBe('');
  });
});
