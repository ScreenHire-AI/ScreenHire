// One stage's sheet: its name and kind, then sections shown by kind: Timing (Respond within in plain choices with a reminder
// that follows it and what runs when it passes), Meeting, Scorecard, and the automations as names that link to their tab.
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

const callbacks = () => ({ onscorecard: vi.fn(), onremove: vi.fn() });
const renderSheet = (props: { stageId?: string; errors?: DefinitionError[]; flow?: HiringWorkflow } & Partial<ReturnType<typeof callbacks>> = {}) => {
  const { flow = workflow, stageId = 'interview', ...rest } = props;
  return render(WithQueryClient, { props: { component: StageSheetHarness, props: { workflow: structuredClone(flow), stageId, ...rest } } });
};

const sheet = () => screen.findByRole('dialog');
const stages = () => JSON.parse(screen.getByTestId('stages').textContent!) as { id: string; name: string; kind: string; slaDays: number | null; meetingName?: string }[];
const interview = () => stages().find((stage) => stage.id === 'interview')!;
const automationIds = () => screen.getByTestId('automations').textContent!.split(',').filter(Boolean);
const limit = (dialog: HTMLElement, name: string) => within(dialog).getByRole('radio', { name });
// The button's name is its word and the stage: "Choose scorecard for Interview".
const scorecardButton = (dialog: HTMLElement, word: 'Choose' | 'Change') =>
  within(dialog).getByRole('button', { name: `${word} scorecard for Interview` });
const reminder = 'When overdue';
const overdue = (dialog: HTMLElement) => dialog.querySelector<HTMLElement>('[data-when-overdue]');
const tellsTeam = /When overdue\s*Tell the team/;
const help = 'How long a candidate should wait here before your team moves them on. After that they show as Overdue. Nothing happens to the candidate.';

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
    expect(within(dialog).getByRole('link', { name: 'Edit in Automations' })).toHaveAttribute('href', '?tab=automations');
    expect(within(dialog).getByRole('button', { name: 'Done' })).toBeInTheDocument();
    await expectNoAxeViolations(container.ownerDocument.body);
  });

  it("separates the button's word from the stage it is for, in its accessible name", async () => {
    renderSheet();
    const dialog = await sheet();

    expect(within(dialog).getByRole('button', { name: 'Choose scorecard for Interview' })).toBeInTheDocument();
  });

  it('lists the automations at the stage by their own names, each a link to it, with no way to add or change one here', async () => {
    renderSheet();
    const dialog = await sheet();

    const section = within(dialog).getByRole('region', { name: 'Automations' });
    expect(within(section).getAllByRole('link').map((link) => [link.textContent?.trim(), link.getAttribute('href')])).toEqual([
      ['Ask for a review', '?tab=automations&show=ask-review'],
      ['Edit in Automations', '?tab=automations'],
    ]);
    expect(within(section).queryByRole('button')).toBeNull();
    expect(within(dialog).queryByText(/Welcome|Tell the team/)).toBeNull();
  });

  it('lists the reminder its own time limit made under When overdue, not again with the automations', async () => {
    renderSheet();
    const dialog = await sheet();
    await fireEvent.click(limit(dialog, '3 days'));

    expect(automationIds()).toContain('reminder-interview');
    expect(overdue(dialog)).toHaveTextContent(tellsTeam);
    expect(within(dialog).getAllByRole('link', { name: 'Tell the team' })).toHaveLength(1);
  });

  it('says one press away what the limit means, that nothing happens to the candidate, and where candidates see the meeting name', async () => {
    renderSheet();
    const dialog = await sheet();
    expect(dialog).not.toHaveTextContent(help);

    await fireEvent.click(within(dialog).getByRole('button', { name: 'About Respond within' }));
    expect(await screen.findByText(help)).toBeInTheDocument();
    await fireEvent.click(within(dialog).getByRole('button', { name: 'About the meeting name' }));
    expect(await screen.findByText(/^Candidates see it in emails and on the page where they book/)).toBeInTheDocument();
  });

  it('has one link to the Automations tab where the stage has none yet, and says which are off', async () => {
    const { unmount } = renderSheet({ flow: { ...workflow, automations: [] } });
    let section = within(await sheet()).getByRole('region', { name: 'Automations' });
    expect(within(section).getAllByRole('link').map((link) => link.textContent?.trim())).toEqual(['Add in Automations']);
    unmount();

    renderSheet({ flow: { ...workflow, automations: workflow.automations.map((automation) => ({ ...automation, enabled: false })) } });
    section = within(await sheet()).getByRole('region', { name: 'Automations' });
    expect(within(section).getAllByRole('link').map((link) => link.textContent?.trim())).toEqual(['Ask for a review, off', 'Edit in Automations']);
  });

  it.each([
    ['applied', ['Timing', 'Scorecard', 'Automations']],
    ['interview', ['Timing', 'Meeting', 'Scorecard', 'Automations']],
    ['offer', ['Timing', 'Scorecard', 'Automations']],
    ['hired', ['Automations']],
  ])('shows the sections the kind of %s has, in one order', async (stageId, sections) => {
    renderSheet({ stageId, flow: { ...workflow, stages: [...workflow.stages.slice(0, 2), { id: 'offer', name: 'Offer', kind: 'Offer' }, ...workflow.stages.slice(2)] } });
    const dialog = await sheet();

    expect(within(dialog).getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent?.trim())).toEqual(sections);
  });

  it('closes the sheet when Edit in Automations is pressed', async () => {
    renderSheet();
    const dialog = await sheet();
    const link = within(dialog).getByRole('link', { name: 'Edit in Automations' });
    link.addEventListener('click', (event) => event.preventDefault());

    await fireEvent.click(link);

    expect(screen.getByTestId('open')).toHaveTextContent('none');
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('shows no time limit and no scorecard for a stage that ends the candidacy', async () => {
    renderSheet({ stageId: 'hired' });
    const dialog = await sheet();

    expect(within(dialog).queryByText('Respond within')).toBeNull();
    expect(within(dialog).queryByRole('radio')).toBeNull();
    expect(within(dialog).queryByText('Scorecard')).toBeNull();
    expect(within(dialog).getByRole('link', { name: 'Add in Automations' })).toBeInTheDocument();
  });

  it('opens the scorecard sheet of this stage from its button', async () => {
    const watched = callbacks();
    renderSheet(watched);
    const dialog = await sheet();

    await fireEvent.click(scorecardButton(dialog, 'Choose'));
    expect(watched.onscorecard).toHaveBeenCalledExactlyOnceWith(1);
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
    expect(overdue(dialog)).toHaveTextContent(tellsTeam);
    expect(automationIds()).toEqual(['welcome', 'ask-review', 'reminder-interview']);
  });

  it('says a week for 7 days', async () => {
    renderSheet();
    const dialog = await sheet();

    await fireEvent.click(limit(dialog, '1 week'));

    expect(interview().slaDays).toBe(7);
    expect(limit(dialog, '1 week')).toBeChecked();
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
    expect(overdue(dialog)).toHaveTextContent(tellsTeam);

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
    expect(within(dialog).queryByRole('button', { name: 'Add a reminder' })).toBeNull();
    expect(automationIds()).not.toContain('reminder-interview');
  });

  it('says Nothing yet and offers Add a reminder when nothing runs once the limit passes', async () => {
    const onreminder = vi.fn();
    const quiet: HiringWorkflow = { ...workflow, stages: workflow.stages.map((stage) => (stage.id === 'interview' ? { ...stage, slaDays: 3 } : stage)) };
    render(WithQueryClient, { props: { component: StageSheetHarness, props: { workflow: structuredClone(quiet), stageId: 'interview', onreminder } } });
    const dialog = await sheet();

    expect(overdue(dialog)).toHaveTextContent('When overdue: Nothing yet.');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Add a reminder' }));
    expect(onreminder).toHaveBeenCalledWith('interview');
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
    expect(overdue(dialog)).toHaveTextContent(tellsTeam);
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

  it('stops following once the kind is set by hand', async () => {
    renderSheet();
    const dialog = await sheet();
    await fireEvent.change(within(dialog).getByRole('combobox', { name: 'Kind' }), { target: { value: 'Offer' } });
    expect(interview().kind).toBe('Offer');

    await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Name' }), { target: { value: 'Trial shift' } });

    expect(interview().name).toBe('Trial shift');
    expect(interview().kind).toBe('Offer');
  });

  it('drops the reminder when the kind is set by hand to one that ends the application', async () => {
    renderSheet();
    const dialog = await sheet();
    await fireEvent.click(limit(dialog, '3 days'));
    expect(automationIds()).toContain('reminder-interview');

    await fireEvent.change(within(dialog).getByRole('combobox', { name: 'Kind' }), { target: { value: 'Rejected' } });

    expect(interview().kind).toBe('Rejected');
    expect(automationIds()).not.toContain('reminder-interview');
  });

  it('never changes the kind of the first stage, which is where candidates arrive', async () => {
    renderSheet({ stageId: 'applied' });
    const dialog = await sheet();

    await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Name' }), { target: { value: 'Offer desk' } });

    expect(stages()[0]).toMatchObject({ name: 'Offer desk', kind: 'Applied' });
  });
});

describe('the meeting name', () => {
  const meetingBox = (dialog: HTMLElement) => within(dialog).getByRole('textbox', { name: 'What the meeting is called' });

  it('asks what the meeting is called on a stage where the team meets a candidate, with interview as the placeholder', async () => {
    renderSheet();
    const dialog = await sheet();

    expect(meetingBox(dialog)).toHaveValue('');
    expect(meetingBox(dialog)).toHaveAttribute('placeholder', 'interview');
    expect(meetingBox(dialog)).toHaveAttribute('maxlength', '40');
  });

  it('sets the name as it is typed, and clears it to nothing so the meeting is an interview again', async () => {
    renderSheet();
    const dialog = await sheet();

    await fireEvent.input(meetingBox(dialog), { target: { value: 'trial shift' } });
    expect(interview().meetingName).toBe('trial shift');
    await fireEvent.input(meetingBox(dialog), { target: { value: '  ' } });

    expect(interview().meetingName).toBeUndefined();
  });

  it('is not asked on Applied, an Offer or a stage that ends the application', async () => {
    renderSheet({ stageId: 'applied' });
    const dialog = await sheet();

    expect(within(dialog).queryByRole('textbox', { name: 'What the meeting is called' })).toBeNull();
  });

  it('is asked again on a stage that has a name already, whatever its kind, so it can be cleared', async () => {
    const flow: HiringWorkflow = { ...workflow, stages: workflow.stages.map((stage) => (stage.id === 'hired' ? { ...stage, meetingName: 'start day' } : stage)) };
    renderSheet({ stageId: 'hired', flow });
    const dialog = await sheet();

    expect(meetingBox(dialog)).toHaveValue('start day');
  });

  it('shows the API refusal under the meeting field and marks that field, not the stage name', async () => {
    renderSheet({ errors: [{ path: 'stages[1].meetingName', message: 'The meeting name of "Interview" is too long. Keep it to 40 characters or fewer.' }] });
    const dialog = await sheet();

    expect(meetingBox(dialog)).toHaveAttribute('aria-invalid', 'true');
    expect(meetingBox(dialog)).toHaveAccessibleDescription(expect.stringContaining('is too long'));
    expect(within(dialog).getByRole('textbox', { name: 'Name' })).not.toHaveAttribute('aria-invalid');
    expect(dialog.querySelector('#stage-meeting-name-errors')).toHaveTextContent(/meeting name of "Interview" is too long/);
    expect(dialog.querySelector('#stage-sheet-errors')).toBeNull();
  });
});

describe('the kind beside the name', () => {
  it('shows the kind without opening anything, following the name in view as it is typed', async () => {
    renderSheet();
    const dialog = await sheet();
    const kind = within(dialog).getByRole('combobox', { name: 'Kind' });
    expect(kind).toHaveValue('Interview');

    await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Name' }), { target: { value: 'Trial shift' } });

    expect(kind).toHaveValue('Assessment');
    expect(within(dialog).queryByRole('button', { name: 'More options' })).toBeNull();
  });

  it('reads Applied as text on the first stage, the one kind allowed there, and stays a choice where another kind came first', async () => {
    const { unmount } = renderSheet({ stageId: 'applied' });
    let dialog = await sheet();
    expect(within(dialog).queryByRole('combobox', { name: 'Kind' })).toBeNull();
    expect(dialog.querySelector('[data-stage-kind]')).toHaveTextContent(/Kind\s*Applied/);
    unmount();

    renderSheet({ flow: { ...workflow, stages: [workflow.stages[1]!, workflow.stages[0]!, ...workflow.stages.slice(2)] } });
    dialog = await sheet();
    expect(within(dialog).getByRole('combobox', { name: 'Kind' })).toHaveValue('Interview');
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

describe('the stage scheduling default', () => {
  const scheduling = () => (stages().find((stage) => stage.id === 'interview') as unknown as { scheduling?: Record<string, unknown> | null }).scheduling;

  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/email-templates': () => Response.json({ templates: [], sendingEnabled: false }),
          'GET /v1/organization/teams/': () => Response.json([{ id: 'team-1', name: 'Kitchen', members: [] }]),
          'GET /v1/team/members': () => Response.json([]),
        }),
      ),
    );
  });

  it('sets how interviews here are offered: the way to pick, a team, the length and the days ahead', async () => {
    renderSheet();
    const dialog = await sheet();
    expect(scheduling()).toBeUndefined();

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Set a default' }));
    expect(scheduling()).toEqual({ option: 'OurTimes', durationMinutes: 30, windowDays: 14 });

    await fireEvent.click(within(dialog).getByRole('radio', { name: /^Tell us when you're free/ }));
    await fireEvent.change(within(dialog).getByRole('combobox', { name: 'Length' }), { target: { value: '45' } });
    expect(scheduling()).toMatchObject({ option: 'TheirTimes', durationMinutes: 45 });

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove the default' }));
    expect(scheduling()).toBeNull();
  });

  it('shows its own errors under it, not under the stage name', async () => {
    renderSheet({ errors: [{ path: 'stages[1].scheduling.windowDays', message: 'How far ahead candidates pick in "Interview" must be between 1 and 60 days.' }], flow: { ...workflow, stages: workflow.stages.map((stage) => (stage.id === 'interview' ? { ...stage, scheduling: { windowDays: 0 } } : stage)) } });
    const dialog = await sheet();

    const section = dialog.querySelector<HTMLElement>('[data-stage-scheduling]')!;
    expect(section).toHaveTextContent('must be between 1 and 60 days');
    await waitFor(() => expect(within(dialog).getAllByText(/must be between 1 and 60 days/)).toHaveLength(1));
  });
});
