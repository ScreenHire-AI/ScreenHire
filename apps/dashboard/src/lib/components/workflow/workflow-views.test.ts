// A workflow as the builder shows it and as the read-only view reads it (design system section 7), and the validation
// result beside its title.
import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import { stubViewport } from '../../../test-support/viewport.js';
import WorkflowHarness from '../../../test-support/workflow-harness.svelte';
import ValidationBadge from './validation-badge.svelte';

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone-screen', name: 'Phone screen', kind: 'Screening', slaDays: 3 },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: {
    questions: [
      {
        id: 'right_to_work',
        type: 'YesNo',
        label: 'Do you have the right to work here?',
        required: true,
        knockout: { disqualifyingAnswer: false, reason: 'The role needs the right to work.' },
      },
    ],
  },
  integrityPolicy: { aiUse: 'AllowedWithDisclosure' },
  automations: [
    { id: 'tag-screen', name: 'Tag screened', trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'AddTag', tag: 'screened' }] },
    { id: 'notify', name: 'Tell the team', enabled: false, trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'NotifyTeam', note: 'New' }] },
  ],
};

const renderViews = (mode: 'editor' | 'summary', tab = 'pipeline', errors: DefinitionError[] = []) =>
  render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: structuredClone(workflow), errors, mode, tab } } });

const controls = (root: Element) => root.querySelectorAll('input, select, textarea');

describe('validation badge', () => {
  it('is busy while a check is on its way', () => {
    render(ValidationBadge, { props: { errors: [], checking: true } });

    const region = screen.getByRole('region', { name: 'Validation' });
    expect(region).toHaveAttribute('aria-busy', 'true');
  });

  it('says how many problems are left once the latest change is checked, as a status badge', () => {
    render(ValidationBadge, { props: { errors: [{ path: 'stages', message: 'A workflow needs a hired stage.' }], checking: false } });

    const region = screen.getByRole('region', { name: 'Validation' });
    expect(region).toHaveAttribute('aria-busy', 'false');
    expect(region).toHaveTextContent('1 problem to fix');
    expect(region.querySelector('[data-status="verify"]')).not.toBeNull();
  });

  it('counts problems, not problems to fix, when the workflow cannot be edited here', () => {
    render(ValidationBadge, { props: { errors: [{ path: 'stages', message: 'A workflow needs a hired stage.' }], editable: false } });

    const region = screen.getByRole('region', { name: 'Validation' });
    expect(region).toHaveTextContent('1 problem');
    expect(region).not.toHaveTextContent('to fix');
  });

  it('shows a clear badge when there is nothing to fix', () => {
    render(ValidationBadge, { props: { errors: [] } });

    expect(screen.getByRole('region', { name: 'Validation' })).toHaveTextContent('No problems found');
    expect(screen.getByRole('region', { name: 'Validation' }).querySelector('[data-status="clear"]')).not.toBeNull();
  });
});

describe('read-only workflow', () => {
  it('reads the pipeline as content: stages on a line, end stages in the footer, automations as rows', async () => {
    const { container } = renderViews('summary');

    const pane = screen.getByRole('tabpanel', { name: 'Pipeline' });
    expect(controls(pane)).toHaveLength(0);

    const stages = within(pane).getByRole('region', { name: 'Stages' });
    const line = within(stages).getByRole('list', { name: 'Stages in order' });
    expect(within(line).getAllByRole('listitem').map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      '1 Applied',
      '2 Phone screen Screening 3-day limit',
    ]);
    const footer = stages.lastElementChild as HTMLElement;
    expect(footer).toHaveTextContent('End stages');
    expect(within(footer).getByText('Hired')).toHaveAttribute('data-slot', 'badge');
    expect(within(footer).getByText('Not progressing')).toHaveAttribute('data-slot', 'badge');

    const automations = within(pane).getAllByRole('region', { name: 'Automations' });
    expect(automations).toHaveLength(1);
    const rows = within(automations[0]!).getAllByRole('listitem');
    expect(rows[0]).toHaveTextContent('When a candidate enters Phone screen: add the tag "screened".');
    expect(rows[1]).toHaveTextContent('Off');

    expect(container.querySelectorAll(':disabled')).toHaveLength(0);
    await expectNoAxeViolations(container);
  });

  it('reads the questionnaire as content, with the candidate preview behind the switch (A8)', async () => {
    const { container } = renderViews('summary', 'questionnaire');

    const pane = screen.getByRole('tabpanel', { name: 'Questionnaire' });
    expect(within(pane).getByRole('region', { name: 'AI use while applying' })).toHaveTextContent('Allowed with disclosure');
    const questions = within(pane).getByRole('region', { name: 'Screening questions' });
    expect(controls(questions)).toHaveLength(0);
    expect(questions).toHaveTextContent('Do you have the right to work here?');
    expect(within(questions).getByText('Knockout if no')).toHaveAttribute('data-slot', 'badge');
    expect(within(questions).getByText('Required')).toHaveAttribute('data-slot', 'badge');
    await expectNoAxeViolations(container);

    // The candidate's form never shares the screen with the content: the switch swaps them.
    expect(within(pane).queryByRole('group', { name: 'Screen size' })).toBeNull();
    await fireEvent.click(within(pane).getByRole('button', { name: 'Preview' }));
    expect(within(pane).queryByRole('region', { name: 'Screening questions' })).toBeNull();
    expect(within(pane).getByRole('group', { name: 'Screen size' })).toBeInTheDocument();
    expect(within(pane).getByRole('group', { name: 'Do you have the right to work here?' })).toBeInTheDocument();
  });

  it('reads each follow-up questionnaire as its own panel', () => {
    const followUp: HiringWorkflow = {
      ...workflow,
      followUpQuestionnaires: [{ id: 'availability', name: 'Availability', questions: [{ id: 'start', type: 'ShortText', label: 'When could you start?' }] }],
    };
    render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: followUp, errors: [], mode: 'summary', tab: 'questionnaire' } } });

    const panel = screen.getByRole('region', { name: 'Follow-up: Availability' });
    expect(controls(panel)).toHaveLength(0);
    expect(panel).toHaveTextContent('When could you start?');
  });

  it('lists each problem in words in a Problems region, with nothing to fix and no controls', () => {
    const { container } = renderViews('summary', 'pipeline', [
      { path: 'stages[1].name', message: 'Give the stage a name.' },
      { path: 'stages', message: 'A workflow needs a hired stage.' },
    ]);

    const problems = screen.getByRole('region', { name: 'Problems' });
    const items = within(problems).getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent('Give the stage a name.');
    expect(items[1]).toHaveTextContent('A workflow needs a hired stage.');
    expect(screen.queryByRole('region', { name: 'Problems to fix' })).toBeNull();
    expect(container.querySelectorAll(':disabled')).toHaveLength(0);
  });

  it('has no Problems region when the stored workflow is valid', () => {
    renderViews('summary');

    expect(screen.queryByRole('region', { name: 'Problems' })).toBeNull();
  });

  it('draws the graph with focusable nodes and no buttons that edit', () => {
    renderViews('summary', 'graph');

    const pane = screen.getByRole('tabpanel', { name: 'Graph' });
    expect(within(pane).getByRole('region', { name: 'Stages and automations' })).toBeInTheDocument();
    expect(within(pane).getByRole('group', { name: 'When: Tag screened' })).toHaveAttribute('tabindex', '0');
    expect(within(pane).queryByRole('button', { name: /^(Edit|Add automation|Remove) / })).toBeNull();
  });
});

describe('workflow builder', () => {
  it('lists the problems to fix in a panel, and none when there are none', () => {
    renderViews('editor', 'pipeline', [{ path: 'stages[1].name', message: 'Give the stage a name.' }]);

    const problems = screen.getByRole('region', { name: 'Problems to fix' });
    expect(problems).toHaveTextContent('Stage "Phone screen": Give the stage a name.');
  });

  it('has no problems panel when the workflow is valid', () => {
    renderViews('editor');

    expect(screen.queryByRole('region', { name: 'Problems to fix' })).toBeNull();
  });

  it('edits stages as rows in one panel that reorder with a grip, leaving out moves past either end (A7)', async () => {
    const { container } = renderViews('editor');

    const stages = screen.getByRole('region', { name: 'Stages' });
    // Hired and Not progressing end the application, so a time limit there would do nothing.
    expect(within(stages).getAllByRole('spinbutton', { name: 'Time limit (days)' })).toHaveLength(2);
    expect(within(stages).queryByRole('button', { name: /^Move .* (earlier|later)$/ })).toBeNull();
    // No card inside the panel (S4): stages are rows.
    expect(stages.querySelectorAll('[data-slot="panel"]')).toHaveLength(0);

    await fireEvent.click(within(stages).getByRole('button', { name: 'Reorder Applied' }));
    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim())).toEqual(['Move down', 'Move to bottom']);
    await fireEvent.click(screen.getByRole('menuitem', { name: 'Move down' }));
    expect(within(stages).getAllByRole('group', { name: /^Stage / })[0]).toHaveAccessibleName('Stage 1: Phone screen');

    expect(screen.getByRole('region', { name: 'Whole workflow' })).toHaveTextContent('When an application arrives: notify the team. (off)');
    await expectNoAxeViolations(container);
  });

  it('adds a follow-up questionnaire beside the apply form, with no file questions and a preview of its own', async () => {
    const { container } = renderViews('editor', 'questionnaire');
    const pane = screen.getByRole('tabpanel', { name: 'Questionnaire' });
    const choices = within(pane).getByRole('group', { name: 'Questionnaire' });
    expect(within(choices).getByRole('button', { name: 'Apply form' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(pane).getByRole('region', { name: 'Screening questions' })).toBeInTheDocument();

    await fireEvent.click(within(pane).getByRole('button', { name: 'Add follow-up questionnaire' }));

    expect(within(choices).getByRole('button', { name: 'New questionnaire' })).toHaveAttribute('aria-pressed', 'true');
    const name = within(pane).getByRole('textbox', { name: /^Name/ });
    await fireEvent.input(name, { target: { value: 'Availability' } });
    expect(within(choices).getByRole('button', { name: 'Availability' })).toBeInTheDocument();
    const questions = within(pane).getByRole('region', { name: 'Questions' });
    expect(within(questions).getByRole('button', { name: /^Edit question 1: New question / })).toBeInTheDocument();
    await expectNoAxeViolations(container);

    // A follow-up's page takes no uploads, so its answer menu has no File.
    await fireEvent.pointerDown(within(questions).getByRole('button', { name: 'Add a question' }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    expect(within(menu).queryByRole('menuitem', { name: 'File' })).toBeNull();
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Short text' }));
    // Each new question's id is unique across the workflow, the apply form's questions included.
    await fireEvent.click(within(questions).getByRole('button', { name: 'Duplicate New question' }));
    expect(within(questions).getAllByRole('button', { name: /^Edit question \d: New question Short text$/ })).toHaveLength(2);

    // The follow-up's page asks only its questions: no details and no resume.
    await fireEvent.click(within(pane).getByRole('button', { name: 'Preview' }));
    expect(within(pane).queryByRole('region', { name: 'Questions' })).toBeNull();
    expect(within(pane).queryByRole('textbox', { name: /First name/ })).toBeNull();
    expect(within(pane).getAllByRole('textbox', { name: /^New question/ })).toHaveLength(3);
    await fireEvent.click(within(pane).getByRole('button', { name: 'Edit' }));

    await fireEvent.click(within(pane).getByRole('button', { name: 'Remove questionnaire' }));
    expect(within(choices).queryByRole('button', { name: 'Availability' })).toBeNull();
    expect(within(choices).getByRole('button', { name: 'Apply form' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('offers Send a questionnaire and the questionnaire trigger only with a follow-up to name', async () => {
    const followUp: HiringWorkflow = {
      ...workflow,
      followUpQuestionnaires: [{ id: 'availability', name: 'Availability', questions: [{ id: 'start', type: 'ShortText', label: 'When could you start?' }] }],
    };
    const view = render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: structuredClone(workflow), errors: [], mode: 'editor' } } });
    await fireEvent.click(screen.getByRole('button', { name: 'Edit automations for Phone screen' }));
    let sheet = await screen.findByRole('dialog', { name: 'Automations: Phone screen' });
    expect(within(sheet).queryByRole('option', { name: 'Send a questionnaire' })).toBeNull();
    view.unmount();

    render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: followUp, errors: [], mode: 'editor' } } });
    await fireEvent.click(screen.getByRole('button', { name: 'Edit automations for Phone screen' }));
    sheet = await screen.findByRole('dialog', { name: 'Automations: Phone screen' });
    const action = within(sheet).getByRole('combobox', { name: 'Action 1' });
    await fireEvent.change(action, { target: { value: 'SendQuestionnaire' } });
    expect((within(sheet).getByRole('combobox', { name: 'Questionnaire' }) as HTMLSelectElement).value).toBe('availability');

    const trigger = within(sheet).getByRole('combobox', { name: 'Trigger' });
    await fireEvent.change(trigger, { target: { value: 'QuestionnaireCompleted' } });
    const pickers = within(sheet).getAllByRole('combobox', { name: 'Questionnaire' });
    expect(within(pickers[0]!).getByRole('option', { name: 'Any follow-up questionnaire' })).toBeInTheDocument();
    expect((pickers[0] as HTMLSelectElement).value).toBe('availability');
  });

  // A passed time limit measures the team's delay, so the form offers only reminders for it (workflow-engine.md section 8).
  it("offers a stage's time limit as a trigger, on stages that have one, with only the reminder actions", async () => {
    renderViews('editor');
    await fireEvent.click(within(screen.getByRole('region', { name: 'Stages' })).getByRole('button', { name: 'Edit automations for Phone screen' }));
    const sheet = await screen.findByRole('dialog', { name: 'Automations: Phone screen' });
    const options = (select: HTMLElement) => within(select).getAllByRole('option').map((option) => option.textContent?.trim());

    expect(options(within(sheet).getByRole('combobox', { name: 'Action 1' }))).toContain('Queue for rejection review');
    await fireEvent.change(within(sheet).getByRole('combobox', { name: 'Trigger' }), { target: { value: 'SlaBreached' } });

    const stage = within(sheet).getByRole('combobox', { name: 'Stage' }) as HTMLSelectElement;
    expect(options(stage)).toEqual(['Any stage with a time limit', 'Phone screen']);
    expect(stage.value).toBe('phone-screen');
    expect(options(within(sheet).getByRole('combobox', { name: 'Action 1' }))).toEqual(['Add a tag', 'Assign a reviewer', 'Notify the team', 'Create a task']);
  });

  it('gives a review stage a scorecard from the library in a sheet, and an end stage none', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/scorecard-templates': () =>
            Response.json([
              { id: 'phone-screen', name: 'Phone screen', builtIn: true, questions: 2, ratingQuestions: 2, lastUpdated: { at: null, by: 'ScreenHire' } },
              { id: '01a157d4-0000-7000-8000-0000000005c1', name: 'Technical interview', builtIn: false, questions: 1, ratingQuestions: 1, lastUpdated: { at: null, by: null } },
            ]),
          'GET /v1/scorecard-templates/phone-screen': () =>
            Response.json({
              id: 'phone-screen',
              name: 'Phone screen',
              builtIn: true,
              copiedFrom: null,
              questions: [
                { id: 'role_requirements', type: 'Rating5', label: "Meets the role's requirements", required: false },
                { id: 'notes', type: 'LongText', label: 'Notes', required: false },
              ],
              lastUpdated: { at: null, by: 'ScreenHire' },
            }),
        }),
      ),
    );
    const { container } = renderViews('editor');
    const stages = screen.getByRole('region', { name: 'Stages' });

    expect(within(stages).queryByRole('button', { name: 'Choose scorecard for Hired' })).toBeNull();
    await fireEvent.click(within(stages).getByRole('button', { name: 'Choose scorecard for Phone screen' }));
    const sheet = await screen.findByRole('dialog', { name: 'Scorecard: Phone screen' });
    const picker = within(sheet).getByRole('combobox', { name: 'Scorecard' });
    await within(sheet).findByRole('option', { name: 'Technical interview' });
    expect(sheet).toHaveTextContent('No scorecard: reviewers record no decision in Phone screen.');

    await fireEvent.change(picker, { target: { value: 'phone-screen' } });
    const questions = await within(sheet).findByRole('list', { name: 'Questions' });
    expect(within(questions).getAllByRole('listitem').map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      "Meets the role's requirements Rating, 1 to 5",
      'Notes Long text',
    ]);
    expect(stages).toHaveTextContent('Scorecard: Phone screen');
    await expectNoAxeViolations(container);
    vi.unstubAllGlobals();
  });

  it('says so, with a retry, when the scorecard library does not load', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/scorecard-templates': () => new Response(null, { status: 500 }) })));
    renderViews('editor');
    await fireEvent.click(within(screen.getByRole('region', { name: 'Stages' })).getByRole('button', { name: 'Choose scorecard for Phone screen' }));
    const sheet = await screen.findByRole('dialog', { name: 'Scorecard: Phone screen' });

    expect(await within(sheet).findByRole('alert')).toHaveTextContent('Could not load the scorecard library');
    expect(within(sheet).getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    vi.unstubAllGlobals();
  });
});

describe('workflow graph in the builder', () => {
  afterEach(() => vi.unstubAllGlobals());

  const graph = () => screen.getByRole('tabpanel', { name: 'Graph' });

  it('adds an automation to a stage and opens its form at its name, the one the Pipeline view uses', async () => {
    const { container } = renderViews('editor', 'graph');
    await expectNoAxeViolations(container);

    await fireEvent.click(within(graph()).getByRole('button', { name: 'Add automation to Phone screen' }));

    const sheet = await screen.findByRole('dialog', { name: 'Automations: Phone screen' });
    const name = within(sheet).getByRole('textbox', { name: 'Name' }) as HTMLInputElement;
    expect(name.value).toBe('New automation');
    await vi.waitFor(() => expect(name).toHaveFocus());
    expect(within(sheet).getAllByRole('article')).toHaveLength(1);
    expect((within(sheet).getByRole('combobox', { name: 'Trigger' }) as HTMLSelectElement).value).toBe('StageEntered');
    expect(within(graph()).getByRole('group', { name: 'When: New automation' })).toBeInTheDocument();
  });

  it("opens an action's form at that action from its node's Edit", async () => {
    renderViews('editor', 'graph');

    await fireEvent.click(within(graph()).getByRole('button', { name: 'Edit Add the tag "screened" in Tag screened' }));

    const sheet = await screen.findByRole('dialog', { name: 'Automations: Phone screen' });
    await vi.waitFor(() => expect(within(sheet).getByRole('combobox', { name: 'Action 1' })).toHaveFocus());
  });

  it('shows a validation problem on its node, as its form control does', () => {
    renderViews('editor', 'graph', [{ path: 'automations[0].actions[0].tag', message: 'Give the tag a name.' }]);

    const node = within(graph()).getByRole('group', { name: 'Then, action 1: Add the tag "screened", Tag screened, 1 problem to fix' });
    expect(node).toHaveTextContent('Give the tag a name.');
    expect(node.querySelector('[data-problem-icon]')).not.toBeNull();
  });

  it('removes an automation from its trigger node and moves the focus to its stage', async () => {
    renderViews('editor', 'graph');

    await fireEvent.click(within(graph()).getByRole('button', { name: 'Remove Tag screened' }));

    expect(within(graph()).queryByRole('group', { name: /Tag screened/ })).toBeNull();
    await vi.waitFor(() => expect(within(graph()).getByRole('group', { name: 'Stage 2: Phone screen' })).toHaveFocus());
  });

  it('moves between nodes in reading order with the arrow keys', async () => {
    renderViews('editor', 'graph');
    const lane = within(graph()).getByRole('group', { name: 'Whole workflow' });
    lane.focus();

    await fireEvent.keyDown(lane, { key: 'ArrowRight' });
    expect(within(graph()).getByRole('group', { name: 'Stage 1: Applied' })).toHaveFocus();
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    expect(within(graph()).getByRole('group', { name: 'When: Tag screened' })).toHaveFocus();
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowRight' });
    expect(within(graph()).getByRole('group', { name: 'Then, action 1: Add the tag "screened", Tag screened' })).toHaveFocus();
    await fireEvent.keyDown(document.activeElement!, { key: 'Home' });
    expect(lane).toHaveFocus();
  });

  it('is for reading on a phone, where editing stays in the Pipeline view', () => {
    stubViewport(390);
    renderViews('editor', 'graph');

    expect(graph()).toHaveTextContent('On a phone the graph is for reading. Edit in Pipeline.');
    expect(within(graph()).queryByRole('button', { name: /^(Edit|Add automation|Remove) / })).toBeNull();
    expect(within(graph()).getByRole('group', { name: 'When: Tag screened' })).toBeInTheDocument();
  });
});
