// One stage's sheet: its name, a time limit in plain choices with a reminder that follows it, its scorecard and
// automations, and the stage's kind under More options.
import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub } from '../../../test-support/fixtures.js';
import StageSheetHarness from '../../../test-support/stage-sheet-harness.svelte';
import WithQueryClient from '../../../test-support/with-query-client.svelte';

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'interview', name: 'Interview', kind: 'Interview' },
    { id: 'hired', name: 'Hired', kind: 'Hired', slaDays: 5 },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: { questions: [] },
  verificationPolicy: { aiUse: 'AllowedWithDisclosure' },
  automations: [
    { id: 'welcome', name: 'Welcome', trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'NotifyTeam' }] },
    {
      id: 'ask-review',
      name: 'Ask for a review',
      trigger: { type: 'StageEntered', stageId: 'interview' },
      conditions: null,
      actions: [{ type: 'AssignReviewer', assigneeUserId: '01a157d4-0000-7000-8000-0000000000aa' }],
    },
  ],
};

const callbacks = () => ({ onscorecard: vi.fn(), onautomations: vi.fn(), onremove: vi.fn() });
const renderSheet = (props: { stageId?: string; errors?: DefinitionError[]; flow?: HiringWorkflow } & Partial<ReturnType<typeof callbacks>> = {}) => {
  const { flow = workflow, stageId = 'interview', ...rest } = props;
  return render(WithQueryClient, { props: { component: StageSheetHarness, props: { workflow: structuredClone(flow), stageId, ...rest } } });
};

const sheet = () => screen.findByRole('dialog');
const stages = () => JSON.parse(screen.getByTestId('stages').textContent!) as { id: string; name: string; kind: string; slaDays: number | null }[];
const interview = () => stages().find((stage) => stage.id === 'interview')!;
const automationIds = () => screen.getByTestId('automations').textContent!.split(',').filter(Boolean);
const limit = (dialog: HTMLElement, name: string) => within(dialog).getByRole('radio', { name });
// The button's name is its word and the stage: "Choose scorecard for Interview".
const scorecardButton = (dialog: HTMLElement, word: 'Choose' | 'Change') =>
  within(dialog).getByRole('button', { name: `${word} scorecard for Interview` });
const reminder = 'The team gets a reminder after';

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/email-templates': () => Response.json({ templates: [], sendingEnabled: false }) })));
});

afterEach(() => vi.unstubAllGlobals());

describe('stage sheet', () => {
  it('is a dialog named for the stage, with its name, time limit, scorecard and automations, and Done as its one primary', async () => {
    const { container } = renderSheet();
    const dialog = await sheet();

    expect(dialog).toHaveAccessibleName('Interview');
    expect(within(dialog).getByRole('textbox', { name: 'Name' })).toHaveValue('Interview');
    expect(within(dialog).getAllByRole('radio').map((radio) => radio.textContent?.trim())).toEqual(['None', '2 days', '3 days', '5 days', '1 week', 'Other']);
    expect(limit(dialog, 'None')).toBeChecked();
    expect(scorecardButton(dialog, 'Choose')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Add an automation' })).toBeInTheDocument();
    expect(within(dialog).getByRole('link', { name: 'Open in Automations' })).toHaveAttribute('href', '?tab=automations');
    expect(within(dialog).getByRole('button', { name: 'Done' })).toBeInTheDocument();
    await expectNoAxeViolations(container.ownerDocument.body);
  });

  it("separates the button's word from the stage it is for, in its accessible name", async () => {
    renderSheet();
    const dialog = await sheet();

    expect(within(dialog).getByRole('button', { name: 'Choose scorecard for Interview' })).toBeInTheDocument();
  });

  it('lists the automations at the stage in the journey\'s words, never naming the person a step asks', async () => {
    renderSheet();
    const dialog = await sheet();

    expect(within(dialog).getByText('Asks the team to review')).toBeInTheDocument();
    expect(dialog).not.toHaveTextContent('01a157d4');
    expect(within(dialog).queryByText(/Welcome|Tells the team/)).toBeNull();
  });

  it('does not list the reminder its own time limit made, which the sentence under the limit already says', async () => {
    renderSheet();
    const dialog = await sheet();
    await fireEvent.click(limit(dialog, '3 days'));

    expect(automationIds()).toContain('reminder-interview');
    expect(dialog).toHaveTextContent('The team gets a reminder after 3 days.');
    expect(within(dialog).getAllByRole('listitem')).toHaveLength(1);
    expect(dialog).not.toHaveTextContent('when the time limit passes');
  });

  it('closes the sheet when Open in Automations is pressed', async () => {
    renderSheet();
    const dialog = await sheet();
    const link = within(dialog).getByRole('link', { name: 'Open in Automations' });
    link.addEventListener('click', (event) => event.preventDefault());

    await fireEvent.click(link);

    expect(screen.getByTestId('open')).toHaveTextContent('none');
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('shows no time limit and no scorecard for a stage that ends the candidacy', async () => {
    renderSheet({ stageId: 'hired' });
    const dialog = await sheet();

    expect(within(dialog).queryByText('Time limit')).toBeNull();
    expect(within(dialog).queryByRole('radio')).toBeNull();
    expect(within(dialog).queryByText('Scorecard')).toBeNull();
    expect(within(dialog).getByRole('button', { name: 'Add an automation' })).toBeInTheDocument();
  });

  it('opens the scorecard sheet and the automations drawer of this stage from its buttons', async () => {
    const watched = callbacks();
    renderSheet(watched);
    const dialog = await sheet();

    await fireEvent.click(scorecardButton(dialog, 'Choose'));
    expect(watched.onscorecard).toHaveBeenCalledExactlyOnceWith(1);
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Add an automation' }));
    expect(watched.onautomations).toHaveBeenCalledExactlyOnceWith('interview');
  });

  it('says Change, not Choose, once the stage has a scorecard, and names it', async () => {
    renderSheet({ flow: { ...workflow, stages: workflow.stages.map((stage) => (stage.id === 'interview' ? { ...stage, scorecard: { name: 'Interview guide', questions: [] } } : stage)) } });
    const dialog = await sheet();

    expect(scorecardButton(dialog, 'Change')).toBeInTheDocument();
    expect(dialog).toHaveTextContent('Interview guide');
    expect(dialog).not.toHaveTextContent('None yet');
  });

  it('shows what is wrong with the name under it, and marks the field', async () => {
    renderSheet({ errors: [{ path: 'stages[1].name', message: 'Give the stage a name.' }] });
    const dialog = await sheet();

    expect(dialog).toHaveTextContent('Give the stage a name.');
    expect(within(dialog).getByRole('textbox', { name: 'Name' })).toHaveAttribute('aria-invalid', 'true');
  });

  it('closes with Done', async () => {
    renderSheet();
    const dialog = await sheet();

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Done' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(screen.getByTestId('open')).toHaveTextContent('none');
  });
});

describe('the time limit', () => {
  it('sets the limit from a choice, says the team gets a reminder, and adds the reminder to the workflow', async () => {
    renderSheet();
    const dialog = await sheet();
    expect(dialog).not.toHaveTextContent(reminder);

    await fireEvent.click(limit(dialog, '5 days'));

    expect(interview().slaDays).toBe(5);
    expect(limit(dialog, '5 days')).toBeChecked();
    expect(dialog).toHaveTextContent('The team gets a reminder after 5 days.');
    expect(automationIds()).toEqual(['welcome', 'ask-review', 'reminder-interview']);
  });

  it('says a week for 7 days', async () => {
    renderSheet();
    const dialog = await sheet();

    await fireEvent.click(limit(dialog, '1 week'));

    expect(interview().slaDays).toBe(7);
    expect(dialog).toHaveTextContent('The team gets a reminder after 1 week.');
  });

  it('removes the reminder, and only the reminder, when the limit is set to None', async () => {
    renderSheet();
    const dialog = await sheet();
    await fireEvent.click(limit(dialog, '3 days'));
    expect(automationIds()).toContain('reminder-interview');

    await fireEvent.click(limit(dialog, 'None'));

    expect(interview().slaDays).toBeNull();
    expect(automationIds()).toEqual(['welcome', 'ask-review']);
    expect(dialog).not.toHaveTextContent(reminder);
  });

  it('adds no second reminder when an automation already tells the team, and still says the team gets one', async () => {
    const told: HiringWorkflow = {
      ...workflow,
      automations: [
        ...workflow.automations,
        { id: 'mine', name: 'Mine', trigger: { type: 'SlaBreached', stageId: 'interview' }, conditions: null, actions: [{ type: 'NotifyTeam', note: 'Late' }] },
      ],
    };
    renderSheet({ flow: told });
    const dialog = await sheet();

    await fireEvent.click(limit(dialog, '2 days'));

    expect(automationIds()).toEqual(['welcome', 'ask-review', 'mine']);
    expect(dialog).toHaveTextContent('The team gets a reminder after 2 days.');

    // Taking the limit away leaves the person's own automation alone.
    await fireEvent.click(limit(dialog, 'None'));
    expect(automationIds()).toEqual(['welcome', 'ask-review', 'mine']);
  });

  it('says nothing about a reminder where the validator would refuse one: a stage a verification check moves candidates into', async () => {
    const routed: HiringWorkflow = {
      ...workflow,
      automations: [
        ...workflow.automations,
        { id: 'route', name: 'Route', trigger: { type: 'VerificationAssessed' }, conditions: null, actions: [{ type: 'MoveToStage', stageId: 'interview' }] },
      ],
    };
    renderSheet({ flow: routed });
    const dialog = await sheet();

    await fireEvent.click(limit(dialog, '3 days'));

    expect(interview().slaDays).toBe(3);
    expect(dialog).not.toHaveTextContent(reminder);
    expect(automationIds()).not.toContain('reminder-interview');
  });

  it('opens with a stored limit already chosen', async () => {
    renderSheet({ flow: { ...workflow, stages: workflow.stages.map((stage) => (stage.id === 'interview' ? { ...stage, slaDays: 5 } : stage)) } });
    const dialog = await sheet();

    expect(limit(dialog, '5 days')).toBeChecked();
    expect(limit(dialog, 'None')).not.toBeChecked();
  });

  it('asks for the days under Other, and sets the limit from them', async () => {
    renderSheet();
    const dialog = await sheet();
    expect(within(dialog).queryByRole('spinbutton', { name: 'Days' })).toBeNull();

    await fireEvent.click(limit(dialog, 'Other'));
    const days = within(dialog).getByRole('spinbutton', { name: 'Days' });
    await fireEvent.change(days, { target: { value: '10' } });

    expect(interview().slaDays).toBe(10);
    expect(limit(dialog, 'Other')).toBeChecked();
    expect(dialog).toHaveTextContent('The team gets a reminder after 10 days.');
    expect(automationIds()).toContain('reminder-interview');
  });

  it('keeps the days between 1 and 365, and clears the limit when they are emptied', async () => {
    renderSheet();
    const dialog = await sheet();
    await fireEvent.click(limit(dialog, 'Other'));
    const days = within(dialog).getByRole('spinbutton', { name: 'Days' });

    await fireEvent.change(days, { target: { value: '900' } });
    expect(interview().slaDays).toBe(365);
    await fireEvent.change(days, { target: { value: '0' } });
    expect(interview().slaDays).toBe(1);
    await fireEvent.change(days, { target: { value: '' } });
    expect(interview().slaDays).toBeNull();
    expect(automationIds()).not.toContain('reminder-interview');
  });

  it('opens a limit that is not one of the choices under Other, with its days', async () => {
    renderSheet({ flow: { ...workflow, stages: workflow.stages.map((stage) => (stage.id === 'interview' ? { ...stage, slaDays: 4 } : stage)) } });
    const dialog = await sheet();

    expect(limit(dialog, 'Other')).toBeChecked();
    expect(within(dialog).getByRole('spinbutton', { name: 'Days' })).toHaveValue(4);
  });

  it('renames the reminder with the stage, so it never says a name the stage no longer has', async () => {
    renderSheet();
    const dialog = await sheet();
    await fireEvent.click(limit(dialog, '3 days'));
    expect(screen.getByTestId('automation-names')).toHaveTextContent('Remind the team when Interview waits too long');

    await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Name' }), { target: { value: 'Panel interview' } });

    expect(screen.getByTestId('automation-names')).toHaveTextContent('Remind the team when Panel interview waits too long');
    expect(automationIds()).toEqual(['welcome', 'ask-review', 'reminder-interview']);
  });
});

describe('the name and the kind', () => {
  it('follows the name in the title as it is typed', async () => {
    renderSheet();
    const dialog = await sheet();

    await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Name' }), { target: { value: 'Panel interview' } });

    expect(interview().name).toBe('Panel interview');
    expect(dialog).toHaveAccessibleName('Panel interview');
  });

  it('sets the kind from the name while the kind is still what the old name gave', async () => {
    renderSheet();
    const dialog = await sheet();
    const name = within(dialog).getByRole('textbox', { name: 'Name' });

    await fireEvent.input(name, { target: { value: 'Trial shift' } });
    expect(interview().kind).toBe('Assessment');
    await fireEvent.input(name, { target: { value: 'Reference check' } });
    expect(interview().kind).toBe('Verification');
  });

  it('stops following once the kind is set by hand under More options', async () => {
    renderSheet();
    const dialog = await sheet();
    await fireEvent.click(within(dialog).getByRole('button', { name: 'More options' }));
    await fireEvent.change(within(dialog).getByRole('combobox', { name: 'Kind' }), { target: { value: 'Offer' } });
    expect(interview().kind).toBe('Offer');

    await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Name' }), { target: { value: 'Trial shift' } });

    expect(interview().name).toBe('Trial shift');
    expect(interview().kind).toBe('Offer');
  });

  it('never changes the kind of the first stage, which is where candidates arrive', async () => {
    renderSheet({ stageId: 'applied' });
    const dialog = await sheet();

    await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Name' }), { target: { value: 'Offer desk' } });

    expect(stages()[0]).toMatchObject({ name: 'Offer desk', kind: 'Applied' });
  });
});

describe('more options', () => {
  it('hides the kind until More options is opened, and says whether it is open', async () => {
    renderSheet();
    const dialog = await sheet();
    const more = within(dialog).getByRole('button', { name: 'More options' });

    expect(more).toHaveAttribute('aria-expanded', 'false');
    expect(within(dialog).queryByRole('combobox', { name: 'Kind' })).toBeNull();
    await fireEvent.click(more);

    expect(more).toHaveAttribute('aria-expanded', 'true');
    expect(within(dialog).getByRole('combobox', { name: 'Kind' })).toHaveValue('Interview');
    await fireEvent.click(more);
    expect(within(dialog).queryByRole('combobox', { name: 'Kind' })).toBeNull();
  });
});

describe('removing from the sheet', () => {
  it('has Remove stage in the footer menu, which closes the sheet and removes the stage at its place', async () => {
    const watched = callbacks();
    renderSheet(watched);
    const dialog = await sheet();

    await fireEvent.pointerDown(within(dialog).getByRole('button', { name: 'More for Interview' }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Remove stage' }));

    expect(watched.onremove).toHaveBeenCalledExactlyOnceWith(1);
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('has no Remove stage on the first stage', async () => {
    renderSheet({ stageId: 'applied' });
    const dialog = await sheet();

    expect(within(dialog).queryByRole('button', { name: /^More for / })).toBeNull();
  });
});
