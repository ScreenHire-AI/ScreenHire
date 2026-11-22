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
  it('reads the pipeline as content: stages in a flow with their chips, Ends in the footer, automations as rows', async () => {
    const { container } = renderViews('summary');

    const pane = screen.getByRole('tabpanel', { name: 'Pipeline' });
    expect(controls(pane)).toHaveLength(0);

    const stages = within(pane).getByRole('region', { name: 'Stages' });
    const line = within(stages).getByRole('list', { name: 'Stages in order' });
    expect(Array.from(line.children).map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      '1 Applied 1',
      '2 Phone screen Respond within 3 days 1',
    ]);
    expect(within(line).getByRole('button', { name: '1 question at Applied' })).toBeInTheDocument();
    expect(within(line).getByRole('button', { name: '1 automation at Phone screen' })).toBeInTheDocument();
    const ends = within(stages).getByRole('region', { name: 'Ends' });
    expect(ends).toHaveTextContent('Hired');
    expect(ends).toHaveTextContent('Not progressing');
    expect(within(stages).queryByRole('link')).toBeNull();

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
      props: { component: WorkflowHarness, props: { workflow: structuredClone(workflow), errors: [], mode: 'summary', tab: 'forms', aiUse: { policy: 'AllowedWithDisclosure' } } },
    });

    const pane = screen.getByRole('tabpanel', { name: 'Forms' });
    expect(pane.querySelector('[data-ai-use-row]')).toHaveTextContent('AI use. Asked by ScreenHire. Policy: Allowed with disclosure.');
    expect(within(pane).queryByRole('link', { name: 'Change policy' })).toBeNull();
    const questions = within(pane).getByRole('region', { name: 'Application form' });
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
    expect(within(pane).queryByRole('region', { name: 'Application form' })).toBeNull();
    expect(within(pane).getByRole('group', { name: 'Screen size' })).toBeInTheDocument();
    // The preview pages as the candidate's form does: the details first, the questions after Next.
    await fireEvent.click(within(pane).getByRole('button', { name: 'Next' }));
    expect(within(pane).getByRole('group', { name: 'Do you have the right to work here?' })).toBeInTheDocument();
  });

  it('reads each other form as its own panel, saying when it is sent', () => {
    const followUp: HiringWorkflow = {
      ...workflow,
      followUpQuestionnaires: [{ id: 'availability', name: 'Availability', questions: [{ id: 'start', type: 'ShortText', label: 'When could you start?' }] }],
    };
    render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: followUp, errors: [], mode: 'summary', tab: 'forms' } } });

    const panel = screen.getByRole('region', { name: 'Availability' });
    expect(controls(panel)).toHaveLength(0);
    expect(panel).toHaveTextContent('When could you start?');
    expect(panel).toHaveTextContent('Only when sent by hand');
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

  it('takes Fix to where the problem is: a stage in its sheet, an automation in its form, a question on Forms', async () => {
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
    await waitFor(() => expect(screen.getByRole('tab', { name: 'Forms' })).toHaveAttribute('aria-selected', 'true'));
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
    expect(cards()).toEqual(['Stage 1: Applied', 'Stage 2: Phone screen']);
    expect(within(within(stages).getByRole('region', { name: 'Ends' })).getAllByRole('button').map((end) => end.textContent?.trim())).toEqual([
      'Hired',
      'Not progressing',
    ]);
    // The time limit is in the stage's sheet, not on the card, and a card is no panel inside the panel (S4).
    expect(within(stages).queryByRole('spinbutton')).toBeNull();
    expect(within(stages).getByRole('group', { name: 'Stage 2: Phone screen' })).toHaveTextContent('Respond within 3 days');
    expect(within(within(stages).getByRole('group', { name: 'Stage 2: Phone screen' })).getByRole('button', { name: '1 automation at Phone screen' })).toBeInTheDocument();
    expect(stages.querySelectorAll('[data-slot="panel"]')).toHaveLength(0);
    // The old panel of automations is gone: they are counted on their stage and listed under Automations.
    expect(screen.queryByRole('region', { name: 'Workflow automations' })).toBeNull();

    await fireEvent.click(within(stages).getByRole('button', { name: 'Reorder Applied' }));
    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim())).toEqual(['Move right', 'Move to end']);
    await fireEvent.click(screen.getByRole('menuitem', { name: 'Move right' }));
    expect(cards()).toEqual(['Stage 1: Phone screen', 'Stage 2: Applied']);

    await fireEvent.click(within(stages).getByRole('button', { name: 'Reorder Applied' }));
    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim())).toEqual(['Move left', 'Move to start']);
    await fireEvent.click(screen.getByRole('menuitem', { name: 'Move to start' }));
    // The stages that end a candidacy keep their place after the open ones.
    expect(cards()).toEqual(['Stage 1: Applied', 'Stage 2: Phone screen']);
    expect(screen.getByTestId('draft')).toHaveTextContent('"stages":[{"id":"applied"');

    await expectNoAxeViolations(container);
  });

  it('adds a library form, sends it when they enter a stage, reads its questions from the library and removes it with its automation', async () => {
    const formId = '01a157d4-0000-7000-8000-00000000f001';
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/form-templates': () =>
            Response.json({
              forms: [
                { id: formId, name: 'Screening questionnaire', kind: 'Standard', questions: 1, latestPublishedVersion: 1, draftVersion: null, lastUpdated: { at: null, by: null }, archived: false },
                { id: 'clash', name: 'Right to work again', kind: 'Standard', questions: 1, latestPublishedVersion: 1, draftVersion: null, lastUpdated: { at: null, by: null }, archived: false },
              ],
              equalityMonitoringEnabled: false,
            }),
          [`GET /v1/form-templates/${formId}/versions/1`]: () =>
            Response.json({
              formTemplateId: formId,
              version: 1,
              status: 'Published',
              kind: 'Standard',
              content: { name: 'Screening questionnaire', questions: [{ id: 'q_notice', type: 'ShortText', label: 'How much notice do you give?', required: false }] },
              createdAt: '2026-11-20T09:00:00Z',
              publishedAt: '2026-11-20T09:00:00Z',
            }),
          'GET /v1/form-templates/clash/versions/1': () =>
            Response.json({
              formTemplateId: 'clash',
              version: 1,
              status: 'Published',
              kind: 'Standard',
              content: { name: 'Right to work again', questions: [{ id: 'right_to_work', type: 'YesNo', label: 'Right to work?', required: false }] },
              createdAt: '2026-11-20T09:00:00Z',
              publishedAt: '2026-11-20T09:00:00Z',
            }),
        }),
      ),
    );
    const { container } = render(WithQueryClient, {
      props: {
        component: WorkflowHarness,
        props: { workflow: structuredClone(workflow), errors: [], mode: 'editor', tab: 'forms', aiUse: { policy: 'NotAllowed', href: '/verification/checks' } },
      },
    });
    const pane = screen.getByRole('tabpanel', { name: 'Forms' });
    const forms = within(pane).getByRole('group', { name: 'Forms' });
    const apply = within(forms).getByRole('button', { name: /Application form/ });
    expect(apply).toHaveAttribute('aria-pressed', 'true');
    expect(apply).toHaveTextContent('When they apply');
    expect(apply).toHaveTextContent('1 question');

    // The AI-use question is locked: ScreenHire asks it, and the policy is the organization's.
    expect(pane.querySelector('[data-ai-use-row]')).toHaveTextContent('AI use. Asked by ScreenHire. Policy: Not allowed.');
    expect(within(pane).getByRole('link', { name: 'Change policy' })).toHaveAttribute('href', '/verification/checks');

    // New form offers the library's published forms; one whose question id another form here asks cannot be added.
    await fireEvent.click(within(forms).getByRole('button', { name: 'New form' }));
    const sheet = await screen.findByRole('dialog', { name: 'Add a form' });
    await waitFor(() => expect(within(sheet).getByRole('button', { name: 'Add Screening questionnaire' })).toBeEnabled());
    await waitFor(() => expect(within(sheet).getByRole('button', { name: 'Add Right to work again' })).toBeDisabled());
    expect(sheet).toHaveTextContent('Another form here already asks "Right to work?". Change one of them in the library first.');
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Add Screening questionnaire' }));

    const added = await within(forms).findByRole('button', { name: /Screening questionnaire/ });
    expect(added).toHaveAttribute('aria-pressed', 'true');
    expect(added).toHaveTextContent('By hand only');

    // When it is sent is the form's own choice, and it writes the automation behind it.
    await fireEvent.change(within(pane).getByRole('combobox', { name: "When it's sent" }), { target: { value: 'stage' } });
    expect(added).toHaveTextContent('Entering Phone screen');
    expect(within(pane).getByRole('combobox', { name: 'Stage' })).toHaveValue('phone-screen');
    expect(screen.getByTestId('draft')).toHaveTextContent('"trigger":{"type":"StageEntered","stageId":"phone-screen"},"conditions":null,"actions":[{"type":"SendQuestionnaire","questionnaireId":"screening_questionnaire"}]');

    // Its questions read here and change in the library, so every template that uses it can take the change.
    const questions = within(pane).getByRole('region', { name: 'Questions' });
    expect(controls(questions)).toHaveLength(0);
    expect(questions).toHaveTextContent('How much notice do you give?');
    expect(within(questions).getByRole('link', { name: 'Edit in the library' })).toHaveAttribute('href', `/templates/forms/${formId}`);
    expect(pane.querySelector('[data-ai-use-row]')).toBeNull();
    await expectNoAxeViolations(container);

    // Removing the form takes the automation that emails it, and says so first.
    await fireEvent.pointerDown(within(pane).getByRole('button', { name: 'Actions for Screening questionnaire' }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Remove form' }));
    const dialog = await screen.findByRole('dialog', { name: /Remove Screening questionnaire/ });
    expect(dialog).toHaveTextContent('This also removes Email the form when they enter Phone screen.');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove form' }));
    await waitFor(() => expect(within(forms).queryByRole('button', { name: /Screening questionnaire/ })).toBeNull());
    expect(within(forms).getByRole('button', { name: /Application form/ })).toHaveAttribute('aria-pressed', 'true');
    vi.unstubAllGlobals();
  });

  it("edits a job's own older form, which copies no library form, in the form builder, with no File kind", async () => {
    const own: HiringWorkflow = {
      ...structuredClone(workflow),
      followUpQuestionnaires: [{ id: 'availability', name: 'Availability', questions: [{ id: 'start', type: 'ShortText', label: 'When could you start?' }] }],
    };
    render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: own, errors: [], mode: 'editor', tab: 'forms' } } });
    const pane = screen.getByRole('tabpanel', { name: 'Forms' });
    await fireEvent.click(within(within(pane).getByRole('group', { name: 'Forms' })).getByRole('button', { name: /Availability/ }));

    const formPanel = within(pane).getByRole('region', { name: 'Form' });
    expect(within(formPanel).getByRole('textbox', { name: 'Name' })).toHaveValue('Availability');
    const questions = within(pane).getByRole('region', { name: 'Questions' });
    expect(within(questions).getByRole('button', { name: /^Edit question 1: When could you start\? / })).toBeInTheDocument();

    // A follow-up's page takes no uploads, so its answer menu has no File.
    await fireEvent.pointerDown(within(questions).getByRole('button', { name: 'Add a question' }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    expect(within(menu).queryByRole('menuitem', { name: 'File' })).toBeNull();
  });

  it('shows the apply form as the candidate sees it, ending in the AI-use question under the policy', async () => {
    render(WithQueryClient, {
      props: { component: WorkflowHarness, props: { workflow: structuredClone(workflow), errors: [], mode: 'editor', tab: 'forms', aiUse: { policy: 'Allowed' } } },
    });
    const pane = screen.getByRole('tabpanel', { name: 'Forms' });
    await fireEvent.click(within(pane).getByRole('button', { name: 'Preview' }));

    const shown = within(pane.querySelector<HTMLElement>('[data-preview-pane]')!);
    // The AI-use question is on the last step, the review.
    while (shown.queryByRole('button', { name: 'Next' })) await fireEvent.click(shown.getByRole('button', { name: 'Next' }));
    expect(shown.getByRole('group', { name: /Did you use AI tools for this application\? \(optional\)/ })).toBeInTheDocument();
  });

  it('says a form no automation sends goes only when sent by hand', () => {
    const unsent: HiringWorkflow = {
      ...structuredClone(workflow),
      automations: [],
      followUpQuestionnaires: [{ id: 'references', name: 'References', questions: [{ id: 'who', type: 'ShortText', label: 'Who can we ask?' }] }],
    };
    render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: unsent, errors: [], mode: 'editor', tab: 'forms' } } });
    const pane = screen.getByRole('tabpanel', { name: 'Forms' });

    expect(within(within(pane).getByRole('group', { name: 'Forms' })).getByRole('button', { name: /References/ })).toHaveTextContent('By hand only');
  });

  it("opens a stage's form on the Forms tab with its card chosen, from the stage's questions list", async () => {
    const sent: HiringWorkflow = {
      ...structuredClone(workflow),
      followUpQuestionnaires: [{ id: 'availability', name: 'Availability', questions: [{ id: 'start', type: 'ShortText', label: 'When could you start?' }] }],
      automations: [
        ...structuredClone(workflow.automations),
        { id: 'send-availability', name: '', trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'SendQuestionnaire', questionnaireId: 'availability' }] },
      ],
    };
    render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: sent, errors: [], mode: 'editor', tab: 'pipeline' } } });

    const card = within(screen.getByRole('region', { name: 'Stages' })).getByRole('group', { name: 'Stage 2: Phone screen' });
    await fireEvent.click(within(card).getByRole('button', { name: '1 question at Phone screen' }));
    await fireEvent.click(within(await screen.findByRole('dialog', { name: '1 question at Phone screen' })).getByRole('link', { name: 'Availability' }));

    await waitFor(() => expect(screen.getByRole('tab', { name: 'Forms' })).toHaveAttribute('aria-selected', 'true'));
    const forms = within(screen.getByRole('tabpanel', { name: 'Forms' })).getByRole('group', { name: 'Forms' });
    expect(within(forms).getByRole('button', { name: /^Availability/ })).toHaveAttribute('aria-pressed', 'true');
    expect(within(forms).getByRole('button', { name: /^Application form/ })).toHaveAttribute('aria-pressed', 'false');
  });

  it('offers Send a form and the form trigger only with a follow-up to name', async () => {
    const followUp: HiringWorkflow = {
      ...workflow,
      followUpQuestionnaires: [{ id: 'availability', name: 'Availability', questions: [{ id: 'start', type: 'ShortText', label: 'When could you start?' }] }],
    };
    const view = render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: structuredClone(workflow), errors: [], mode: 'editor' } } });
    let sheet = await writeOwnAsRules('Phone screen');
    expect(within(sheet).queryByRole('option', { name: 'Send a form' })).toBeNull();
    view.unmount();

    render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: followUp, errors: [], mode: 'editor' } } });
    sheet = await writeOwnAsRules('Phone screen');
    const action = within(sheet).getByRole('combobox', { name: 'Action 1' });
    await fireEvent.change(action, { target: { value: 'SendQuestionnaire' } });
    expect((within(sheet).getByRole('combobox', { name: 'Form' }) as HTMLSelectElement).value).toBe('availability');

    const trigger = within(sheet).getByRole('combobox', { name: 'Trigger' });
    await fireEvent.change(trigger, { target: { value: 'QuestionnaireCompleted' } });
    const pickers = within(sheet).getAllByRole('combobox', { name: 'Form' });
    expect(within(pickers[0]!).getByRole('option', { name: 'Any form' })).toBeInTheDocument();
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

    // A stage that ends the application has no scorecard, and no menu: its button opens its sheet (A3).
    expect(within(stages).queryByRole('button', { name: 'Actions for Hired' })).toBeNull();
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
    // The stage's card has a Scorecard chip that opens the sheet again.
    expect(within(within(stages).getByRole('group', { name: 'Stage 2: Phone screen' })).getByRole('button', { name: 'Scorecard' })).toBeInTheDocument();
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
      // The card and its menu left with the stage, and it was the last, so the last card left takes the focus rather than the page.
      await vi.waitFor(() => expect(within(stages).getByRole('button', { name: 'Applied' })).toHaveFocus());
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

describe('try it in the builder', () => {
  afterEach(() => vi.unstubAllGlobals());

  it("opens the stage chosen under Change something at the end of Try it, in the builder's own stage sheet", async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) =>
        `${init?.method ?? 'GET'} ${String(input)}` === 'POST /v1/workflows/simulate' ? Response.json({ automations: [] }) : new Response(null, { status: 404 }),
      ),
    );
    renderViews('editor', 'test');
    const tab = screen.getByRole('tabpanel', { name: 'Try it' });

    await fireEvent.click(within(tab).getByRole('button', { name: /A good fit/ }));
    for (const next of ['Phone screen', 'Hired']) {
      await fireEvent.click(await within(tab).findByRole('button', { name: `Move Jordan to ${next}` }));
      await within(tab).findByRole('heading', { level: 3, name: next });
    }
    await fireEvent.click(within(tab).getByRole('button', { name: 'See the whole process' }));
    await fireEvent.pointerDown(await within(tab).findByRole('button', { name: 'Change something' }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Phone screen' }));

    expect(await screen.findByRole('dialog', { name: 'Phone screen' })).toBeInTheDocument();
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
    const first = node('Stage Applied');
    first.focus();

    await fireEvent.keyDown(first, { key: 'ArrowRight' });
    expect(node('Stage Phone screen')).toHaveFocus();
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    expect(node('Add an automation to Phone screen')).toHaveFocus();
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    expect(node('When Tag screened')).toHaveFocus();
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    expect(node('Add a condition to Tag screened')).toHaveFocus();
    await fireEvent.keyDown(document.activeElement!, { key: 'Home' });
    expect(first).toHaveFocus();
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
