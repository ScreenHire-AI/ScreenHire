// A workflow as the builder shows it and as the read-only view reads it (design system section 7), and the validation
// result beside its title.
import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub } from '../../../test-support/fixtures.js';
import { inHiddenPane } from '../../../test-support/panes.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import { stubViewport } from '../../../test-support/viewport.js';
import type { WorkflowTab } from '#lib/workflow-tabs.js';
import GuidedTestHarness from '../../../test-support/guided-test-harness.svelte';
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
  automations: [
    { id: 'tag-screen', name: 'Tag screened', trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'AddTag', tag: 'screened' }] },
    { id: 'notify', name: 'Tell the team', enabled: false, trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'NotifyTeam', note: 'New' }] },
  ],
};

const renderViews = (mode: 'editor' | 'summary', tab: WorkflowTab = 'pipeline', errors: DefinitionError[] = [], flow: HiringWorkflow = workflow) =>
  render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: structuredClone(flow), errors, mode, tab } } });

const controls = (root: Element) => root.querySelectorAll('input, select, textarea');

// A stage's Add an automation, Choose scorecard and Remove stage are its card's menu, not buttons on every card.
async function chooseStageAction(stage: string, action: string) {
  await fireEvent.pointerDown(within(screen.getByRole('region', { name: 'Stages' })).getByRole('button', { name: `Actions for ${stage}` }), {
    button: 0,
    pointerType: 'mouse',
  });
  await fireEvent.click(await screen.findByRole('menuitem', { name: action }));
}

// A stage's Add an automation opens the recipes; Write your own opens an empty automation as a sentence, and Edit as rules its
// full form, which keeps the selects the form tests below read.
async function writeOwn(stage: string): Promise<HTMLElement> {
  await chooseStageAction(stage, 'Add an automation');
  const recipes = await screen.findByRole('dialog', { name: 'Add an automation' });
  await fireEvent.click(within(recipes).getByRole('button', { name: 'Write your own' }));
  return screen.findByRole('dialog', { name: 'New automation' });
}

async function writeOwnAsRules(stage: string): Promise<HTMLElement> {
  const sheet = await writeOwn(stage);
  await fireEvent.click(within(sheet).getByRole('button', { name: 'Edit as rules' }));
  await within(sheet).findByRole('combobox', { name: 'Trigger' });
  return sheet;
}

describe('validation badge', () => {
  it('is busy while a check is on its way', () => {
    render(ValidationBadge, { props: { errors: [], checking: true } });

    const region = screen.getByRole('region', { name: 'Validation' });
    expect(region).toHaveAttribute('aria-busy', 'true');
  });

  it('says Needs fixes as a status badge, with a Fix button beside it that counts the things', () => {
    render(ValidationBadge, { props: { errors: [{ path: 'stages', message: 'A workflow needs a hired stage.' }], checking: false } });

    const region = screen.getByRole('region', { name: 'Validation' });
    expect(region).toHaveAttribute('aria-busy', 'false');
    expect(region).toHaveTextContent('Needs fixes');
    expect(region.querySelector('[data-status="verify"]')).not.toBeNull();
    expect(screen.getByRole('button', { name: 'Fix 1 thing' })).toBeInTheDocument();
  });

  it('counts two problems as two things', () => {
    render(ValidationBadge, {
      props: {
        errors: [
          { path: 'stages', message: 'A workflow needs a hired stage.' },
          { path: 'stages[1].name', message: 'Give the stage a name.' },
        ],
      },
    });

    expect(screen.getByRole('button', { name: 'Fix 2 things' })).toBeInTheDocument();
  });

  it('has no Fix button when the workflow cannot be edited here, or when the page shows its own notice', () => {
    const errors = [{ path: 'stages', message: 'A workflow needs a hired stage.' }];
    const readOnly = render(ValidationBadge, { props: { errors, editable: false } });

    expect(screen.getByRole('region', { name: 'Validation' })).toHaveTextContent('Needs fixes');
    expect(screen.queryByRole('button')).toBeNull();
    readOnly.unmount();

    render(ValidationBadge, { props: { errors, fix: false } });
    expect(screen.getByRole('region', { name: 'Validation' })).toHaveTextContent('Needs fixes');
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('shows nothing at all when there is nothing to fix', () => {
    render(ValidationBadge, { props: { errors: [] } });

    const region = screen.getByRole('region', { name: 'Validation' });
    expect(region).toBeEmptyDOMElement();
    expect(screen.queryByRole('button')).toBeNull();
    expect(document.body.textContent?.trim()).toBe('');
  });
});

describe('read-only workflow', () => {
  it('reads the pipeline as content: stages in a flow, end stages in the footer, automations as rows', async () => {
    const { container } = renderViews('summary');

    const pane = screen.getByRole('tabpanel', { name: 'Pipeline' });
    expect(controls(pane)).toHaveLength(0);

    const stages = within(pane).getByRole('region', { name: 'Stages' });
    const line = within(stages).getByRole('list', { name: 'Stages in order' });
    expect(Array.from(line.children).map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      '1 Applied',
      '2 Phone screen 3-day limit 1 automation',
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
    const { container } = render(WithQueryClient, {
      props: { component: WorkflowHarness, props: { workflow: structuredClone(workflow), errors: [], mode: 'summary', tab: 'questions', aiUse: { policy: 'AllowedWithDisclosure' } } },
    });

    const pane = screen.getByRole('tabpanel', { name: 'Questions' });
    expect(pane.querySelector('[data-ai-use-row]')).toHaveTextContent('AI use. Asked by ScreenHire. Policy: Allowed with disclosure.');
    expect(within(pane).queryByRole('link', { name: 'Change policy' })).toBeNull();
    const questions = within(pane).getByRole('region', { name: 'Screening questions' });
    expect(controls(questions)).toHaveLength(0);
    expect(questions).toHaveTextContent('Do you have the right to work here?');
    expect(within(questions).getByText('Knockout if no')).toHaveAttribute('data-slot', 'badge');
    expect(within(questions).getByText('Required')).toHaveAttribute('data-slot', 'badge');
    await expectNoAxeViolations(container);

    // The candidate's form never shares the screen with the content: the switch swaps them. Nothing here can be edited, so
    // its first choice names what it shows instead of offering Edit (E3).
    expect(within(pane).getByRole('button', { name: 'Questions' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(pane).queryByRole('button', { name: 'Edit' })).toBeNull();
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
    render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: followUp, errors: [], mode: 'summary', tab: 'questions' } } });

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
    renderViews('summary', 'automations');

    const pane = screen.getByRole('tabpanel', { name: 'Automations' });
    expect(within(pane).getByRole('region', { name: 'Stages and automations' })).toBeInTheDocument();
    expect(within(pane).getByRole('group', { name: 'When: Tag screened' })).toHaveAttribute('tabindex', '0');
    expect(within(pane).queryByRole('button', { name: /^(Add|Edit|Remove) / })).toBeNull();
  });
});

describe('workflow builder', () => {
  it('lists the problems to fix in a panel, and none when there are none', () => {
    renderViews('editor', 'pipeline', [{ path: 'stages[1].name', message: 'Give the stage a name.' }]);

    const problems = screen.getByRole('region', { name: 'Problems to fix' });
    // A message names its own thing, so nothing goes before it.
    expect(within(problems).getAllByRole('listitem')[0]!.textContent).toMatch(/^Give the stage a name\./);
    expect(problems).not.toHaveTextContent('Phone screen: ');
  });

  it('takes Fix to where the problem is: a stage in its sheet, an automation in its form, a question on Questions', async () => {
    renderViews('editor', 'automations', [
      { path: 'stages[1].name', message: 'Give the stage a name.' },
      { path: 'automations[0].name', message: 'This is blank. Fill it in or remove it.' },
      { path: 'questionnaire.questions[0].label', message: 'Write the question.' },
    ]);
    const problems = screen.getByRole('region', { name: 'Problems to fix' });

    await fireEvent.click(within(problems).getByRole('button', { name: 'Fix: The stage "Phone screen"' }));
    const stageSheet = await screen.findByRole('dialog', { name: 'Phone screen' });
    expect(within(stageSheet).getByRole('textbox', { name: 'Name' })).toHaveAttribute('aria-invalid', 'true');
    await fireEvent.click(within(stageSheet).getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Phone screen' })).toBeNull());

    await fireEvent.click(within(problems).getByRole('button', { name: /^Fix: The automation / }));
    const sheet = await screen.findByRole('dialog', { name: 'Tag screened' });
    await waitFor(() => expect(sheet).toContainElement(document.activeElement as HTMLElement));
    expect(within(sheet).getByRole('textbox', { name: 'Name' })).toHaveAttribute('aria-invalid', 'true');
    await fireEvent.keyDown(sheet, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Tag screened' })).toBeNull());

    await fireEvent.click(within(problems).getByRole('button', { name: 'Fix: The question "Do you have the right to work here?"' }));
    await waitFor(() => expect(screen.getByRole('tab', { name: 'Questions' })).toHaveAttribute('aria-selected', 'true'));
  });

  it('puts a Fix button beside the Needs fixes badge, which stays a badge, and takes the focus to the first Fix', async () => {
    renderViews('editor', 'pipeline', [{ path: 'stages[1].name', message: 'Give the stage a name.' }]);
    render(ValidationBadge, { props: { errors: [{ path: 'stages[1].name', message: 'Give the stage a name.' }] } });

    // Badges are never clickable (principle 4).
    expect(screen.queryByRole('button', { name: 'Needs fixes' })).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'Fix 1 thing' }));

    expect(document.activeElement).toHaveAccessibleName('Fix: The stage "Phone screen"');
  });

  it('has no problems panel when the workflow is valid, and none on the Test tab, which shows its own notice', () => {
    const valid = renderViews('editor');

    expect(screen.queryByRole('region', { name: 'Problems to fix' })).toBeNull();
    valid.unmount();

    renderViews('editor', 'test', [{ path: 'stages[1].name', message: 'Give the stage a name.' }]);
    expect(screen.queryByRole('region', { name: 'Problems to fix' })).toBeNull();
  });

  it('edits stages as cards that reorder with a grip that says left and right, leaving out moves past either end (A7)', async () => {
    const { container } = renderViews('editor');

    const stages = screen.getByRole('region', { name: 'Stages' });
    const cards = () => within(stages).getAllByRole('group', { name: /^Stage / }).map((card) => card.getAttribute('aria-label'));
    expect(cards()).toEqual(['Stage 1: Applied', 'Stage 2: Phone screen', 'Stage 3: Hired', 'Stage 4: Not progressing']);
    // The time limit is in the stage's sheet, not on the card, and a card is no panel inside the panel (S4).
    expect(within(stages).queryByRole('spinbutton')).toBeNull();
    expect(within(stages).getByRole('group', { name: 'Stage 2: Phone screen' })).toHaveTextContent('3-day limit');
    expect(within(stages).getByRole('group', { name: 'Stage 2: Phone screen' })).toHaveTextContent('1 automation');
    expect(stages.querySelectorAll('[data-slot="panel"]')).toHaveLength(0);
    // The old panel of automations is gone: they are counted on their stage and listed under Automations.
    expect(screen.queryByRole('region', { name: 'Workflow automations' })).toBeNull();

    await fireEvent.click(within(stages).getByRole('button', { name: 'Reorder Applied' }));
    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim())).toEqual(['Move right', 'Move to end']);
    await fireEvent.click(screen.getByRole('menuitem', { name: 'Move right' }));
    expect(cards()).toEqual(['Stage 1: Phone screen', 'Stage 2: Applied', 'Stage 3: Hired', 'Stage 4: Not progressing']);

    await fireEvent.click(within(stages).getByRole('button', { name: 'Reorder Not progressing' }));
    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim())).toEqual(['Move left', 'Move to start']);
    await fireEvent.click(screen.getByRole('menuitem', { name: 'Move to start' }));
    expect(cards()).toEqual(['Stage 1: Not progressing', 'Stage 2: Phone screen', 'Stage 3: Applied', 'Stage 4: Hired']);

    await expectNoAxeViolations(container);
  });

  it('lays the forms out by when the candidate gets them, and asks more later with the email automation it adds', async () => {
    const { container } = render(WithQueryClient, {
      props: {
        component: WorkflowHarness,
        props: { workflow: structuredClone(workflow), errors: [], mode: 'editor', tab: 'questions', aiUse: { policy: 'NotAllowed', href: '/verification/policy' } },
      },
    });
    const pane = screen.getByRole('tabpanel', { name: 'Questions' });
    const forms = within(pane).getByRole('group', { name: 'Forms' });
    const apply = within(forms).getByRole('button', { name: /Apply form/ });
    expect(apply).toHaveAttribute('aria-pressed', 'true');
    expect(apply).toHaveTextContent('When they apply');
    expect(within(pane).getByRole('region', { name: 'Questions' })).toBeInTheDocument();

    // The AI-use question is locked: ScreenHire asks it, and the policy is the organization's.
    expect(pane.querySelector('[data-ai-use-row]')).toHaveTextContent('AI use. Asked by ScreenHire. Policy: Not allowed.');
    expect(within(pane).getByRole('link', { name: 'Change policy' })).toHaveAttribute('href', '/verification/policy');
    expect(within(pane).queryByRole('combobox', { name: 'AI use while applying' })).toBeNull();

    // Ask more later asks when, then makes the form and the automation that emails it.
    await fireEvent.click(within(pane).getByRole('button', { name: 'Ask more later' }));
    const ask = within(pane).getByRole('form', { name: 'Ask more later' });
    expect(ask).toHaveTextContent('We add the email automation for you.');
    await fireEvent.change(within(ask).getByLabelText('When should it go?'), { target: { value: 'after' } });
    await fireEvent.input(within(ask).getByLabelText('Days'), { target: { value: '2' } });
    await fireEvent.click(within(ask).getByRole('button', { name: 'Add form' }));

    const later = within(forms).getByRole('button', { name: /Phone screen questions after 2 days/ });
    expect(later).toHaveAttribute('aria-pressed', 'true');
    expect(later).toHaveTextContent('2 days after Phone screen starts');
    expect(within(pane).queryByRole('form', { name: 'Ask more later' })).toBeNull();
    expect(pane.querySelector('[data-ai-use-row]')).toBeNull();
    const name = within(pane).getByRole('textbox', { name: /^Name/ });
    await fireEvent.input(name, { target: { value: 'Availability' } });
    expect(within(forms).getByRole('button', { name: /Availability/ })).toHaveTextContent('2 days after Phone screen starts');
    const questions = within(pane).getByRole('region', { name: 'Questions' });
    expect(within(questions).getByRole('button', { name: /^Edit question 1: New question / })).toBeInTheDocument();
    await expectNoAxeViolations(container);

    // A follow-up's page takes no uploads, so its answer menu has no File.
    await fireEvent.pointerDown(within(questions).getByRole('button', { name: 'Add a question' }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    expect(within(menu).queryByRole('menuitem', { name: 'File' })).toBeNull();
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Short text' }));
    // Duplicate and Delete are in the row's menu, and each new question's id is unique across the workflow.
    await fireEvent.pointerDown(within(questions).getAllByRole('button', { name: 'Actions for New question' })[0]!, { button: 0, pointerType: 'mouse' });
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Duplicate' }));
    expect(within(questions).getAllByRole('button', { name: /^Edit question \d: New question Short text$/ }).length).toBeGreaterThanOrEqual(2);

    // The follow-up's page asks only its questions: no details and no resume, and no AI-use question.
    await fireEvent.click(within(pane).getByRole('button', { name: 'Preview' }));
    expect(inHiddenPane(within(pane).getByRole('region', { name: 'Questions' }))).toBe(true);
    const shown = within(pane.querySelector<HTMLElement>('[data-preview-pane]')!);
    expect(shown.queryByRole('textbox', { name: /First name/ })).toBeNull();
    expect(shown.queryByRole('group', { name: /Did you use AI tools/ })).toBeNull();
    await fireEvent.click(within(pane).getByRole('button', { name: 'Edit' }));

    // The follow-up's name and its menu sit in their own panel, never on the page background (S3).
    const formPanel = within(pane).getByRole('region', { name: 'Form' });
    expect(within(formPanel).getByRole('textbox', { name: 'Name' })).toHaveValue('Availability');

    // Removing the form takes the automation that emailed it, and says so first.
    await fireEvent.pointerDown(within(formPanel).getByRole('button', { name: 'Actions for Availability' }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Remove form' }));
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('This also removes Email the form 2 days after Phone screen starts.');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove form' }));
    await waitFor(() => expect(within(forms).queryByRole('button', { name: /Availability/ })).toBeNull());
    expect(within(forms).getByRole('button', { name: /Apply form/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it('shows the apply form as the candidate sees it, ending in the AI-use question under the policy', async () => {
    render(WithQueryClient, {
      props: { component: WorkflowHarness, props: { workflow: structuredClone(workflow), errors: [], mode: 'editor', tab: 'questions', aiUse: { policy: 'Allowed' } } },
    });
    const pane = screen.getByRole('tabpanel', { name: 'Questions' });
    await fireEvent.click(within(pane).getByRole('button', { name: 'Preview' }));

    const shown = within(pane.querySelector<HTMLElement>('[data-preview-pane]')!);
    expect(shown.getByRole('group', { name: /Did you use AI tools for this application\? \(optional\)/ })).toBeInTheDocument();
  });

  it('offers Ask more later only for stages a form can follow, and a form no automation sends says so', () => {
    const unsent: HiringWorkflow = {
      ...structuredClone(workflow),
      stages: workflow.stages.filter((stage) => stage.kind === 'Applied' || stage.kind === 'Hired'),
      automations: [],
      followUpQuestionnaires: [{ id: 'references', name: 'References', questions: [{ id: 'who', type: 'ShortText', label: 'Who can we ask?' }] }],
    };
    render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: unsent, errors: [], mode: 'editor', tab: 'questions' } } });
    const pane = screen.getByRole('tabpanel', { name: 'Questions' });

    expect(within(pane).queryByRole('button', { name: 'Ask more later' })).toBeNull();
    expect(within(within(pane).getByRole('group', { name: 'Forms' })).getByRole('button', { name: /References/ })).toHaveTextContent('Not sent yet');
  });

  it('offers Send a questionnaire and the questionnaire trigger only with a follow-up to name', async () => {
    const followUp: HiringWorkflow = {
      ...workflow,
      followUpQuestionnaires: [{ id: 'availability', name: 'Availability', questions: [{ id: 'start', type: 'ShortText', label: 'When could you start?' }] }],
    };
    const view = render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: structuredClone(workflow), errors: [], mode: 'editor' } } });
    let sheet = await writeOwnAsRules('Phone screen');
    expect(within(sheet).queryByRole('option', { name: 'Send a questionnaire' })).toBeNull();
    view.unmount();

    render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: followUp, errors: [], mode: 'editor' } } });
    sheet = await writeOwnAsRules('Phone screen');
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
    const sheet = await writeOwnAsRules('Phone screen');
    await vi.waitFor(() => expect(sheet).toContainElement(document.activeElement as HTMLElement));
    const options = (select: HTMLElement) => within(select).getAllByRole('option').map((option) => option.textContent?.trim());

    expect(options(within(sheet).getByRole('combobox', { name: 'Action 1' }))).toContain('Queue for rejection review');
    await fireEvent.change(within(sheet).getByRole('combobox', { name: 'Trigger' }), { target: { value: 'SlaBreached' } });

    const stage = within(sheet).getByRole('combobox', { name: 'Stage' }) as HTMLSelectElement;
    expect(options(stage)).toEqual(['Any stage with a time limit', 'Phone screen']);
    expect(stage.value).toBe('phone-screen');
    expect(options(within(sheet).getByRole('combobox', { name: 'Action 1' }))).toEqual(['Add a tag', 'Assign a reviewer', 'Notify the team', 'Create a task']);
  });

  // F6: a message sits under the field it is about, and the action's own list keeps only what no field holds.
  it("shows an interview field's message under that field and the action's own message under the action", async () => {
    const withInterview = structuredClone(workflow);
    withInterview.automations![0]!.actions = [
      { type: 'ScheduleInterview', interviewerUserIds: [], interviewMinutes: 45, interviewWindowDays: 0, interviewTimeZone: 'Europe/London' },
    ];
    const errors: DefinitionError[] = [
      { path: 'automations[0].actions[0].interviewWindowDays', message: 'Give the candidate 1 to 60 days.' },
      { path: 'automations[0].actions[0]', message: 'Offering an interview is not allowed here.' },
    ];
    render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: withInterview, errors, mode: 'editor', tab: 'automations' } } });
    await fireEvent.click(within(screen.getByRole('tabpanel', { name: 'Automations' })).getByRole('button', { name: (name) => name.includes('Then action 1 in Tag screened') }));
    const sheet = await screen.findByRole('dialog', { name: 'Tag screened' });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Edit as rules' }));

    const days = within(sheet).getByRole('spinbutton', { name: 'Days to choose' });
    const described = (control: HTMLElement) => control.getAttribute('aria-describedby')?.split(' ').map((id) => document.getElementById(id)?.textContent?.trim());
    expect(days).toHaveAttribute('aria-invalid', 'true');
    expect(described(days)).toEqual(['Give the candidate 1 to 60 days.']);
    const action = within(sheet).getByRole('combobox', { name: 'Action 1' });
    expect(described(action)).toEqual(['Offering an interview is not allowed here.']);
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

    // A stage that ends the application has no scorecard, so its menu leaves the item out (A3).
    await fireEvent.pointerDown(within(stages).getByRole('button', { name: 'Actions for Hired' }), { button: 0, pointerType: 'mouse' });
    const hiredMenu = await screen.findByRole('menu');
    expect(within(hiredMenu).getByRole('menuitem', { name: 'Add an automation' })).toBeInTheDocument();
    expect(within(hiredMenu).queryByRole('menuitem', { name: 'Choose scorecard' })).toBeNull();
    await fireEvent.keyDown(hiredMenu, { key: 'Escape' });
    await chooseStageAction('Phone screen', 'Choose scorecard');
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
    // The stage's scorecard is its name beside an icon, with the word for screen readers only (W9).
    expect(stages).toHaveTextContent('Scorecard: Phone screen');
    await expectNoAxeViolations(container);
    vi.unstubAllGlobals();
  });

  // An action repeated on every card belongs in the card's menu (L2): one More button per stage holds all four.
  it("holds a stage's Edit stage, Add an automation, Choose scorecard and Remove stage in its card menu, and removes the stage without a confirm", async () => {
    renderViews('editor');
    const stages = screen.getByRole('region', { name: 'Stages' });
    expect(within(stages).queryByRole('button', { name: /^(Edit stage|Add an automation|Choose scorecard|Remove)/ })).toBeNull();

    await fireEvent.pointerDown(within(stages).getByRole('button', { name: 'Actions for Phone screen' }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Edit stage', 'Add an automation', 'Choose scorecard', 'Remove stage']);
    // jsdom has no Web Animations, and a card leaving the list's `animate:flip` asks for its running ones (as in form-builder.test.ts).
    Element.prototype.getAnimations = () => [];
    try {
      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove stage' }));
      await vi.waitFor(() => expect(within(stages).queryByRole('group', { name: /Phone screen/ })).toBeNull());
      // The card and its menu left with the stage, so focus moves on to the next card's name rather than falling to the page.
      await vi.waitFor(() => expect(within(stages).getByRole('button', { name: 'Hired' })).toHaveFocus());

      await fireEvent.pointerDown(within(stages).getByRole('button', { name: 'Actions for Not progressing' }), { button: 0, pointerType: 'mouse' });
      await fireEvent.click(await screen.findByRole('menuitem', { name: 'Remove stage' }));
      // The last card went, so the last one left takes the focus.
      await vi.waitFor(() => expect(within(stages).getByRole('button', { name: 'Hired' })).toHaveFocus());
      expect(within(stages).queryByRole('group', { name: /Not progressing/ })).toBeNull();
    } finally {
      delete (Element.prototype as Partial<Element>).getAnimations;
    }
  });

  it('says so, with a retry, when the scorecard library does not load', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/scorecard-templates': () => new Response(null, { status: 500 }) })));
    renderViews('editor');
    await chooseStageAction('Phone screen', 'Choose scorecard');
    const sheet = await screen.findByRole('dialog', { name: 'Scorecard: Phone screen' });

    expect(await within(sheet).findByRole('alert')).toHaveTextContent('Could not load the scorecard library');
    expect(within(sheet).getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    vi.unstubAllGlobals();
  });
});

describe('workflow simulation', () => {
  afterEach(() => vi.unstubAllGlobals());

  // Applying runs a knockout automation and a verification route; entering a stage tags. Four moments, three questions each.
  const guided: HiringWorkflow = {
    ...workflow,
    questionnaire: {
      questions: [
        workflow.questionnaire.questions[0]!,
        {
          id: 'stack',
          type: 'MultiChoice',
          label: 'Which of these have you used?',
          required: false,
          options: [
            { id: 'csharp', label: 'C#' },
            { id: 'svelte', label: 'Svelte' },
          ],
        },
      ],
    },
    automations: [
      ...workflow.automations,
      {
        id: 'queue-review',
        name: 'Queue for review',
        trigger: { type: 'ApplicationSubmitted' },
        conditions: { match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }] },
        actions: [{ type: 'QueueForRejectionReview', reason: 'No right to work' }],
      },
      {
        id: 'svelte-fans',
        name: 'Tag Svelte users',
        trigger: { type: 'ApplicationSubmitted' },
        conditions: { match: 'All', rules: [{ fact: 'answers.stack', operator: 'Contains', value: 'svelte' }] },
        actions: [{ type: 'AddTag', tag: 'svelte' }],
      },
      {
        id: 'route',
        name: 'Route for review',
        trigger: { type: 'VerificationAssessed' },
        conditions: { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'EqualTo', value: 'VerificationRecommended' }] },
        actions: [{ type: 'AddTag', tag: 'needs-review' }],
      },
    ],
  };

  const stubSimulate = (answer: (body: Record<string, unknown>) => Response = () => Response.json({ automations: [] })) => {
    const fetchMock = vi.fn(
      apiStub({
        'GET /v1/email-templates': () => Response.json({ templates: [], sendingEnabled: false }),
        'POST /v1/workflows/simulate': (init) => answer(JSON.parse(String(init?.body))),
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
    return fetchMock;
  };
  const sent = (fetchMock: ReturnType<typeof stubSimulate>) =>
    fetchMock.mock.calls.filter(([url]) => url === '/v1/workflows/simulate').map(([, init]) => JSON.parse(String(init?.body)));
  const renderGuided = (flow: HiringWorkflow, props: Record<string, unknown> = {}) =>
    render(WithQueryClient, { props: { component: GuidedTestHarness, props: { workflow: structuredClone(flow), ...props } } });
  const stepHeading = () => screen.getByRole('heading', { level: 3 });
  const pane = () => screen.getByRole('region', { name: 'Test this workflow' });

  it('asks one thing at a time, says once that no real candidate is touched, and runs a ready-made candidate with one press', async () => {
    const fetchMock = stubSimulate((body) =>
      Response.json({
        automations: [{ automationId: 'queue-review', name: 'Queue for review', outcome: 'NotMatched', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', held: false }], actions: [], reason: null }],
      }),
    );
    const { container } = renderGuided(guided);

    expect(stepHeading()).toHaveTextContent('What happened?');
    expect(pane()).toHaveTextContent('It never touches a real candidate or sends anything.');
    const progress = within(pane()).getByRole('list', { name: 'Progress of the test' });
    expect(within(progress).getByText('Moment').closest('li')).toHaveAttribute('aria-current', 'step');
    // Only the moments some automation that is on reacts to: the disabled "Tell the team" is not one of them.
    const moments = within(pane()).getByRole('list', { name: 'Moments' });
    expect(within(moments).getAllByRole('button').map((button) => button.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'A candidate applied 2 automations react',
      'The verification checks finished 1 automation reacts',
      'A candidate moved to a stage 1 automation reacts',
    ]);
    expect(within(pane()).queryAllByRole('form')).toHaveLength(0);
    await expectNoAxeViolations(container);

    await fireEvent.click(within(moments).getByRole('button', { name: /A candidate applied/ }));

    await waitFor(() => expect(stepHeading()).toHaveTextContent('Who is the candidate?'));
    await waitFor(() => expect(stepHeading()).toHaveFocus());
    expect(pane()).not.toHaveTextContent('never touches a real candidate');
    const candidates = within(pane()).getByRole('list', { name: 'Candidates' });
    expect(within(candidates).getAllByRole('button').map((button) => button.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Meets every requirement Passes every knockout question',
      'Fails a knockout Answers No to "Do you have the right to work here?"',
    ]);
    expect(within(pane()).getByRole('button', { name: 'Back' })).toBeInTheDocument();
    await fireEvent.click(within(candidates).getByRole('button', { name: /Fails a knockout/ }));

    await waitFor(() => expect(stepHeading()).toHaveTextContent('What would happen'));
    expect(sent(fetchMock)).toHaveLength(1);
    expect(sent(fetchMock)[0]).toMatchObject({
      trigger: { type: 'ApplicationSubmitted' },
      facts: { answers: [{ questionId: 'right_to_work', values: ['no'] }] },
      accommodationRequested: false,
    });
    // Nothing ran, so there is no numbered line; the automations of the moment are listed with one line on why not, and the one for another moment is not.
    await within(pane()).findByRole('button', { name: 'Change the candidate' });
    expect(within(pane()).queryByRole('list', { name: 'What would happen, in order' })).toBeNull();
    expect(pane()).toHaveTextContent('Nothing would run.');
    const skipped = within(pane()).getByRole('heading', { level: 4, name: 'Would not run' }).parentElement!;
    expect(skipped).toHaveTextContent('Queue for review');
    expect(skipped).toHaveTextContent('It needs "Do you have the right to work here? is No", and that did not hold.');
    expect(skipped).not.toHaveTextContent('Route for review');
    expect(within(pane()).getByRole('button', { name: 'Try another moment' })).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it('shows what would happen in order, then why the others did not run, in words and never as a verdict on the candidate', async () => {
    stubSimulate(() =>
      Response.json({
        automations: [
          { automationId: 'route', name: 'Route for review', outcome: 'Matched', rules: [{ fact: 'verification.authenticity', operator: 'EqualTo', held: true }], actions: [{ type: 'AddTag', tag: 'needs-review' }], reason: null },
        ],
      }),
    );
    renderGuided(guided);

    await fireEvent.click(within(pane()).getByRole('button', { name: /The verification checks finished/ }));
    await waitFor(() => expect(stepHeading()).toHaveTextContent('Who is the candidate?'));
    // The level is offered in words, as the verification screens name it.
    expect(within(pane()).getByRole('button', { name: /Has verification signals/ })).toHaveTextContent('Verify');
    await fireEvent.click(within(pane()).getByRole('button', { name: /Has verification signals/ }));

    const timeline = await within(pane()).findByRole('list', { name: 'What would happen, in order' });
    expect(timeline).toHaveTextContent('Route for review');
    expect(timeline).toHaveTextContent('Add the tag "needs-review"');
    // A level is a word, never a number, and nothing in the result rejects.
    expect(pane().textContent).not.toMatch(/\d+\s?%|score/i);
    expect(within(pane()).queryByRole('button', { name: /reject/i })).toBeNull();
  });

  it('opens only what the automations read, picks a multi-choice answer by the options names and sends their ids (W8)', async () => {
    const fetchMock = stubSimulate();
    renderGuided(guided);

    await fireEvent.click(within(pane()).getByRole('button', { name: /A candidate applied/ }));
    await waitFor(() => expect(stepHeading()).toHaveTextContent('Who is the candidate?'));
    await fireEvent.click(within(pane()).getByRole('button', { name: 'Customize' }));

    await waitFor(() => expect(stepHeading()).toHaveTextContent('Describe the candidate'));
    const form = within(pane()).getByRole('form', { name: 'Describe the candidate' });
    // The two questions the automations of this moment read, and nothing else: no tags, source, hours or levels.
    expect(within(form).getByLabelText('Do you have the right to work here?')).toBeInTheDocument();
    const group = within(form).getByRole('group', { name: 'Which of these have you used?' });
    for (const unread of [/tags/i, /source/i, /hours/i, /authenticity/i, /AI assistance/i, /accommodation/i]) expect(within(form).queryByText(unread)).toBeNull();
    expect(screen.queryByText(/ids/i)).toBeNull();
    await fireEvent.click(within(group).getByRole('checkbox', { name: 'Svelte' }));
    await fireEvent.click(within(group).getByRole('checkbox', { name: 'C#' }));
    await fireEvent.click(within(group).getByRole('checkbox', { name: 'Svelte' }));
    await fireEvent.change(within(form).getByLabelText('Do you have the right to work here?'), { target: { value: 'yes' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Run the test' }));

    await waitFor(() => expect(sent(fetchMock)).toHaveLength(1));
    expect(sent(fetchMock)[0].facts.answers).toEqual([
      { questionId: 'right_to_work', values: ['yes'] },
      { questionId: 'stack', values: ['csharp'] },
    ]);
  });

  it('goes back a step with what was chosen kept, and offers no customizing where nothing is read', async () => {
    stubSimulate();
    renderGuided(guided);

    await fireEvent.click(within(pane()).getByRole('button', { name: /A candidate moved to a stage/ }));
    await waitFor(() => expect(stepHeading()).toHaveTextContent('Who is the candidate?'));
    expect(within(pane()).queryByRole('button', { name: 'Customize' })).toBeNull();
    await fireEvent.click(within(pane()).getByRole('button', { name: 'Back' }));

    await waitFor(() => expect(stepHeading()).toHaveTextContent('What happened?'));
    expect(within(pane()).getByRole('button', { name: /A candidate moved to a stage/ })).toHaveAttribute('aria-current', 'true');
  });

  it('goes back through the second question, one screen at a time, with the stage chosen kept', async () => {
    stubSimulate();
    renderGuided({
      ...workflow,
      automations: [
        ...workflow.automations,
        { id: 'tag-applied', name: 'Tag applied', trigger: { type: 'StageEntered', stageId: 'applied' }, conditions: null, actions: [{ type: 'AddTag', tag: 'applied' }] },
      ],
    });

    await fireEvent.click(within(pane()).getByRole('button', { name: /A candidate moved to a stage/ }));
    await waitFor(() => expect(stepHeading()).toHaveTextContent('Which stage?'));
    await fireEvent.click(within(pane()).getByRole('button', { name: 'Back' }));
    await waitFor(() => expect(stepHeading()).toHaveTextContent('What happened?'));

    await fireEvent.click(within(pane()).getByRole('button', { name: /A candidate moved to a stage/ }));
    await waitFor(() => expect(stepHeading()).toHaveTextContent('Which stage?'));
    await fireEvent.click(within(pane()).getByRole('button', { name: 'Phone screen' }));
    await waitFor(() => expect(stepHeading()).toHaveTextContent('Who is the candidate?'));
    await fireEvent.click(within(pane()).getByRole('button', { name: 'Back' }));
    await waitFor(() => expect(stepHeading()).toHaveTextContent('Which stage?'));
    expect(within(pane()).getByRole('button', { name: 'Phone screen' })).toHaveAttribute('aria-current', 'true');
  });

  it('keeps the candidate for another moment, and returns to the question to change the candidate', async () => {
    const fetchMock = stubSimulate();
    renderGuided(guided);

    await fireEvent.click(within(pane()).getByRole('button', { name: /A candidate applied/ }));
    await fireEvent.click(await within(pane()).findByRole('button', { name: /Meets every requirement/ }));
    await within(pane()).findByRole('button', { name: 'Try another moment' });

    await fireEvent.click(within(pane()).getByRole('button', { name: 'Try another moment' }));
    await waitFor(() => expect(stepHeading()).toHaveTextContent('What happened?'));
    await fireEvent.click(within(pane()).getByRole('button', { name: /The verification checks finished/ }));

    // The same kind of candidate goes through the new moment without being asked for again, described by what that
    // moment's automations read: the knockout answer no longer matters, the authenticity level does.
    await waitFor(() => expect(sent(fetchMock)).toHaveLength(2));
    expect(sent(fetchMock)[1]).toMatchObject({ trigger: { type: 'VerificationAssessed' }, facts: { answers: [], verificationAuthenticity: 'NoConcerns' } });
    await waitFor(() => expect(stepHeading()).toHaveTextContent('What would happen'));
    await within(pane()).findByRole('button', { name: 'Change the candidate' });

    await fireEvent.click(within(pane()).getByRole('button', { name: 'Change the candidate' }));
    await waitFor(() => expect(stepHeading()).toHaveTextContent('Who is the candidate?'));
    expect(within(pane()).getByRole('button', { name: /Meets every requirement/ })).toHaveAttribute('aria-current', 'true');
  });

  it('asks which stage only when automations react in more than one', async () => {
    const fetchMock = stubSimulate();
    const staged: HiringWorkflow = {
      ...workflow,
      automations: [
        ...workflow.automations,
        { id: 'tag-applied', name: 'Tag applied', trigger: { type: 'StageEntered', stageId: 'applied' }, conditions: null, actions: [{ type: 'AddTag', tag: 'applied' }] },
      ],
    };
    renderGuided(staged);

    await fireEvent.click(within(pane()).getByRole('button', { name: /A candidate moved to a stage/ }));
    await waitFor(() => expect(stepHeading()).toHaveTextContent('Which stage?'));
    const stages = within(pane()).getByRole('list', { name: 'Stages' });
    expect(within(stages).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Applied', 'Phone screen']);
    await fireEvent.click(within(stages).getByRole('button', { name: 'Phone screen' }));
    await fireEvent.click(await within(pane()).findByRole('button', { name: /Meets every requirement/ }));

    await waitFor(() => expect(sent(fetchMock)).toHaveLength(1));
    expect(sent(fetchMock)[0].trigger).toEqual({ type: 'StageEntered', stageId: 'phone-screen' });
  });

  it('says there is nothing to test yet in a workflow with no automations, and offers adding one only where the page can', async () => {
    stubSimulate();
    const empty: HiringWorkflow = { ...workflow, automations: [] };
    const onadd = vi.fn();
    const editor = renderGuided(empty, { onadd });

    expect(pane()).toHaveTextContent('Nothing to test yet');
    expect(within(pane()).queryByRole('list', { name: 'Moments' })).toBeNull();
    await fireEvent.click(within(pane()).getByRole('button', { name: 'Add an automation' }));
    expect(onadd).toHaveBeenCalledTimes(1);
    editor.unmount();

    // A read-only version passes no way to add one.
    renderGuided(empty);
    expect(pane()).toHaveTextContent('Nothing to test yet');
    expect(within(pane()).queryByRole('button', { name: 'Add an automation' })).toBeNull();
  });

  it('says so when every automation is switched off, and offers to open Pipeline only where the page can', async () => {
    stubSimulate();
    const off: HiringWorkflow = { ...workflow, automations: workflow.automations.map((automation) => ({ ...automation, enabled: false })) };
    const onpipeline = vi.fn();
    const editor = renderGuided(off, { onpipeline });

    expect(pane()).toHaveTextContent('Nothing to test yet');
    expect(pane()).toHaveTextContent('Every automation is switched off.');
    await fireEvent.click(within(pane()).getByRole('button', { name: 'Open Pipeline' }));
    expect(onpipeline).toHaveBeenCalledTimes(1);
    editor.unmount();

    renderGuided(off);
    expect(pane()).toHaveTextContent('Every automation is switched off.');
    expect(within(pane()).queryByRole('button', { name: 'Open Pipeline' })).toBeNull();
  });

  it('names the problems in plain words when the workflow cannot be tested, and keeps the way back', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/email-templates': () => Response.json({ templates: [], sendingEnabled: false }),
          'POST /v1/workflows/simulate': () =>
            Response.json({ title: 'Invalid', status: 400, errors: { 'workflow.automations[0].actions[0]': ['An action needs a tag.'] } }, { status: 400 }),
        }),
      ),
    );
    renderGuided(guided);

    await fireEvent.click(within(pane()).getByRole('button', { name: /A candidate moved to a stage/ }));
    await fireEvent.click(await within(pane()).findByRole('button', { name: /Meets every requirement/ }));

    expect(await within(pane()).findByRole('alert')).toHaveTextContent('Fix the problems in this workflow first: it cannot be tested until it is valid.');
    // The message names its own thing, so no "where" comes before it.
    expect(pane()).toHaveTextContent('An action needs a tag.');
    expect(pane()).not.toHaveTextContent(/Automation "Tag screened"|The step "/);
    expect(within(pane()).getByRole('button', { name: 'Try another moment' })).toBeInTheDocument();
  });

  it('shows the Test tab of an empty workflow with Add an automation, which opens the recipes, and none where it only reads', async () => {
    stubSimulate();
    const empty: HiringWorkflow = { ...workflow, automations: [] };
    const editor = renderViews('editor', 'test', [], empty);

    const tab = screen.getByRole('tabpanel', { name: 'Test' });
    expect(tab).toHaveTextContent('Nothing to test yet');
    await fireEvent.click(within(tab).getByRole('button', { name: 'Add an automation' }));
    expect(await screen.findByRole('dialog', { name: 'Add an automation' })).toBeInTheDocument();
    editor.unmount();

    renderViews('summary', 'test', [], empty);
    expect(screen.getByRole('tabpanel', { name: 'Test' })).toHaveTextContent('Nothing to test yet');
    expect(within(screen.getByRole('tabpanel', { name: 'Test' })).queryByRole('button', { name: 'Add an automation' })).toBeNull();
  });
});

describe('workflow graph in the builder', () => {
  afterEach(() => vi.unstubAllGlobals());

  const graph = () => screen.getByRole('tabpanel', { name: 'Automations' });
  // A node is a button named by its own text: its heading, then its title, with the automation it belongs to spoken after the heading.
  const node = (text: string) => within(graph()).getByRole('button', { name: (name) => name.includes(text) });
  const lightOf = (element: HTMLElement) => element.closest('[data-state]')?.getAttribute('data-state');

  it('opens the recipes from the plus under a stage, and Write your own opens its sentence at the name', async () => {
    const { container } = renderViews('editor', 'automations');
    await expectNoAxeViolations(container);

    await fireEvent.click(node('Add an automation to Phone screen'));

    const recipes = await screen.findByRole('dialog', { name: 'Add an automation' });
    await fireEvent.click(within(recipes).getByRole('button', { name: 'Write your own' }));
    const sheet = await screen.findByRole('dialog', { name: 'New automation' });
    const name = within(sheet).getByRole('textbox', { name: 'Name' }) as HTMLInputElement;
    expect(name.value).toBe('New automation');
    await vi.waitFor(() => expect(name).toHaveFocus());
    expect(within(sheet).getAllByRole('article')).toHaveLength(1);
    expect(within(sheet).getByRole('button', { name: 'Trigger: someone moves into' })).toBeInTheDocument();
    expect(within(sheet).getByRole('button', { name: 'Done' })).toBeInTheDocument();
    expect(node('When New automation')).toBeInTheDocument();
  });

  it("opens an action's sentence at that action when the whole node is pressed", async () => {
    renderViews('editor', 'automations');

    await fireEvent.click(node('Then action 1 in Tag screened'));

    const sheet = await screen.findByRole('dialog', { name: 'Tag screened' });
    await vi.waitFor(() => expect(within(sheet).getByRole('button', { name: 'Action 1: add the tag' })).toHaveFocus());
  });

  it("opens a stage's automations as rows when the stage is pressed", async () => {
    renderViews('editor', 'automations');

    await fireEvent.click(node('Stage Phone screen'));

    const sheet = await screen.findByRole('dialog', { name: 'Automations: Phone screen' });
    expect(within(sheet).getAllByRole('listitem')).toHaveLength(1);
    expect(sheet).toHaveTextContent('Tag screened');
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Add automation' }));
    expect(await screen.findByRole('dialog', { name: 'Add an automation' })).toBeInTheDocument();
  });

  it('adds a condition from the plus on the line, and opens the sentence at it', async () => {
    renderViews('editor', 'automations');

    await fireEvent.click(node('Add a condition to Tag screened'));

    const sheet = await screen.findByRole('dialog', { name: 'Tag screened' });
    await vi.waitFor(() => expect(within(sheet).getByRole('button', { name: /^Condition 1: what to check/ })).toHaveFocus());
    expect(node('If in Tag screened')).toBeInTheDocument();
    expect(within(graph()).queryByRole('button', { name: 'Add a condition to Tag screened' })).toBeNull();
  });

  it('adds an action from the plus under the last one, and opens the sentence at it', async () => {
    renderViews('editor', 'automations');

    await fireEvent.click(node('Add an action to Tag screened'));

    const sheet = await screen.findByRole('dialog', { name: 'Tag screened' });
    await vi.waitFor(() => expect(within(sheet).getByRole('button', { name: /^Action 2: add the tag/ })).toHaveFocus());
    expect(node('Then action 2 in Tag screened')).toBeInTheDocument();
  });

  it('shows a validation problem on its node, as its form control does', () => {
    renderViews('editor', 'automations', [{ path: 'automations[0].actions[0].tag', message: 'Give the tag a name.' }]);

    const pressable = node('Then action 1 in Tag screened');
    expect(pressable).toHaveTextContent('Give the tag a name.');
    expect(pressable.querySelector('[data-problem-icon]')).not.toBeNull();
  });

  it('has no Remove on a node: removing an automation is the Remove in its rules form', () => {
    renderViews('editor', 'automations');

    expect(within(graph()).queryByRole('button', { name: /^Remove / })).toBeNull();
  });

  it('moves between nodes with the arrow keys, down an automation and across to the next column', async () => {
    renderViews('editor', 'automations');
    const lane = node('Lane Whole workflow');
    lane.focus();

    await fireEvent.keyDown(lane, { key: 'ArrowRight' });
    expect(node('Stage Applied')).toHaveFocus();
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowRight' });
    expect(node('Stage Phone screen')).toHaveFocus();
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    expect(node('Add an automation to Phone screen')).toHaveFocus();
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    expect(node('When Tag screened')).toHaveFocus();
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    expect(node('Add a condition to Tag screened')).toHaveFocus();
    await fireEvent.keyDown(document.activeElement!, { key: 'Home' });
    expect(lane).toHaveFocus();
  });

  it("lights a node's path when it has the pointer or the focus, and quietens the rest", async () => {
    renderViews('editor', 'automations');
    const wrapper = (name: string) => node(name).closest<HTMLElement>('.svelte-flow__node')!;

    expect(lightOf(node('When Tag screened'))).toBe('rest');
    await fireEvent.pointerEnter(wrapper('When Tag screened'));
    expect(lightOf(node('Then action 1 in Tag screened'))).toBe('lit');
    expect(lightOf(node('Stage Phone screen'))).toBe('lit');
    expect(lightOf(node('When, off Tell the team'))).toBe('quiet');
    await fireEvent.pointerLeave(wrapper('When Tag screened'));
    expect(lightOf(node('When, off Tell the team'))).toBe('rest');

    node('When, off Tell the team').focus();
    await vi.waitFor(() => expect(lightOf(node('When, off Tell the team'))).toBe('lit'));
    expect(lightOf(node('When Tag screened'))).toBe('quiet');
  });

  it('uses no motion that reduced motion would not turn off', () => {
    renderViews('editor', 'automations');

    const motion = Array.from(graph().querySelectorAll('[data-node-kind] [class], [data-node-kind]'))
      .flatMap((element) => (element.getAttribute('class') ?? '').split(/\s+/))
      .filter((token) => /(^|:)(transition|duration|animate|ease)/.test(token) && !token.startsWith('motion-safe:'));
    expect(motion).toEqual([]);
  });

  it('says what to do in an empty workflow, with one action that adds the first automation', async () => {
    renderViews('editor', 'automations', [], { ...workflow, automations: [] });

    expect(graph()).toHaveTextContent('No automations yet');
    expect(within(graph()).queryByRole('button', { name: /^Add an automation to / })).toBeNull();
    // The map's own button says it once: the panel's header has none while the map is empty (W7).
    expect(within(screen.getByRole('region', { name: 'Stages and automations' })).getAllByRole('button', { name: /^Add (an )?automation$/ })).toHaveLength(1);
    await fireEvent.click(within(graph()).getByRole('button', { name: 'Add an automation' }));

    expect(await screen.findByRole('dialog', { name: 'Add an automation' })).toBeInTheDocument();
  });

  it('lights the path of a sample once it is shown from the Test tab, until it is cleared', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/email-templates': () => Response.json({ templates: [], sendingEnabled: false }),
          'POST /v1/workflows/simulate': () =>
            Response.json({ automations: [{ automationId: 'tag-screen', name: 'Tag screened', outcome: 'Matched', rules: [], actions: [{ type: 'AddTag', tag: 'screened' }], reason: null }] }),
        }),
      ),
    );
    renderViews('editor', 'test');

    // The samples run a moment after the tab opens, on their own.
    const show = await screen.findByRole('link', { name: 'Show in the map: Meets every requirement' }, { timeout: 4000 });
    await fireEvent.click(show);

    await vi.waitFor(() => expect(within(graph()).getByText('Showing the path of your last test.')).toBeInTheDocument());
    expect(screen.getByRole('tab', { name: 'Automations' })).toHaveAttribute('aria-selected', 'true');
    // The link that asked left with its tab, so the focus goes to Clear rather than falling to the page.
    await vi.waitFor(() => expect(within(graph()).getByRole('button', { name: 'Clear' })).toHaveFocus());
    expect(lightOf(node('When Tag screened'))).toBe('lit');
    expect(lightOf(node('When, off Tell the team'))).toBe('quiet');
    await fireEvent.click(within(graph()).getByRole('button', { name: 'Clear' }));
    expect(lightOf(node('When Tag screened'))).toBe('rest');
    expect(within(graph()).queryByText('Showing the path of your last test.')).toBeNull();
  });

  it('offers no path for a test of the guided panel once the workflow has changed since it ran', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/email-templates': () => Response.json({ templates: [], sendingEnabled: false }),
          'POST /v1/workflows/simulate': () =>
            Response.json({ automations: [{ automationId: 'tag-screen', name: 'Tag screened', outcome: 'Matched', rules: [], actions: [{ type: 'AddTag', tag: 'screened' }], reason: null }] }),
        }),
      ),
    );
    const onshow = vi.fn();
    render(WithQueryClient, { props: { component: GuidedTestHarness, props: { workflow: structuredClone(workflow), onshow } } });

    await fireEvent.click(screen.getByRole('button', { name: /A candidate moved to a stage/ }));
    await fireEvent.click(await screen.findByRole('button', { name: /Meets every requirement/ }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Show this path in the graph' }));
    expect(onshow).toHaveBeenCalledTimes(1);

    await fireEvent.click(screen.getByRole('button', { name: 'Edit the workflow' }));

    await vi.waitFor(() => expect(screen.queryByRole('button', { name: 'Show this path in the graph' })).toBeNull());
    // The answer on screen stays the one the tested workflow gave, whatever the edit, and says it is from before the edit.
    expect(screen.getByRole('list', { name: 'What would happen, in order' })).toHaveTextContent('Tag screened');
    expect(screen.getByText('The workflow changed after this test.')).toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'Run it again' }));
    await screen.findByRole('button', { name: 'Show this path in the graph' });
    expect(screen.queryByText('The workflow changed after this test.')).toBeNull();
  });

  it('gives a node that cannot be pressed the focus ring too: it is still a tab stop', () => {
    renderViews('summary', 'automations');

    const box = within(screen.getByRole('tabpanel', { name: 'Automations' })).getByRole('group', { name: 'When: Tag screened' });
    expect(box).toHaveAttribute('tabindex', '0');
    expect(box.className).toContain('focus-visible:ring-2');
  });

  it('draws no map on a phone: the tab is the list, which edits through the same sheets', async () => {
    stubViewport(390);
    renderViews('editor', 'automations');

    const panel = within(graph());
    expect(panel.queryByRole('group', { name: 'View' })).toBeNull();
    expect(panel.queryByRole('group', { name: /^When/ })).toBeNull();
    expect(graph()).not.toHaveTextContent('graph is for reading');
    const rows = panel.getAllByRole('listitem');
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent('Tell the team, off');
    await fireEvent.click(within(rows[1]!).getByRole('button'));
    expect(await screen.findByRole('dialog', { name: 'Tag screened' })).toBeInTheDocument();
  });

  it('switches between the list and the map on a wider screen, the map first', async () => {
    stubViewport(1440);
    renderViews('editor', 'automations');

    const views = within(graph()).getByRole('group', { name: 'View' });
    expect(within(views).getByRole('button', { name: 'Map' })).toHaveAttribute('aria-pressed', 'true');
    expect(node('When Tag screened')).toBeInTheDocument();

    await fireEvent.click(within(views).getByRole('button', { name: 'List' }));
    expect(within(graph()).queryByRole('button', { name: (name) => name.includes('When Tag screened') })).toBeNull();
    expect(within(graph()).getAllByRole('listitem')).toHaveLength(2);

    await fireEvent.click(within(views).getByRole('button', { name: 'Map' }));
    expect(node('When Tag screened')).toBeInTheDocument();
  });

  it('reads the list without buttons where the workflow only reads', async () => {
    stubViewport(390);
    renderViews('summary', 'automations');

    expect(within(graph()).getAllByRole('listitem')).toHaveLength(2);
    expect(within(graph()).queryByRole('button')).toBeNull();
  });
});
