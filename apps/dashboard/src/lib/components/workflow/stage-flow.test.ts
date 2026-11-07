// A workflow's stages as the read-only view draws them (design system section 11, Stage flow). jsdom has no layout, so
// what these check is the markup both layouts share: the order, the facts and what a stage with nothing attached says.
import type { HiringWorkflow, WorkflowStage } from '@screenhire/api-client';
import { render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
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
];

const renderFlow = (list: WorkflowStage[] = stages) =>
  render(WithQueryClient, { props: { component: StageFlow, props: { stages: list, automations } } });

const text = (element: Element) => element.textContent?.replace(/\s+/g, ' ').trim();

describe('stage flow', () => {
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

  it('names a scorecard from the library once the library loads, with its word for screen readers only', async () => {
    renderFlow([{ id: 'screen', name: 'Screen', kind: 'Screening', scorecardTemplateId: 'phone-screen' }]);

    const fact = within(screen.getByRole('list', { name: 'Stages in order' })).getByText('Scorecard:');
    expect(fact).toHaveClass('sr-only');
    expect(text(fact.parentElement!)).toBe('Scorecard: a scorecard from the library');
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
