// Editing an automation as a sentence, adding one from the recipes, and the list the Automations tab is on a phone. The model
// behind the blanks is tested in #lib/automation-sentence.test.ts; this is the screen: the sheet, the blanks and their lists,
// Done, and the Sentence and Rules tabs.
import type { Automation, HiringWorkflow } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub, roleCatalogue } from '../../../test-support/fixtures.js';
import { stubViewport } from '../../../test-support/viewport.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import WorkflowHarness from '../../../test-support/workflow-harness.svelte';
import { preferences } from '#lib/preferences.svelte.js';

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
  // jsdom has no Web Animations, and a step leaving Rules' list asks for its running ones (`animate:flip`).
  Element.prototype.getAnimations = () => [];
  workflow = structuredClone(base);
  // The tab an automation opens on is remembered in this browser: each test starts from the default, Sentence.
  localStorage.removeItem('screenhire:automation-view');
  preferences.automationView = 'sentence';
});

afterEach(() => {
  delete (Element.prototype as Partial<Element>).getAnimations;
  vi.unstubAllGlobals();
});

function renderEditor(automations: Automation[] = [], errors: { path: string; message: string }[] = []) {
  workflow = { ...structuredClone(base), automations: structuredClone(automations) };
  return render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow, errors, mode: 'editor', tab: 'automations' } } });
}

const panel = () => screen.getByRole('tabpanel', { name: 'Automations' });
/** Each section as it reads, one entry a line: its keyword, then its clauses. */
const sentenceText = (scope: HTMLElement) =>
  [...scope.querySelectorAll('[data-section]')].map((section) =>
    [section.getAttribute('data-section'), ...[...section.querySelectorAll('[data-clause]')].map((clause) => clause.textContent!.replace(/\s+/g, ' ').trim())].join(' | '),
  );
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
      'Get the team to act',
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
    expect(within(recipes).getAllByRole('status')[0]).toHaveTextContent('Added "Thank people when they apply".');
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
    expect(sentenceText(edit)).toEqual(['When | someone applies', 'If', 'Then | send the email Application received']);
  });

  it('removes the recipe just added from the same place, and offers it again', async () => {
    renderEditor();
    await fireEvent.click(within(panel()).getByRole('button', { name: 'Add an automation' }));
    const recipes = await sheet('Add an automation');
    await fireEvent.click(within(recipes).getByRole('button', { name: 'Add Thank people when they apply' }));

    await fireEvent.click(within(recipes).getByRole('button', { name: 'Remove Thank people when they apply' }));

    expect(draft().automations).toEqual([]);
    expect(within(recipes).getByRole('button', { name: 'Add Thank people when they apply' })).toBeInTheDocument();
    await fireEvent.click(within(recipes).getByRole('button', { name: 'Undo' }));
    await waitFor(() => expect(draft().automations).toHaveLength(1));
  });

  it('shows only the recipes of a stage when opened from the stage, and Write your own adds there', async () => {
    renderEditor([tagOnApply]);

    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('New automation at Trial shift') }));
    const recipes = await sheet('Add an automation');
    expect(within(recipes).getAllByRole('listitem').map((row) => row.textContent)).toEqual([
      expect.stringContaining('Tell people when they reach Trial shift'),
      expect.stringContaining('Ask the team to review at Trial shift'),
    ]);

    await fireEvent.click(within(recipes).getByRole('button', { name: 'Write your own' }));
    const edit = await sheet('When someone moves into Trial shift');
    expect(draft().automations.at(-1)).toMatchObject({ trigger: { type: 'StageEntered', stageId: 'trial' }, actions: [] });
    expect(sentenceText(edit)).toEqual(['When | someone moves into Trial shift', 'If', 'Then']);
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
  it('opens an automation with its name as the title, On beside it and Done in the footer, and no subtitle', async () => {
    renderEditor([tagOnApply]);
    await fireEvent.click(within(panel()).getByRole('button', { name: 'List' }));
    await fireEvent.click(within(panel()).getByRole('button', { name: /^Tag new people/ }));

    const edit = await sheet('Tag new people');
    expect(within(edit).getByRole('textbox', { name: 'Name' })).toHaveValue('Tag new people');
    expect(within(edit).getByRole('switch')).toBeChecked();
    expect(within(edit).getByText('On')).toBeInTheDocument();
    expect(within(edit).getByRole('button', { name: 'Done' })).toBeInTheDocument();
    expect(edit.querySelector('[data-slot="sheet-description"]')).toBeNull();
    expect(sentenceText(edit)).toEqual(['When | someone applies', 'If', 'Then | add the tag new']);
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

  it('adds a condition with the plus that ends If, grouped as Their answers, The application and ScreenHire\'s checks', async () => {
    renderEditor([tagOnApply]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    await fireEvent.click(within(edit).getByRole('button', { name: 'Add a condition: a condition' }));
    const list = await screen.findByRole('listbox');
    expect([...list.querySelectorAll('[data-command-group-heading]')].map((heading) => heading.textContent?.trim())).toEqual(['Their answers', 'The application', "ScreenHire's checks"]);
    await fireEvent.click(within(list).getByRole('option', { name: 'Right to work' }));

    expect(draft().automations[0]!.conditions).toEqual({ match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'yes' }] });
    await choose(edit, /^Condition 1: value/, 'No');
    expect(draft().automations[0]!.conditions!.rules[0]).toEqual({ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' });
    expect(sentenceText(edit)[1]).toBe('If | their answer to Right to work is No');
    expect(within(edit).getByRole('button', { name: 'Add a condition: another condition' })).toBeInTheDocument();
  });

  it('adds a step with the plus that ends Then, opens the new tag for typing, and drops it when it is left empty', async () => {
    renderEditor([{ ...tagOnApply, actions: [{ type: 'NotifyTeam', note: 'Hello' }] }]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    await choose(edit, 'Add a step: another action', 'Add a tag');
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

  it('removes a condition or a step with the x at its end, and Undo, inside the sheet, brings it back', async () => {
    renderEditor([{ ...tagOnApply, conditions: { match: 'All', rules: [{ fact: 'application.tags', operator: 'Exists' }] }, actions: [{ type: 'AddTag', tag: 'a' }, { type: 'AddTag', tag: 'b' }] }]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');
    const before = draft().automations[0];

    await fireEvent.click(within(edit).getByRole('button', { name: 'Remove condition 1' }));
    expect(draft().automations[0]!.conditions).toBeNull();
    // The focus goes where the next condition would be added, not to the top of the sheet.
    await waitFor(() => expect(within(edit).getByRole('button', { name: 'Add a condition: a condition' })).toHaveFocus());
    await fireEvent.click(within(edit).getByRole('button', { name: 'Undo' }));
    await waitFor(() => expect(draft().automations[0]).toEqual(before));

    await fireEvent.click(within(edit).getByRole('button', { name: 'Remove step 1' }));
    expect(draft().automations[0]!.actions).toEqual([{ type: 'AddTag', tag: 'b' }]);
    await waitFor(() => expect(within(edit).getByRole('button', { name: 'Remove step 1' })).toHaveFocus());
    await fireEvent.click(within(edit).getByRole('button', { name: 'Undo' }));
    await waitFor(() => expect(draft().automations[0]).toEqual(before));
  });

  it('hands its Undo to the page when the sheet closes', async () => {
    renderEditor([{ ...tagOnApply, actions: [{ type: 'AddTag', tag: 'a' }, { type: 'AddTag', tag: 'b' }] }]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');
    await fireEvent.click(within(edit).getByRole('button', { name: 'Remove step 2' }));
    await fireEvent.click(within(edit).getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

    await fireEvent.click(screen.getByRole('button', { name: 'Undo' }));

    await waitFor(() => expect(draft().automations[0]!.actions).toHaveLength(2));
  });

  it('starts each line with its joining word, and the word between conditions is a choice', async () => {
    renderEditor([
      { ...tagOnApply, conditions: { match: 'All', rules: [{ fact: 'application.tags', operator: 'Exists' }, { fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }] }, actions: [{ type: 'AddTag', tag: 'a' }, { type: 'AddTag', tag: 'b' }] },
    ]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    expect(sentenceText(edit)).toEqual(['When | someone applies', 'If | their tags is given | and their answer to Right to work is No', 'Then | add the tag a | and add the tag b']);
    await choose(edit, 'Conditions join: and', 'or: any one of them holds');
    expect(draft().automations[0]!.conditions!.match).toBe('Any');
    expect(sentenceText(edit)[1]).toBe('If | their tags is given | or their answer to Right to work is No');
  });

  it('shows the validator\'s message under the sentence and marks the blank it is about', async () => {
    renderEditor([tagOnApply], [{ path: 'automations[0].actions[0]', message: 'This step is not allowed here.' }]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    expect(edit).toHaveTextContent('This step is not allowed here.');
    expect(within(edit).getByRole('button', { name: 'Action 1: add the tag' })).toHaveAttribute('data-invalid', 'true');
    expect(within(edit).getByRole('button', { name: 'Trigger: someone applies' })).not.toHaveAttribute('data-invalid');
  });
});

describe('work in progress', () => {
  const blank: Automation = { ...tagOnApply, actions: [{ type: 'AssignReviewer' }, { type: 'SendEmail', emailTemplate: '' }] };
  const error = { path: 'automations[0].actions[1].emailTemplate', message: 'This step needs an email.' };
  const trigger = () => within(panel()).getByRole('button', { name: (name) => name.includes('When Tag new people') });

  it('shows no red while its sheet is open, in the sheet, on the map or among the problems, and all of it once it closes', async () => {
    renderEditor([blank], [error]);
    const problems = () => screen.queryByRole('region', { name: 'Problems to fix', hidden: true });
    expect(problems()).toHaveTextContent(error.message);

    await fireEvent.click(trigger());
    const edit = await sheet('Tag new people');
    expect(edit).not.toHaveTextContent(error.message);
    expect(edit.querySelector('[data-invalid], [aria-invalid]')).toBeNull();
    expect(problems()).toBeNull();
    await waitFor(() => expect(screen.getByRole('tabpanel', { name: 'Automations', hidden: true })).not.toHaveTextContent(error.message));
    await fireEvent.click(within(edit).getByRole('tab', { name: 'Rules' }));
    expect(edit).not.toHaveTextContent(error.message);

    await fireEvent.click(within(edit).getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(problems()).toHaveTextContent(error.message);
    expect(panel()).toHaveTextContent(error.message);
  });

  it('shows it when Fix opened the automation, and the next time it is opened another way it is work in progress again', async () => {
    renderEditor([blank], [error]);
    await fireEvent.click(screen.getByRole('button', { name: /^Fix: / }));
    const edit = await sheet('Tag new people');
    expect(edit).toHaveTextContent(error.message);
    await fireEvent.click(within(edit).getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

    await fireEvent.click(trigger());

    expect(await sheet('Tag new people')).not.toHaveTextContent(error.message);
  });

  it("still shows another automation's problems, and always a refusal of its own", async () => {
    renderEditor(
      [blank, { ...tagOnApply, id: 'other', name: 'Other', actions: [{ type: 'AddTag', tag: '' }] }],
      [error, { path: 'automations[0].actions[0]', message: 'A check can only ask for a closer look.' }, { path: 'automations[1].actions[0].tag', message: 'The tag in "Other" has no name.' }],
    );
    await fireEvent.click(trigger());
    const edit = await sheet('Tag new people');

    expect(edit).toHaveTextContent('A check can only ask for a closer look.');
    expect(edit).not.toHaveTextContent(error.message);
    expect(screen.getByRole('region', { name: 'Problems to fix', hidden: true })).toHaveTextContent('The tag in "Other" has no name.');
  });
});

describe('a new automation', () => {
  async function write() {
    renderEditor([tagOnApply]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('New automation at Applied') }));
    await fireEvent.click(within(await sheet('Add an automation')).getByRole('button', { name: 'Write your own' }));
    return sheet('When someone moves into Applied');
  }
  const name = (scope: HTMLElement) => (within(scope).getByRole('textbox', { name: 'Name' }) as HTMLInputElement).value;

  it('has its trigger only, and a name that follows the trigger and the steps until one is typed', async () => {
    const edit = await write();
    expect(draft().automations[1]).toMatchObject({ conditions: null, actions: [] });

    await choose(edit, 'Trigger: someone moves into', 'Someone applies');
    await waitFor(() => expect(name(edit)).toBe('When someone applies'));
    await choose(edit, 'Add a step: an action', 'Ask a teammate to review');
    await waitFor(() => expect(name(edit)).toBe('Ask the team to review when someone applies'));
    await fireEvent.click(within(edit).getByRole('tab', { name: 'Rules' }));
    await fireEvent.change(within(edit).getByRole('combobox', { name: 'Action 1' }), { target: { value: 'ScheduleInterview' } });
    await waitFor(() => expect(name(edit)).toBe('Schedule an interview when someone applies'));

    await fireEvent.input(within(edit).getByRole('textbox', { name: 'Name' }), { target: { value: 'Book a call' } });
    await fireEvent.change(within(edit).getByRole('combobox', { name: 'Trigger' }), { target: { value: 'Manual' } });
    expect(name(edit)).toBe('Book a call');
    expect(draft().automations[1]!.name).toBe('Book a call');
  });

  it('goes back to following when its name is left empty', async () => {
    const edit = await write();
    const field = within(edit).getByRole('textbox', { name: 'Name' });
    await fireEvent.input(field, { target: { value: '' } });
    await fireEvent.blur(field);

    expect(name(edit)).toBe('When someone moves into Applied');
    await choose(edit, 'Add a step: an action', 'Ask a teammate to review');
    await waitFor(() => expect(name(edit)).toBe('Ask the team to review when someone moves into Applied'));
  });

  it('never renames an automation somebody named', async () => {
    renderEditor([tagOnApply]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (label) => label.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');

    await choose(edit, 'Add a step: another action', 'Ask a teammate to review');

    expect(name(edit)).toBe('Tag new people');
  });

  it('is removed when its sheet closes without a step, and Undo brings it back', async () => {
    const edit = await write();
    await fireEvent.keyDown(edit, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

    expect(draft().automations.map((automation) => automation.id)).toEqual(['tag']);
    expect(screen.getByText('Removed "When someone moves into Applied", which had no step.')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Undo' }));
    await waitFor(() => expect(draft().automations).toHaveLength(2));
  });

  it('comes back with the step that was taken out of it just before', async () => {
    renderEditor([tagOnApply]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (label) => label.includes('When Tag new people') }));
    const edit = await sheet('Tag new people');
    await fireEvent.click(within(edit).getByRole('button', { name: 'Remove step 1' }));
    await fireEvent.click(within(edit).getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(draft().automations).toEqual([]));

    await fireEvent.click(screen.getByRole('button', { name: 'Undo' }));

    await waitFor(() => expect(draft().automations).toEqual([tagOnApply]));
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

    expect(sentenceText(edit)).toEqual(['When | the checks finish', 'If | Verification is Review or Verify', 'Then | ask the team to review']);
    expect(edit.textContent).not.toMatch(/Authenticity|Review suggested|Verification suggested/);
  });

  it('offers only asking a teammate to review, adding a tag and moving into a review stage, and says why once', async () => {
    renderEditor([checking]);
    await fireEvent.click(within(panel()).getByRole('button', { name: (name) => name.includes('When Look closer') }));
    const edit = await sheet('Look closer');

    await fireEvent.click(within(edit).getByRole('button', { name: 'Add a step: another action' }));
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

    await fireEvent.click(within(edit).getByRole('button', { name: 'Add a condition: a condition' }));
    const options = (await screen.findAllByRole('option')).map((option) => option.textContent?.trim());
    expect(options).not.toContain('Verification');
    expect(options).toContain('Right to work');
    expect(await screen.findByText(/Some choices are left out/)).toBeInTheDocument();
  });
});

describe('the Sentence and Rules tabs', () => {
  const knockout: Automation = {
    id: 'knockout',
    name: 'Knockout',
    trigger: { type: 'ApplicationSubmitted' },
    conditions: { match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }] },
    actions: [{ type: 'Reject', reason: 'Not eligible' }],
    allowAutoReject: true,
  };
  const open = async (name: string) => {
    await fireEvent.click(within(panel()).getByRole('button', { name: (label) => label.includes(`When ${name}`) }));
    return sheet(name);
  };
  const tab = (scope: HTMLElement, name: 'Sentence' | 'Rules') => within(scope).getByRole('tab', { name });

  it('edit the same automation under one Name and one On, so switching loses nothing', async () => {
    renderEditor([
      { ...tagOnApply, conditions: { match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }] }, actions: [{ type: 'AssignReviewer', assigneeUserId: 'u-dana' }, { type: 'AddTag', tag: 'Not eligible' }] },
    ]);
    const edit = await open('Tag new people');
    expect(tab(edit, 'Sentence')).toHaveAttribute('aria-selected', 'true');
    expect(within(edit).queryByRole('button', { name: /^Edit as/ })).toBeNull();
    const before = JSON.stringify(draft().automations[0]);
    const name = within(edit).getByRole('textbox', { name: 'Name' });
    const on = within(edit).getByRole('switch');

    await fireEvent.click(tab(edit, 'Rules'));

    expect(tab(edit, 'Rules')).toHaveAttribute('aria-selected', 'true');
    expect(within(edit).getByRole('textbox', { name: 'Name' })).toBe(name);
    expect(within(edit).getAllByRole('switch')).toEqual([on]);
    expect(within(edit).queryByRole('checkbox', { name: 'On' })).toBeNull();
    expect(within(edit).getByRole('combobox', { name: 'Trigger' })).toHaveValue('ApplicationSubmitted');
    expect(within(edit).getByRole('combobox', { name: /^Fact/ })).toHaveValue('answers.right_to_work');
    expect(within(edit).getByRole('combobox', { name: /^Value/ })).toHaveValue('no');
    expect(within(edit).getByRole('textbox', { name: 'Tag name' })).toHaveValue('Not eligible');
    expect(JSON.stringify(draft().automations[0])).toBe(before);
    // The name shows once, as the sheet's title, with no heading repeating it under the tabs.
    expect(within(edit).getAllByText('Tag new people')).toHaveLength(1);
    expect(within(edit).getAllByRole('heading', { name: 'Tag new people' })).toHaveLength(1);

    await fireEvent.change(within(edit).getByRole('combobox', { name: /^Value/ }), { target: { value: 'yes' } });
    await fireEvent.click(tab(edit, 'Sentence'));

    expect(sentenceText(edit)[1]).toBe('If | their answer to Right to work is Yes');
    expect(draft().automations[0]!.conditions!.rules[0]).toMatchObject({ value: 'yes' });
  });

  it('open every automation on the tab the person chose last', async () => {
    renderEditor([tagOnApply, { ...tagOnApply, id: 'trial', name: 'Mail at trial', trigger: { type: 'StageEntered', stageId: 'trial' }, actions: [{ type: 'SendEmail', emailTemplate: 'stage-change' }] }]);
    const first = await open('Tag new people');
    await fireEvent.click(tab(first, 'Rules'));
    await fireEvent.click(within(first).getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

    const second = await open('Mail at trial');

    expect(tab(second, 'Rules')).toHaveAttribute('aria-selected', 'true');
    expect(within(second).getByRole('combobox', { name: 'Trigger' })).toHaveValue('StageEntered');
    expect(localStorage.getItem('screenhire:automation-view')).toBe('rules');

    // And back: choosing Sentence is remembered the same way.
    await fireEvent.click(tab(second, 'Sentence'));
    await fireEvent.click(within(second).getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

    const again = await open('Tag new people');

    expect(tab(again, 'Sentence')).toHaveAttribute('aria-selected', 'true');
    expect(again.querySelector('[data-sentence]')).not.toBeNull();
    expect(localStorage.getItem('screenhire:automation-view')).toBe('sentence');
  });

  it('open one too complex for a sentence on Rules, and the Sentence tab says why without changing the choice', async () => {
    renderEditor([knockout]);

    const edit = await open('Knockout');

    expect(tab(edit, 'Rules')).toHaveAttribute('aria-selected', 'true');
    expect(within(edit).getByRole('checkbox', { name: 'Reject without a person reviewing first' })).toBeChecked();
    expect(preferences.automationView).toBe('sentence');

    await fireEvent.click(tab(edit, 'Sentence'));

    expect(edit.querySelector('[data-sentence]')).toBeNull();
    expect(within(edit).getByRole('tabpanel', { name: 'Sentence' })).toHaveTextContent(
      'Too complex for a sentence: it turns someone down, and whether a person reviews first is set on Rules.',
    );
  });

  it.each(['Sentence', 'Rules'] as const)('keep Remove automation in the footer menu, on %s too, and Undo brings it back', async (view) => {
    renderEditor([tagOnApply]);
    const edit = await open('Tag new people');
    await fireEvent.click(tab(edit, view));
    expect(within(edit).queryByRole('button', { name: 'Remove automation' })).toBeNull();

    await fireEvent.pointerDown(within(edit).getByRole('button', { name: 'More for Tag new people' }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Remove automation' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(draft().automations).toEqual([]);
    await fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));
    await waitFor(() => expect(draft().automations).toEqual([tagOnApply]));
  });

  it('give When, If and Then the same muted words in both', async () => {
    renderEditor([tagOnApply]);
    const edit = await open('Tag new people');
    const words = () => [...edit.querySelectorAll('[data-slot="badge"]')].map((badge) => badge.textContent?.trim());
    expect(words()).toEqual(['When', 'If', 'Then']);

    await fireEvent.click(tab(edit, 'Rules'));

    expect(words()).toEqual(['When', 'If', 'Then']);
  });

  it('tick a fixed list by its names on Rules, and store the codes they stand for', async () => {
    renderEditor([
      {
        id: 'check',
        name: 'Check',
        trigger: { type: 'VerificationAssessed' },
        conditions: {
          match: 'Any',
          rules: [
            { fact: 'verification.authenticity', operator: 'In', values: ['ReviewSuggested', 'VerificationRecommended'] },
            { fact: 'verification.aiAssistance', operator: 'EqualTo', value: 'SomeIndicators', negate: true },
          ],
        },
        actions: [{ type: 'AssignReviewer' }],
      },
    ]);
    const edit = await open('Check');
    const before = JSON.stringify(draft().automations[0]);
    await fireEvent.click(tab(edit, 'Rules'));

    const first = within(edit).getByRole('button', { name: 'Value for condition 1: Review or Verify' });
    expect(within(edit).getByRole('button', { name: 'Value for condition 2: Some signs of AI help' })).toBeInTheDocument();
    expect(within(edit).queryByRole('textbox', { name: /^Value/ })).toBeNull();
    expect(edit).not.toHaveTextContent('commas');
    expect(edit.textContent!.split('A verification signal can route a candidate for review, never reject them.')).toHaveLength(2);
    expect(JSON.stringify(draft().automations[0])).toBe(before);

    await fireEvent.click(first);
    const values = within(await screen.findByRole('listbox', { name: 'Value for condition 1' }));
    expect(values.getAllByRole('option').map((option) => option.textContent?.trim())).toEqual(['Clear', 'Review', 'Verify']);
    await fireEvent.click(values.getByRole('option', { name: 'Review' }));
    // One value is stored as is, and the last tick stays.
    expect(draft().automations[0]!.conditions!.rules[0]).toEqual({ fact: 'verification.authenticity', operator: 'EqualTo', value: 'VerificationRecommended' });
    await fireEvent.click(values.getByRole('option', { name: 'Verify' }));
    expect(draft().automations[0]!.conditions!.rules[0]).toEqual({ fact: 'verification.authenticity', operator: 'EqualTo', value: 'VerificationRecommended' });
    await fireEvent.click(values.getByRole('option', { name: 'Review' }));
    expect(draft().automations[0]!.conditions!.rules[0]).toEqual({ fact: 'verification.authenticity', operator: 'In', values: ['ReviewSuggested', 'VerificationRecommended'] });
    await expectNoAxeViolations(edit);
  });

  it('keep a text field on Rules for free text only', async () => {
    renderEditor([{ ...tagOnApply, conditions: { match: 'All', rules: [{ fact: 'application.tags', operator: 'In', values: ['a', 'b'] }] } }]);
    const edit = await open('Tag new people');
    await fireEvent.click(tab(edit, 'Rules'));

    const field = within(edit).getByRole('textbox', { name: /^Values/ });
    expect(field).toHaveValue('a, b');
    await fireEvent.change(field, { target: { value: 'x, y ,' } });
    expect(draft().automations[0]!.conditions!.rules[0]!.values).toEqual(['x', 'y']);
    expect(edit).not.toHaveTextContent('A verification signal can route');
  });

  it('offer no step on Rules that the guardrails refuse after a check', async () => {
    renderEditor([{ id: 'check', name: 'Check', trigger: { type: 'VerificationAssessed' }, conditions: null, actions: [{ type: 'AssignReviewer' }] }]);
    const edit = await open('Check');
    await fireEvent.click(tab(edit, 'Rules'));
    await fireEvent.pointerDown(within(edit).getByRole('button', { name: 'Add action' }), { button: 0, pointerType: 'mouse' });

    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim())).toEqual(['Move to a stage', 'Add a tag', 'Assign a reviewer']);
  });

  it('say must-have, not knockout, where a rejection is opted into', async () => {
    renderEditor([knockout]);
    const edit = await open('Knockout');

    expect(edit).toHaveTextContent('Only a must-have automation may reject');
    expect(edit).not.toHaveTextContent(/knockout automation/i);
  });

  it('give Rules a remove button on every condition and action, and add more from chips', async () => {
    renderEditor([{ ...tagOnApply, conditions: { match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }] } }]);
    const edit = await open('Tag new people');
    await fireEvent.click(tab(edit, 'Rules'));

    await fireEvent.click(within(edit).getByRole('button', { name: 'Add condition' }));
    await fireEvent.pointerDown(within(edit).getByRole('button', { name: 'Add action' }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Assign a reviewer' }));

    expect(within(edit).getAllByRole('button', { name: /^Remove condition \d$/ })).toHaveLength(2);
    expect(within(edit).getAllByRole('button', { name: /^Remove action \d$/ })).toHaveLength(2);
    const before = draft().automations[0];
    await fireEvent.click(within(edit).getByRole('button', { name: 'Remove action 1' }));
    expect(draft().automations[0]!.actions).toEqual([{ type: 'AssignReviewer' }]);
    await fireEvent.click(within(edit).getByRole('button', { name: 'Undo' }));
    await waitFor(() => expect(draft().automations[0]).toEqual(before));
    await expectNoAxeViolations(edit);
  });
});

describe('the list', () => {
  it('is the Automations tab on a phone, grouped by stage, and a row opens the sentence', async () => {
    stubViewport(390);
    renderEditor([tagOnApply, { ...tagOnApply, id: 'trial', name: 'Mail at trial', trigger: { type: 'StageEntered', stageId: 'trial' }, actions: [{ type: 'SendEmail', emailTemplate: 'stage-change' }] }]);

    expect(within(panel()).getByRole('region', { name: 'Applied' })).toHaveTextContent('Tag new people');
    expect(within(panel()).getByRole('region', { name: 'Trial shift' })).toHaveTextContent('Mail at trial');
    expect(within(panel()).queryByRole('group', { name: 'View' })).toBeNull();
    await fireEvent.click(within(panel()).getByRole('button', { name: /^Mail at trial/ }));
    expect(sentenceText(await sheet('Mail at trial'))).toEqual(['When | someone moves into Trial shift', 'If', 'Then | send the email Moved to the next step']);
  });

  it('removes an automation from the menu beside its row, with Undo', async () => {
    renderEditor([tagOnApply]);
    await fireEvent.click(within(panel()).getByRole('button', { name: 'List' }));
    const menu = within(panel()).getByRole('button', { name: 'More for Tag new people' });
    expect(menu.parentElement!.closest('button')).toBeNull();

    await fireEvent.pointerDown(menu, { button: 0, pointerType: 'mouse' });
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Remove automation' }));

    await waitFor(() => expect(draft().automations).toEqual([]));
    await fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));
    await waitFor(() => expect(draft().automations).toEqual([tagOnApply]));
  });
});
