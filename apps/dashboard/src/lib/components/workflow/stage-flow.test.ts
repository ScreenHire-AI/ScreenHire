// A workflow's stages as the read-only view draws them (design system section 11, Stage flow). jsdom has no layout, so
// what these check is the markup both layouts share: the order, the facts and what a stage with nothing attached says.
import type { HiringWorkflow, WorkflowStage } from '@screenhire/api-client';
import { render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import StageFlow from './stage-flow.svelte';

const stages: WorkflowStage[] = [
  { id: 'applied', name: 'Applied', kind: 'Applied' },
  { id: 'phone-screen', name: 'Phone screen', kind: 'Screening', slaDays: 3, scorecard: { name: 'Phone screen scorecard', questions: [] } },
  { id: 'interview', name: 'Interview', kind: 'Interview', slaDays: 5 },
  { id: 'offer', name: 'Offer', kind: 'Offer' },
];

const automations: HiringWorkflow['automations'] = [
  { id: 'a', name: 'Tag screened', trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'AddTag', tag: 'screened' }] },
  { id: 'b', name: 'Tell the team', trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'NotifyTeam', note: 'Screened' }] },
  { id: 'c', name: 'Tag interviewed', trigger: { type: 'StageEntered', stageId: 'interview' }, conditions: null, actions: [{ type: 'AddTag', tag: 'interviewed' }] },
  // Fires on no one stage, so no stage counts it.
  { id: 'd', name: 'Welcome', trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'AddTag', tag: 'new' }] },
  // Off, so they fire on nothing and their stages do not count them.
  { id: 'e', name: 'Paused', enabled: false, trigger: { type: 'StageEntered', stageId: 'interview' }, conditions: null, actions: [{ type: 'AddTag', tag: 'paused' }] },
  { id: 'f', name: 'Paused too', enabled: false, trigger: { type: 'StageEntered', stageId: 'offer' }, conditions: null, actions: [{ type: 'AddTag', tag: 'paused' }] },
];

const renderFlow = (list: WorkflowStage[] = stages) =>
  render(WithQueryClient, { props: { component: StageFlow, props: { stages: list, automations } } });

const text = (element: Element) => element.textContent?.replace(/\s+/g, ' ').trim();

describe('stage flow', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('lists the stages in order, each with its number and name', () => {
    renderFlow();

    const list = screen.getByRole('list', { name: 'Stages in order' });
    const items = Array.from(list.children);
    expect(items.map((item) => item.querySelector('.font-semibold')?.textContent)).toEqual(['Applied', 'Phone screen', 'Interview', 'Offer']);
    items.forEach((item, index) => expect(text(item)).toMatch(new RegExp(`^${index + 1} `)));
  });

  it('shows a stage with nothing attached as its name alone', () => {
    renderFlow();

    const applied = screen.getByRole('list', { name: 'Stages in order' }).children[0]!;
    expect(text(applied)).toBe('1 Applied');
    expect(within(applied as HTMLElement).queryByRole('list')).toBeNull();
  });

  it('shows what is attached as one short fact each, in one order: scorecard, time limit, automations', () => {
    renderFlow();

    const [, phone, interview] = Array.from(screen.getByRole('list', { name: 'Stages in order' }).children) as HTMLElement[];
    const facts = (stage: HTMLElement) => within(within(stage).getByRole('list')).getAllByRole('listitem').map(text);

    expect(facts(phone!)).toEqual(['Scorecard: Phone screen scorecard', '3-day limit', '2 automations']);
    expect(facts(interview!)).toEqual(['5-day limit', '1 automation']);
  });

  it('does not count an automation that is off, and a stage whose only automation is off shows its name alone', () => {
    renderFlow();

    const list = screen.getByRole('list', { name: 'Stages in order' });
    expect(text(list.children[2]!)).toBe('3 Interview 5-day limit 1 automation');
    expect(text(list.children[3]!)).toBe('4 Offer');
  });

  it('names a scorecard from the library once the library loads, with its word for screen readers only', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/scorecard-templates': () =>
            Response.json([{ id: 'phone-screen', name: 'Phone screen', builtIn: true, questions: 2, ratingQuestions: 2, lastUpdated: { at: null, by: 'ScreenHire' } }]),
        }),
      ),
    );
    renderFlow([{ id: 'screen', name: 'Screen', kind: 'Screening', scorecardTemplateId: 'phone-screen' }]);

    const list = screen.getByRole('list', { name: 'Stages in order' });
    expect(await within(list).findByText('Phone screen')).toBeInTheDocument();
    const word = within(list).getByText('Scorecard:');
    expect(word).toHaveClass('sr-only');
    expect(text(word.parentElement!)).toBe('Scorecard: Phone screen');
  });

  it('says a scorecard is from the library while the library has no name for it', () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/scorecard-templates': () => new Response(null, { status: 500 }) })));
    renderFlow([{ id: 'screen', name: 'Screen', kind: 'Screening', scorecardTemplateId: 'phone-screen' }]);

    const word = within(screen.getByRole('list', { name: 'Stages in order' })).getByText('Scorecard:');
    expect(text(word.parentElement!)).toBe('Scorecard: a scorecard from the library');
  });

  it('gives the list its length for the layout, so the choice between a row and a list needs no measuring', () => {
    renderFlow();
    expect(screen.getByRole('list', { name: 'Stages in order' }).style.getPropertyValue('--n')).toBe('4');
  });

  it('has no accessibility violations', async () => {
    const { container } = renderFlow();
    await expectNoAxeViolations(container);
  });
});
