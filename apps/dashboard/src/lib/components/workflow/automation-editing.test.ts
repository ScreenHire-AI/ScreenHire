// Editing an automation as a sentence, adding one from the recipes, and the list the Automations tab is on a phone. The model
// behind the blanks is tested in #lib/automation-sentence.test.ts; this is the screen: the sheet, the blanks and their lists,
// Done, and Edit as rules.
import type { Automation, HiringWorkflow } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub, roleCatalogue } from '../../../test-support/fixtures.js';
import { stubViewport } from '../../../test-support/viewport.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import WorkflowHarness from '../../../test-support/workflow-harness.svelte';

const base: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone', name: 'Phone call', kind: 'Screening' },
    { id: 'trial', name: 'Trial shift', kind: 'Assessment' },
    { id: 'offer', name: 'Offer', kind: 'Offer' },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
  ],
  questionnaire: {
    questions: [{ id: 'right_to_work', type: 'YesNo', label: 'Right to work', required: true, knockout: { disqualifyingAnswer: false, reason: null } }],
  },
  verificationPolicy: { aiUse: 'AllowedWithDisclosure' },
  automations: [],
};

const tagOnApply: Automation = { id: 'tag', name: 'Tag new people', trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'AddTag', tag: 'new' }] };

const member = { id: 'm1', userId: 'u-dana', email: 'dana@example.com', displayName: 'Dana Whitfield', role: 'Recruiter', customRoleId: null, roleName: 'Recruiter', joinedAt: '2026-10-01T00:00:00Z', jobAccess: 'All', seesAllJobs: true, jobCount: 0 };

let workflow = structuredClone(base);
const draft = () => JSON.parse(screen.getByTestId('draft').textContent!) as HiringWorkflow;

beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/email-templates': () => Response.json({ templates: [], sendingEnabled: false }),
        'GET /v1/organization/members': () => Response.json([member]),
        'GET /v1/organization/roles': () => Response.json(roleCatalogue),
      }),
    ),
  );
  // bits-ui scrolls the highlighted option into view, which jsdom does not implement.
  Element.prototype.scrollIntoView ??= () => {};
  workflow = structuredClone(base);
});

afterEach(() => vi.unstubAllGlobals());

function renderEditor(automations: Automation[] = [], errors: { path: string; message: string }[] = []) {
  workflow = { ...structuredClone(base), automations: structuredClone(automations) };
  return render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow, errors, mode: 'editor', tab: 'automations' } } });
}

const panel = () => screen.getByRole('tabpanel', { name: 'Automations' });
/** The sentence's words as they read: the spaces a button's markup holds before a comma or a full stop do not count. */
const sentenceText = (scope: HTMLElement) => {
  const sentence = scope.querySelector('[data-sentence]')!.cloneNode(true) as HTMLElement;
  sentence.querySelectorAll('[data-adds]').forEach((add) => add.remove());
  return sentence.textContent!.replace(/\s+/g, ' ').replace(/ ([,.])/g, '$1').trim();
};
const sheet = (name: string) => screen.findByRole('dialog', { name });

/** Opens a blank's list and presses one of its choices. */
async function choose(scope: HTMLElement, blank: string | RegExp, choice: string) {
  await fireEvent.click(within(scope).getByRole('button', { name: blank }));
  await fireEvent.click(await screen.findByRole('option', { name: choice }));
}

describe('Add automation', () => {
  it('opens ready-made recipes for this template, grouped, one line each', async () => {
    renderEditor();
    await fireEvent.click(within(panel()).getByRole('button', { name: 'Add an automation' }));

    const recipes = await sheet('Add an automation');
    expect(within(recipes).getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual([
      'Keep candidates in the loop',
      'Check the must-haves',
      'Look closer when the checks suggest it',
    ]);
    expect(recipes).toHaveTextContent('Thank people when they apply');
    expect(recipes).toHaveTextContent('If "Right to work" is No, the team reviews before anyone is turned down.');
    expect(recipes).not.toHaveTextContent('turns anyone down on a check');
    expect(within(recipes).getByRole('button', { name: 'Write your own' })).toBeInTheDocument();
    await expectNoAxeViolations(recipes);
  });

  it('is the panel\'s button once there is an automation, and the map\'s own button while there is none', async () => {
    const empty = renderEditor();
    expect(within(panel()).getAllByRole('button', { name: /^Add (an )?automation$/ })).toHaveLength(1);
    empty.unmount();

    renderEditor([tagOnApply]);
    expect(within(panel()).getByRole('button', { name: 'Add automation' })).toBeInTheDocument();
  });

  it('adds a recipe, hides it, and says so with a way to open it', async () => {
    renderEditor();
    await fireEvent.click(within(panel()).getByRole('button', { name: 'Add an automation' }));
    const recipes = await sheet('Add an automation');

    await fireEvent.click(within(recipes).getByRole('button', { name: 'Add Thank people when they apply' }));

    expect(within(recipes).queryByRole('button', { name: 'Add Thank people when they apply' })).toBeNull();
    expect(within(recipes).getByRole('status')).toHaveTextContent('Added "Thank people when they apply".');
    expect(draft().automations).toEqual([
      {
        id: 'recipe-thanks',
        name: 'Thank people when they apply',
        trigger: { type: 'ApplicationSubmitted' },
        conditions: null,
        actions: [{ type: 'SendEmail', emailTemplate: 'application-received' }],
        enabled: true,
        allowAutoReject: false,
      },
    ]);

    await fireEvent.click(within(recipes).getByRole('button', { name: 'Edit Thank people when they apply' }));
    const edit = await sheet('Thank people when they apply');
    expect(sentenceText(edit)).toContain('When someone applies, send the email Application received.');
  });

  it('shows only the recipes of a stage when opened from the stage, and Write your own adds there', async () => {
    renderEditor([tagOnApply]);

    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('Add an automation to Trial shift') }));
    const recipes = await sheet('Add an automation');
    expect(within(recipes).getAllByRole('listitem').map((row) => row.textContent)).toEqual([expect.stringContaining('Tell people when they reach Trial shift')]);

    await fireEvent.click(within(recipes).getByRole('button', { name: 'Write your own' }));
    const edit = await sheet('New automation');
    expect(draft().automations.at(-1)!.trigger).toEqual({ type: 'StageEntered', stageId: 'trial' });
    expect(edit).toHaveTextContent('When someone moves into Trial shift');
  });

  it('says so when every recipe is added', async () => {
    renderEditor();
    await fireEvent.click(within(panel()).getByRole('button', { name: 'Add an automation' }));
    const recipes = await sheet('Add an automation');
    for (let guard = 0; guard < 20; guard++) {
      const add = within(recipes).queryAllByRole('button', { name: /^Add / })[0];
      if (!add) break;
      await fireEvent.click(add);
    }

    expect(recipes).toHaveTextContent('Every ready-made automation for this template is added.');
  });
});

describe('the sentence', () => {
  it('opens an automation as one sentence with Name, On and Done, and no subtitle', async () => {
    renderEditor([tagOnApply]);
    await fireEvent.click(within(panel()).getByRole('button', { name: 'List' }));
    await fireEvent.click(within(panel()).getByRole('button', { name: /Tag new people/ }));

    const edit = await sheet('Tag new people');
    expect(within(edit).getByRole('textbox', { name: 'Name' })).toHaveValue('Tag new people');
    expect(within(edit).getByRole('switch')).toBeChecked();
    expect(within(edit).getByText('On')).toBeInTheDocument();
    expect(within(edit).getByRole('button', { name: 'Done' })).toBeInTheDocument();
    expect(edit.querySelector('[data-slot="sheet-description"]')).toBeNull();
    expect(sentenceText(edit)).toBe('When someone applies, add the tag new.');
    await expectNoAxeViolations(edit);
  });

  it('renames, switches off, and closes with Done', async () => {
    renderEditor([tagOnApply]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    await fireEvent.input(within(edit).getByRole('textbox', { name: 'Name' }), { target: { value: 'Tag everyone' } });
    await fireEvent.click(within(edit).getByRole('switch'));
    await fireEvent.click(within(edit).getByRole('button', { name: 'Done' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(draft().automations[0]).toMatchObject({ name: 'Tag everyone', enabled: false });
  });

  it('changes a blank from its list of plain choices', async () => {
    renderEditor([tagOnApply]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    await fireEvent.click(within(edit).getByRole('button', { name: 'Trigger: someone applies' }));
    const options = (await screen.findAllByRole('option')).map((option) => option.textContent?.trim());
    expect(options).toEqual([
      'Someone applies',
      'Someone moves into',
      'Someone leaves',
      'Someone has waited',
      'Someone answers',
      'The checks finish',
      'A reviewer decides on',
      'The time limit passes in',
      'An interview is booked',
      'Someone runs it by hand',
    ]);
    await fireEvent.click(screen.getByRole('option', { name: 'Someone moves into' }));

    expect(draft().automations[0]!.trigger).toEqual({ type: 'StageEntered', stageId: 'applied' });
    await choose(edit, 'Stage: Applied', 'Trial shift');
    expect(draft().automations[0]!.trigger).toEqual({ type: 'StageEntered', stageId: 'trial' });
    expect(within(edit).getByRole('button', { name: 'Trigger: someone moves into' })).toBeInTheDocument();
  });

  it('adds a condition with "and if", grouped as Their answers, The application and ScreenHire\'s checks', async () => {
    renderEditor([tagOnApply]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    await fireEvent.click(within(edit).getByRole('button', { name: 'Add a condition: and if' }));
    const list = await screen.findByRole('listbox');
    expect([...list.querySelectorAll('[data-command-group-heading]')].map((heading) => heading.textContent?.trim())).toEqual(['Their answers', 'The application', "ScreenHire's checks"]);
    await fireEvent.click(within(list).getByRole('option', { name: 'Right to work' }));

    expect(draft().automations[0]!.conditions).toEqual({ match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'yes' }] });
    await choose(edit, /^Condition 1: value/, 'No');
    expect(draft().automations[0]!.conditions!.rules[0]).toEqual({ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' });
    expect(edit.querySelector('[data-sentence]')).toHaveTextContent('and their answer to Right to work is No');
  });

  it('adds a step with "then also", opens the new tag for typing, and drops it when it is left empty', async () => {
    renderEditor([{ ...tagOnApply, actions: [{ type: 'NotifyTeam', note: 'Hello' }] }]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    await choose(edit, 'Add another step: then also', 'Add a tag');
    const tag = await screen.findByRole('textbox', { name: 'Action 2: tag name' });
    await waitFor(() => expect(tag).toHaveFocus());
    expect(draft().automations[0]!.actions).toEqual([{ type: 'NotifyTeam', note: 'Hello' }, { type: 'AddTag', tag: '' }]);

    await fireEvent.keyDown(tag, { key: 'Escape' });
    await waitFor(() => expect(draft().automations[0]!.actions).toEqual([{ type: 'NotifyTeam', note: 'Hello' }]));
  });

  it('types a tag into its blank as it is typed, and keeps it', async () => {
    renderEditor([{ ...tagOnApply, actions: [{ type: 'AddTag', tag: '' }] }]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    await fireEvent.click(within(edit).getByRole('button', { name: /^Action 1: tag name/ }));
    const tag = await screen.findByRole('textbox', { name: 'Action 1: tag name' });
    await fireEvent.input(tag, { target: { value: 'Referral' } });
    await fireEvent.keyDown(tag, { key: 'Enter' });

    await waitFor(() => expect(within(edit).getByRole('button', { name: 'Action 1: tag name: Referral' })).toBeInTheDocument());
    expect(draft().automations[0]!.actions).toEqual([{ type: 'AddTag', tag: 'Referral' }]);
  });

  it('asks the team or a named teammate to review', async () => {
    renderEditor([{ ...tagOnApply, actions: [{ type: 'AssignReviewer' }] }]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    await fireEvent.click(within(edit).getByRole('button', { name: 'Action 1: who reviews: the team' }));
    expect((await screen.findAllByRole('option')).map((option) => option.textContent?.trim())).toEqual(['The team', 'Dana Whitfield']);
    await fireEvent.click(screen.getByRole('option', { name: 'Dana Whitfield' }));

    expect(draft().automations[0]!.actions).toEqual([{ type: 'AssignReviewer', assigneeUserId: 'u-dana' }]);
  });

  it('removes a condition or a step from the list of its blank', async () => {
    renderEditor([{ ...tagOnApply, conditions: { match: 'All', rules: [{ fact: 'application.tags', operator: 'Exists' }] }, actions: [{ type: 'AddTag', tag: 'a' }, { type: 'AddTag', tag: 'b' }] }]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    await fireEvent.click(within(edit).getByRole('button', { name: /^Condition 1: what to check/ }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Remove this condition' }));
    expect(draft().automations[0]!.conditions).toBeNull();

    await fireEvent.click(within(edit).getByRole('button', { name: 'Action 1: add the tag' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Remove this step' }));
    expect(draft().automations[0]!.actions).toEqual([{ type: 'AddTag', tag: 'b' }]);
  });

  it('shows the validator\'s message under the sentence and marks the blank it is about', async () => {
    renderEditor([tagOnApply], [{ path: 'automations[0].actions[0]', message: 'The tag in "Tag new people" has no name. Type one, or remove that step.' }]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    expect(edit).toHaveTextContent('The tag in "Tag new people" has no name.');
    expect(within(edit).getByRole('button', { name: 'Action 1: add the tag' })).toHaveAttribute('data-invalid', 'true');
    expect(within(edit).getByRole('button', { name: 'Trigger: someone applies' })).not.toHaveAttribute('data-invalid');
  });
});

describe('after a check', () => {
  const checking: Automation = {
    id: 'check',
    name: 'Look closer',
    trigger: { type: 'VerificationAssessed' },
    conditions: { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'In', values: ['ReviewSuggested', 'VerificationRecommended'] }] },
    actions: [{ type: 'AssignReviewer' }],
  };

  it('reads the check as Verification is Review or Verify, never the retired wording', async () => {
    renderEditor([checking]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Look closer') }));
    const edit = await sheet('Look closer');

    expect(sentenceText(edit)).toContain('When the checks finish and Verification is Review or Verify, ask the team to review');
    expect(edit.textContent).not.toMatch(/Authenticity|Review suggested|Verification suggested/);
  });

  it('offers only asking a teammate to review, adding a tag and moving into a review stage, and says why once', async () => {
    renderEditor([checking]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Look closer') }));
    const edit = await sheet('Look closer');

    await fireEvent.click(within(edit).getByRole('button', { name: 'Add another step: then also' }));
    const list = await screen.findByRole('listbox');
    expect(within(list).getAllByRole('option').map((option) => option.textContent?.trim())).toEqual(['Move to a stage', 'Add a tag', 'Ask a teammate to review']);
    const popover = list.closest('[data-slot="popover-content"]') as HTMLElement;
    expect(popover).toHaveTextContent('A check can only ask for a closer look. A person decides.');
    expect(document.body.textContent!.split('A check can only ask for a closer look.').length - 1).toBe(1);
    expect(popover).not.toHaveTextContent(/reject|turn them down|email|interview/i);
  });

  it('moves only into a review stage', async () => {
    renderEditor([{ ...checking, actions: [{ type: 'MoveToStage', stageId: 'phone' }] }]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Look closer') }));
    const edit = await sheet('Look closer');

    await fireEvent.click(within(edit).getByRole('button', { name: 'Action 1: stage: Phone call' }));
    expect((await screen.findAllByRole('option')).map((option) => option.textContent?.trim())).toEqual(['Phone call', 'Trial shift']);
  });

  it('does not offer a check on an automation that sends an email', async () => {
    renderEditor([{ id: 'mail', name: 'Mail', trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'SendEmail', emailTemplate: 'application-received' }] }]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Mail') }));
    const edit = await sheet('Mail');

    await fireEvent.click(within(edit).getByRole('button', { name: 'Add a condition: and if' }));
    const options = (await screen.findAllByRole('option')).map((option) => option.textContent?.trim());
    expect(options).not.toContain('Verification');
    expect(options).toContain('Right to work');
    expect(await screen.findByText(/Some choices are left out/)).toBeInTheDocument();
  });
});

describe('Edit as rules', () => {
  it('swaps the sentence for the full form with the same choices, and back', async () => {
    renderEditor([
      { ...tagOnApply, conditions: { match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }] }, actions: [{ type: 'AssignReviewer', assigneeUserId: 'u-dana' }, { type: 'AddTag', tag: 'Not eligible' }] },
    ]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');
    const before = JSON.stringify(draft().automations[0]);

    await fireEvent.click(within(edit).getByRole('button', { name: 'Edit as rules' }));

    expect(within(edit).getByRole('combobox', { name: 'Trigger' })).toHaveValue('ApplicationSubmitted');
    expect(within(edit).getByRole('combobox', { name: /^Fact/ })).toHaveValue('answers.right_to_work');
    expect(within(edit).getByRole('combobox', { name: /^Value/ })).toHaveValue('no');
    expect(within(edit).getByRole('textbox', { name: 'Tag name' })).toHaveValue('Not eligible');
    expect(JSON.stringify(draft().automations[0])).toBe(before);

    await fireEvent.change(within(edit).getByRole('combobox', { name: /^Value/ }), { target: { value: 'yes' } });
    await fireEvent.click(within(edit).getByRole('button', { name: 'Edit as a sentence' }));

    expect(edit.querySelector('[data-sentence]')).toHaveTextContent('and their answer to Right to work is Yes');
    expect(draft().automations[0]!.conditions!.rules[0]).toMatchObject({ value: 'yes' });
  });

  it('keeps Remove on the full form, which the sentence leaves out', async () => {
    renderEditor([tagOnApply]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');
    expect(within(edit).queryByRole('button', { name: /^Remove/ })).toBeNull();

    await fireEvent.click(within(edit).getByRole('button', { name: 'Edit as rules' }));
    await fireEvent.click(within(edit).getByRole('button', { name: 'Remove Tag new people' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(draft().automations).toEqual([]);
  });

  it('keeps an automation the sentence cannot say in the sentence\'s own words, such as a saved rejection', async () => {
    renderEditor([
      {
        id: 'knockout',
        name: 'Knockout',
        trigger: { type: 'ApplicationSubmitted' },
        conditions: { match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }] },
        actions: [{ type: 'Reject', reason: 'Not eligible' }],
        allowAutoReject: true,
      },
    ]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Knockout') }));
    const edit = await sheet('Knockout');

    expect(edit.querySelector('[data-sentence]')).toHaveTextContent('turn them down, because Not eligible');
    await fireEvent.click(within(edit).getByRole('button', { name: 'Add another step: then also' }));
    expect((await screen.findAllByRole('option')).map((option) => option.textContent?.trim())).not.toContain('Turn them down');
  });
});

describe('the list', () => {
  it('is the Automations tab on a phone, grouped by stage, and a row opens the sentence', async () => {
    stubViewport(390);
    renderEditor([tagOnApply, { ...tagOnApply, id: 'trial', name: 'Mail at trial', trigger: { type: 'StageEntered', stageId: 'trial' }, actions: [{ type: 'SendEmail', emailTemplate: 'stage-change' }] }]);

    expect(within(panel()).getByRole('region', { name: 'Whole workflow' })).toHaveTextContent('Tag new people');
    expect(within(panel()).getByRole('region', { name: 'Trial shift' })).toHaveTextContent('Mail at trial');
    expect(within(panel()).queryByRole('group', { name: 'View' })).toBeNull();
    await fireEvent.click(within(panel()).getByRole('button', { name: /Mail at trial/ }));
    expect(sentenceText(await sheet('Mail at trial'))).toContain('When someone moves into Trial shift, send the email Moved to the next step.');
  });
});
