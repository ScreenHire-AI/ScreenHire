// A step that asks for text and is left empty is dropped, with Undo, so a blank step is never a problem to fix. A new Add a
// tag step starts with its tag field in focus.
import type { Automation, HiringWorkflow } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiStub } from '../../../test-support/fixtures.js';
import AutomationFormHarness from '../../../test-support/automation-form-harness.svelte';
import WithQueryClient from '../../../test-support/with-query-client.svelte';

const workflowWith = (actions: Automation['actions']): HiringWorkflow => ({
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone-screen', name: 'Phone screen', kind: 'Screening' },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: { questions: [] },
  verificationPolicy: { aiUse: 'AllowedWithDisclosure' },
  automations: [{ id: 'tag-screen', name: 'Tag screened', trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions }],
});

const renderForm = (actions: Automation['actions'], undoable = true) =>
  render(WithQueryClient, { props: { component: AutomationFormHarness, props: { workflow: workflowWith(actions), undoable } } });

const actions = () => JSON.parse(screen.getByTestId('actions').textContent!) as Automation['actions'];
const types = () => actions().map((action) => action.type);
const leave = () => screen.getByRole('button', { name: 'Somewhere else' }).focus();

beforeEach(() => {
  // jsdom has no Web Animations, and a step leaving the list's `animate:flip` asks for its running ones.
  Element.prototype.getAnimations = () => [];
  vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/email-templates': () => Response.json({ templates: [], sendingEnabled: false }) })));
});

afterEach(() => {
  delete (Element.prototype as Partial<Element>).getAnimations;
  vi.unstubAllGlobals();
});

describe('a new step', () => {
  it('is an Add a tag step with its Tag name field in focus', async () => {
    renderForm([{ type: 'NotifyTeam' }]);

    await fireEvent.click(screen.getByRole('button', { name: 'Add action' }));

    const tag = await screen.findByRole('textbox', { name: 'Tag name' });
    await waitFor(() => expect(tag).toHaveFocus());
    expect(actions()).toEqual([{ type: 'NotifyTeam' }, { type: 'AddTag', tag: '' }]);
  });
});

describe('an empty step', () => {
  it('is dropped when focus leaves it, and offers to put it back', async () => {
    renderForm([{ type: 'NotifyTeam' }, { type: 'AddTag', tag: '' }]);
    screen.getByRole('textbox', { name: 'Tag name' }).focus();

    leave();

    await waitFor(() => expect(actions()).toEqual([{ type: 'NotifyTeam' }]));
    expect(screen.getByTestId('offer')).toHaveTextContent('Removed the empty step.');
    expect(screen.queryByRole('textbox', { name: 'Tag name' })).toBeNull();
  });

  it('comes back where it was with Undo', async () => {
    renderForm([{ type: 'AddTag', tag: '' }, { type: 'NotifyTeam' }]);
    screen.getByRole('textbox', { name: 'Tag name' }).focus();
    leave();
    await waitFor(() => expect(actions()).toEqual([{ type: 'NotifyTeam' }]));

    await fireEvent.click(screen.getByRole('button', { name: 'Undo' }));

    await waitFor(() => expect(actions()).toEqual([{ type: 'AddTag', tag: '' }, { type: 'NotifyTeam' }]));
    expect(await screen.findByRole('textbox', { name: 'Tag name' })).toBeInTheDocument();
  });

  it('is dropped without an offer outside the app shell, which has none to give', async () => {
    renderForm([{ type: 'NotifyTeam' }, { type: 'AddTag', tag: '' }], false);
    screen.getByRole('textbox', { name: 'Tag name' }).focus();

    leave();

    await waitFor(() => expect(actions()).toEqual([{ type: 'NotifyTeam' }]));
    expect(screen.getByTestId('offer')).toHaveTextContent(/^$/);
  });

  it('counts a tag of spaces as empty', async () => {
    renderForm([{ type: 'AddTag', tag: '   ' }, { type: 'NotifyTeam' }]);
    screen.getByRole('textbox', { name: 'Tag name' }).focus();

    leave();

    await waitFor(() => expect(actions()).toEqual([{ type: 'NotifyTeam' }]));
  });

  it('stays when it has text, and when focus only moves inside the same step', async () => {
    renderForm([{ type: 'AddTag', tag: 'screened' }, { type: 'AddTag', tag: '' }]);
    const tags = screen.getAllByRole('textbox', { name: 'Tag name' });
    tags[0]!.focus();
    leave();
    expect(actions()).toHaveLength(2);

    // Moving from the empty tag to its own step's action picker keeps the step: the person is still on it.
    tags[1]!.focus();
    screen.getAllByRole('combobox', { name: /^Action / })[1]!.focus();
    expect(actions()).toEqual([{ type: 'AddTag', tag: 'screened' }, { type: 'AddTag', tag: '' }]);
    expect(screen.getByTestId('offer')).toHaveTextContent(/^$/);
  });

  it('is dropped for a note or a web address too, but never a step that asks for no text', async () => {
    renderForm([{ type: 'NotifyTeam', note: '' }, { type: 'CallWebhook', url: '' }, { type: 'AssignReviewer' }]);
    screen.getByRole('textbox', { name: 'Note' }).focus();
    leave();
    await waitFor(() => expect(types()).toEqual(['CallWebhook', 'AssignReviewer']));

    screen.getByRole('textbox', { name: /^URL/ }).focus();
    leave();
    await waitFor(() => expect(types()).toEqual(['AssignReviewer']));

    // A step with nothing to type stays, whatever the focus does.
    screen.getByRole('combobox', { name: 'Action 1' }).focus();
    leave();
    expect(types()).toEqual(['AssignReviewer']);
  });
});
