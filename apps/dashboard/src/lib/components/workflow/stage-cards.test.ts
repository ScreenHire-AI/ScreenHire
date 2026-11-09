// The Pipeline tab's stage cards: a card per stage with its facts, a plus that adds a stage and opens its sheet, a menu per
// card, and a grip that moves the card left and right (design system A7).
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

async function openMenu(stage: string) {
  await fireEvent.pointerDown(within(stages()).getByRole('button', { name: `Actions for ${stage}` }), { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
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
  it('has a numbered card per stage, in order, in a list named Stages', async () => {
    const { container } = renderCards();

    expect(cards()).toEqual(['Stage 1: Applied', 'Stage 2: Phone screen', 'Stage 3: Interview', 'Stage 4: Hired', 'Stage 5: Not progressing']);
    expect(within(stages()).getByRole('list', { name: 'Stages' })).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it('shows a stage\'s limit, scorecard and count of automations that are on, and nothing a stage does not have', () => {
    renderCards();

    const screening = card('Phone screen');
    expect(screening).toHaveTextContent('3-day limit');
    expect(screening).toHaveTextContent('Scorecard: Phone screen guide');
    // The two that are on. The one that is off is not counted, and neither is the sheet's reminder: the time limit says it.
    expect(screening).toHaveTextContent('2 automations');
    expect(screening).not.toHaveTextContent('3 automations');
    expect(card('Applied')).toHaveTextContent('1 automation');
    expect(card('Applied')).not.toHaveTextContent('automations');
    expect(card('Interview').querySelector('ul')).toBeNull();
  });

  it('counts a reminder a person wrote like any other automation', () => {
    renderCards({
      flow: { ...workflow, automations: workflow.automations.map((automation) => (automation.id === 'reminder-phone-screen' ? { ...automation, id: 'my-reminder' } : automation)) },
    });

    expect(card('Phone screen')).toHaveTextContent('3 automations');
  });

  it('shows no limit or scorecard on a stage that ends the candidacy, whatever it holds', () => {
    renderCards();

    expect(card('Hired')).not.toHaveTextContent('day limit');
    expect(card('Not progressing')).not.toHaveTextContent('Why not');
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

    await fireEvent.click(within(card('Interview')).getByRole('button', { name: 'Interview' }));

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
    expect(cards().slice(-3)).toEqual(['Stage 4: New stage', 'Stage 5: Hired', 'Stage 6: Not progressing']);
  });

  it('gives each added stage an id of its own', async () => {
    renderCards();

    await fireEvent.click(within(stages()).getByRole('button', { name: 'Add a stage after Applied' }));
    await fireEvent.click(within(stages()).getByRole('button', { name: 'Add a stage after Applied' }));

    expect(ids()).toBe('applied,stage_2,stage,phone-screen,interview,hired,rejected');
  });
});

describe('a card\'s menu', () => {
  it('has Edit stage, Add an automation, Choose scorecard and Remove stage on a stage in the middle', async () => {
    renderCards();

    const menu = await openMenu('Phone screen');

    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Edit stage', 'Add an automation', 'Choose scorecard', 'Remove stage']);
  });

  it('leaves Remove stage out of the first stage, which is where candidates arrive', async () => {
    renderCards();

    const menu = await openMenu('Applied');

    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Edit stage', 'Add an automation', 'Choose scorecard']);
  });

  it('leaves Choose scorecard out of a stage that ends the candidacy and has none, and keeps it where it has one', async () => {
    renderCards();

    const hired = await openMenu('Hired');
    expect(within(hired).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Edit stage', 'Add an automation', 'Remove stage']);
    await fireEvent.keyDown(hired, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());

    const rejected = await openMenu('Not progressing');
    expect(within(rejected).getByRole('menuitem', { name: 'Choose scorecard' })).toBeInTheDocument();
  });

  it('opens the sheet, the automations and the scorecard of the stage it was opened on, once the menu has closed', async () => {
    const watched = callbacks();
    renderCards(watched);

    await fireEvent.click(await within(await openMenu('Interview')).findByRole('menuitem', { name: 'Edit stage' }));
    await waitFor(() => expect(watched.onopen).toHaveBeenCalledExactlyOnceWith('interview'));

    await fireEvent.click(await within(await openMenu('Interview')).findByRole('menuitem', { name: 'Add an automation' }));
    await waitFor(() => expect(watched.onautomations).toHaveBeenCalledExactlyOnceWith('interview'));

    await fireEvent.click(await within(await openMenu('Interview')).findByRole('menuitem', { name: 'Choose scorecard' }));
    await waitFor(() => expect(watched.onscorecard).toHaveBeenCalledExactlyOnceWith(2));
  });

  it('removes the stage with its reminder and no other automation, without asking, and puts the focus on the next card', async () => {
    renderCards();

    await fireEvent.click(within(await openMenu('Phone screen')).getByRole('menuitem', { name: 'Remove stage' }));

    await waitFor(() => expect(within(stages()).queryByRole('group', { name: /Phone screen/ })).toBeNull());
    expect(ids()).toBe('applied,interview,hired,rejected');
    expect(screen.getByTestId('automation-ids')).toHaveTextContent('welcome,tag-screen,tag-two,tag-off');
    await waitFor(() => expect(within(stages()).getByRole('button', { name: 'Interview' })).toHaveFocus());
  });

  it('puts the focus on the last card when the last one is removed', async () => {
    renderCards();

    await fireEvent.click(within(await openMenu('Not progressing')).getByRole('menuitem', { name: 'Remove stage' }));

    await waitFor(() => expect(within(stages()).getByRole('button', { name: 'Hired' })).toHaveFocus());
    expect(ids()).toBe('applied,phone-screen,interview,hired');
  });
});

describe('moving a card', () => {
  it('says Move left, Move right, Move to start and Move to end from the grip, leaving out the moves that do not apply', async () => {
    renderCards();

    await fireEvent.click(within(stages()).getByRole('button', { name: 'Reorder Applied' }));
    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim())).toEqual(['Move right', 'Move to end']);
    await fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());

    await fireEvent.click(within(stages()).getByRole('button', { name: 'Reorder Interview' }));
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
