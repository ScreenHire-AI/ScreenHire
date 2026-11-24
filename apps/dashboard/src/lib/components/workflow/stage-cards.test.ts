// The Pipeline tab's stage cards: a card per open stage with one header row and its chips, a plus that adds a stage and opens
// its sheet, a grip that moves the card left and right (design system A7), then the Any stage and Ends rows.
import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub } from '../../../test-support/fixtures.js';
import StageCardsHarness from '../../../test-support/stage-cards-harness.svelte';
import WithQueryClient from '../../../test-support/with-query-client.svelte';

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone-screen', name: 'Phone screen', kind: 'Screening', slaDays: 3, scorecard: { name: 'Phone screen guide', questions: [] } },
    { id: 'interview', name: 'Interview', kind: 'Interview' },
    { id: 'hired', name: 'Hired', kind: 'Hired', slaDays: 5 },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected', scorecard: { name: 'Why not', questions: [] } },
  ],
  questionnaire: { questions: [] },
  automations: [
    { id: 'welcome', name: 'Welcome', trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'NotifyTeam' }] },
    { id: 'tag-screen', name: 'Tag screened', trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'AddTag', tag: 'screened' }] },
    { id: 'tag-two', name: 'Tag again', trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'AddTag', tag: 'again' }] },
    { id: 'tag-off', name: 'Tag off', enabled: false, trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'AddTag', tag: 'off' }] },
    { id: 'reminder-phone-screen', name: 'Remind', trigger: { type: 'SlaBreached', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'NotifyTeam' }] },
  ],
};

const callbacks = () => ({ onopen: vi.fn(), onscorecard: vi.fn(), onautomations: vi.fn() });
const renderCards = (props: { errors?: DefinitionError[]; flow?: HiringWorkflow } & Partial<ReturnType<typeof callbacks>> = {}) => {
  const { flow = workflow, ...rest } = props;
  return render(WithQueryClient, { props: { component: StageCardsHarness, props: { workflow: structuredClone(flow), ...rest } } });
};

const stages = () => screen.getByRole('region', { name: 'Stages' });
const cards = () => within(stages()).getAllByRole('group', { name: /^Stage / }).map((card) => card.getAttribute('aria-label'));
const ids = () => screen.getByTestId('stage-ids').textContent;
const card = (name: string) => within(stages()).getByRole('group', { name: new RegExp(`^Stage \\d+: ${name}$`) });
const ends = () => within(stages()).getByRole('region', { name: 'Ends' });
/** A card's chips, as their accessible names say them. */
const chips = (name: string) =>
  within(card(name))
    .queryAllByRole('button')
    .filter((button) => button.hasAttribute('data-chip'))
    .map((button) => button.getAttribute('aria-label') ?? button.textContent?.trim());

/** Opens a chip's list and returns it. */
async function openChip(name: string, chip: RegExp) {
  await fireEvent.click(within(card(name)).getByRole('button', { name: chip }));
  return screen.findByRole('dialog', { name: chip });
}

beforeEach(() => {
  // jsdom has no Web Animations, and a card entering or leaving the list's `animate:flip` asks for its running ones.
  Element.prototype.getAnimations = () => [];
  vi.stubGlobal('fetch', vi.fn(apiStub({})));
});

afterEach(() => {
  delete (Element.prototype as Partial<Element>).getAnimations;
  vi.unstubAllGlobals();
});

describe('stage cards', () => {
  it('has a numbered card per open stage, in order, in a list named Stages, and the stages that end a candidacy as buttons under Ends', async () => {
    const { container } = renderCards();

    expect(cards()).toEqual(['Stage 1: Applied', 'Stage 2: Phone screen', 'Stage 3: Interview']);
    expect(within(stages()).getByRole('list', { name: 'Stages' })).toBeInTheDocument();
    expect(within(ends()).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Hired', 'Not progressing']);
    await expectNoAxeViolations(container);
  });

  it('counts what happens at a stage in one row of chips, never a sentence: the time limit, the automations that are on, and the scorecard', () => {
    renderCards();

    // The one that is off is not counted, and neither is the sheet's reminder: Respond within says it.
    expect(chips('Phone screen')).toEqual(['Respond within 3 days', '2 automations at Phone screen', 'Scorecard']);
    expect(chips('Applied')).toEqual(['1 automation at Applied']);
    expect(card('Phone screen')).not.toHaveTextContent('Tag screened');
  });

  it('says "Nothing happens automatically" on a stage with nothing on it, instead of an empty row', () => {
    renderCards();

    expect(card('Interview')).toHaveTextContent('Nothing happens automatically');
    expect(within(card('Interview')).queryByRole('list', { name: /What happens/ })).toBeNull();
  });

  it('goes to the stage\'s automations from its automations chip, naming the ones it counted, and lists none of them here', async () => {
    const watched = callbacks();
    renderCards(watched);

    await fireEvent.click(within(card('Phone screen')).getByRole('button', { name: '2 automations at Phone screen' }));

    expect(watched.onautomations).toHaveBeenCalledExactlyOnceWith('phone-screen', ['tag-screen', 'tag-two']);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('says the scorecard chip\'s word, so no chip is an icon alone', () => {
    renderCards();

    const chip = within(card('Phone screen')).getByRole('button', { name: 'Scorecard' });
    // The name is the word shown, not a label over a word only some screens get.
    expect(chip).not.toHaveAttribute('aria-label');
    expect(chip.querySelector('[aria-hidden]:not(svg)')).toBeNull();
  });

  it('counts the questions the first stage asks and opens to each form, linking to it on Forms', async () => {
    renderCards({ flow: { ...workflow, questionnaire: { questions: [{ id: 'q1', label: 'Right to work', type: 'YesNo', required: true }] } } as HiringWorkflow });

    expect(chips('Applied')).toContain('1 question at Applied');
    const list = await openChip('Applied', /1 question/);
    expect(within(list).getByRole('link', { name: 'Application form' })).toHaveAttribute('href', '?tab=forms&form=apply');
  });

  it('counts the emails a stage sends and lists them by template name, each linking to the automation that sends it', async () => {
    renderCards({
      flow: {
        ...workflow,
        automations: [
          ...workflow.automations,
          { id: 'invite', name: 'Invite them', trigger: { type: 'StageEntered', stageId: 'interview' }, conditions: null, actions: [{ type: 'SendEmail', emailTemplate: 'interview-invitation' }] },
        ],
      },
    });

    expect(chips('Interview')).toEqual(['1 email at Interview']);
    const list = await openChip('Interview', /1 email/);
    expect(within(list).getByRole('link')).toHaveAttribute('href', '?tab=automations&show=invite');
  });

  it('opens the stage from its time limit and the scorecard sheet from its scorecard', async () => {
    const watched = callbacks();
    renderCards(watched);

    await fireEvent.click(within(card('Phone screen')).getByRole('button', { name: 'Respond within 3 days' }));
    await fireEvent.click(within(card('Phone screen')).getByRole('button', { name: 'Scorecard' }));

    expect(watched.onopen).toHaveBeenCalledExactlyOnceWith('phone-screen');
    expect(watched.onscorecard).toHaveBeenCalledExactlyOnceWith(1);
  });

  it('counts a reminder a person wrote like any other automation', () => {
    renderCards({
      flow: { ...workflow, automations: workflow.automations.map((automation) => (automation.id === 'reminder-phone-screen' ? { ...automation, id: 'my-reminder' } : automation)) },
    });

    expect(chips('Phone screen')).toContain('3 automations at Phone screen');
  });

  it('shows no time limit or scorecard on a stage that ends the candidacy, whatever it holds, and opens its sheet when pressed', async () => {
    const watched = callbacks();
    renderCards(watched);

    const hired = within(ends()).getByRole('button', { name: /^Edit Hired/ });
    expect(hired).not.toHaveTextContent('Respond within');
    expect(within(ends()).getByRole('button', { name: /^Edit Not progressing/ })).not.toHaveTextContent('Scorecard');
    await fireEvent.click(hired);
    expect(watched.onopen).toHaveBeenCalledExactlyOnceWith('hired');
  });

  it('puts the automations that belong to no stage in one Any stage row', async () => {
    const watched = callbacks();
    renderCards({
      ...watched,
      flow: { ...workflow, automations: [...workflow.automations, { id: 'by-hand', name: 'By hand', trigger: { type: 'Manual' }, conditions: null, actions: [{ type: 'AddTag', tag: 'vip' }] }] },
    });

    const any = within(stages()).getByRole('region', { name: 'Any stage' });
    await fireEvent.click(within(any).getByRole('button', { name: /1 automation/ }));
    expect(watched.onautomations).toHaveBeenCalledExactlyOnceWith(null, ['by-hand']);
  });

  it('has no Any stage row when every automation has a stage', () => {
    renderCards();

    expect(within(stages()).queryByRole('region', { name: 'Any stage' })).toBeNull();
  });

  it('shows what is wrong with a stage on its card, and what is wrong with the stages above them', () => {
    renderCards({
      errors: [
        { path: 'stages[2].name', message: 'Give the stage a name.' },
        { path: 'stages', message: 'A workflow needs a hired stage.' },
      ],
    });

    expect(card('Interview')).toHaveTextContent('Give the stage a name.');
    expect(card('Phone screen')).not.toHaveTextContent('Give the stage a name.');
    expect(stages()).toHaveTextContent('A workflow needs a hired stage.');
  });

  it('opens the stage when its name is pressed', async () => {
    const watched = callbacks();
    renderCards(watched);
    const name = within(card('Interview')).getByRole('button', { name: 'Interview' });

    await fireEvent.click(name);

    expect(watched.onopen).toHaveBeenCalledExactlyOnceWith('interview');
  });
});

describe('adding a stage', () => {
  it('has a plus after every stage that does not end the candidacy, and none after the ones that do', () => {
    renderCards();

    expect(within(stages()).getAllByRole('button', { name: /^Add a stage after / }).map((button) => button.getAttribute('aria-label'))).toEqual([
      'Add a stage after Applied',
      'Add a stage after Phone screen',
      'Add a stage after Interview',
    ]);
    expect(within(stages()).queryByRole('button', { name: 'Add a stage after Hired' })).toBeNull();
    expect(within(stages()).queryByRole('button', { name: 'Add a stage after Not progressing' })).toBeNull();
  });

  it('puts the stage right after the card it was pressed on, and opens its sheet to name it', async () => {
    const watched = callbacks();
    renderCards(watched);

    await fireEvent.click(within(stages()).getByRole('button', { name: 'Add a stage after Phone screen' }));

    expect(ids()).toBe('applied,phone-screen,stage,interview,hired,rejected');
    expect(watched.onopen).toHaveBeenCalledExactlyOnceWith('stage');
    expect(card('New stage')).toBeInTheDocument();
  });

  it('puts a stage added after the last open stage before the stages that end the candidacy', async () => {
    const watched = callbacks();
    renderCards(watched);

    await fireEvent.click(within(stages()).getByRole('button', { name: 'Add a stage after Interview' }));

    expect(ids()).toBe('applied,phone-screen,interview,stage,hired,rejected');
    expect(cards().at(-1)).toBe('Stage 4: New stage');
  });

  it('gives each added stage an id of its own', async () => {
    renderCards();

    await fireEvent.click(within(stages()).getByRole('button', { name: 'Add a stage after Applied' }));
    await fireEvent.click(within(stages()).getByRole('button', { name: 'Add a stage after Applied' }));

    expect(ids()).toBe('applied,stage_2,stage,phone-screen,interview,hired,rejected');
  });
});

describe('a card\'s header', () => {
  it('is one row: the grip, the number and the name with its pencil', () => {
    renderCards();
    const header = card('Phone screen').querySelector<HTMLElement>('[data-stage-header]')!;
    const name = within(header).getByRole('button', { name: 'Phone screen' });

    expect([...header.children].map((part) => part.textContent?.trim())).toEqual(['', '2', 'Phone screen']);
    expect(within(header).getByRole('button', { name: 'Reorder Phone screen' })).toBe(header.children[0]!.firstElementChild);
    expect(name.querySelector('svg')).not.toBeNull();
  });

  it('has no menu: the sheet removes the stage and picks its scorecard, and automations are on their own tab', () => {
    renderCards();

    expect(within(stages()).queryByRole('button', { name: /^(Actions|More) for / })).toBeNull();
    expect(within(stages()).queryByRole('button', { name: /^(Remove|Add an automation|Choose)/ })).toBeNull();
  });
});

describe('moving a card', () => {
  it('says Move left, Move right, Move to start and Move to end from the grip, leaving out the moves that do not apply', async () => {
    renderCards();

    await fireEvent.click(within(stages()).getByRole('button', { name: 'Reorder Applied' }));
    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim())).toEqual(['Move right', 'Move to end']);
    await fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());

    await fireEvent.click(within(stages()).getByRole('button', { name: 'Reorder Phone screen' }));
    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim())).toEqual(['Move left', 'Move right', 'Move to start', 'Move to end']);
  });

  it('moves the card and renumbers the cards', async () => {
    renderCards();

    await fireEvent.click(within(stages()).getByRole('button', { name: 'Reorder Interview' }));
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Move to start' }));

    expect(ids()).toBe('interview,applied,phone-screen,hired,rejected');
    expect(cards()[0]).toBe('Stage 1: Interview');
    expect(cards()[1]).toBe('Stage 2: Applied');
  });
});
