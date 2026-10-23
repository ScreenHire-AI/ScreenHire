// A workflow as the builder shows it and as the read-only view reads it (design system section 7), and the validation
// result beside its title.
import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
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

  it('reads the questionnaire as content, beside the candidate preview', async () => {
    const { container } = renderViews('summary', 'questionnaire');

    const pane = screen.getByRole('tabpanel', { name: 'Questionnaire' });
    expect(within(pane).getByRole('region', { name: 'AI use while applying' })).toHaveTextContent('Allowed with disclosure');
    const questions = within(pane).getByRole('region', { name: 'Screening questions' });
    expect(controls(questions)).toHaveLength(0);
    expect(questions).toHaveTextContent('Do you have the right to work here?');
    expect(within(questions).getByText('Knockout if no')).toHaveAttribute('data-slot', 'badge');
    expect(within(questions).getByText('Required')).toHaveAttribute('data-slot', 'badge');
    // The candidate's form is a preview to try, not the template's data.
    expect(within(pane).getByRole('region', { name: 'Candidate preview' })).toBeInTheDocument();
    await expectNoAxeViolations(container);
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

  it('draws the graph without Edit buttons', () => {
    renderViews('summary', 'graph');

    const pane = screen.getByRole('tabpanel', { name: 'Graph' });
    expect(within(pane).getByRole('region', { name: 'Stages and automations' })).toBeInTheDocument();
    expect(within(pane).queryByRole('button', { name: /^Edit / })).toBeNull();
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

  it('edits stages as rows in one panel, with labelled icon buttons and no move past either end', async () => {
    const { container } = renderViews('editor');

    const stages = screen.getByRole('region', { name: 'Stages' });
    expect(within(stages).getAllByRole('spinbutton', { name: 'Time limit (days)' })).toHaveLength(4);
    expect(within(stages).queryByRole('button', { name: 'Move Applied earlier' })).toBeNull();
    expect(within(stages).getByRole('button', { name: 'Move Applied later' })).toBeInTheDocument();
    expect(within(stages).queryByRole('button', { name: 'Move Not progressing later' })).toBeNull();
    // No card inside the panel (S4): stages are rows.
    expect(stages.querySelectorAll('[data-slot="panel"]')).toHaveLength(0);

    await fireEvent.click(within(stages).getByRole('button', { name: 'Move Applied later' }));
    expect(within(stages).getAllByRole('listitem', { name: /^Stage / })[0]).toHaveAccessibleName('Stage 1: Phone screen');

    expect(screen.getByRole('region', { name: 'Whole workflow' })).toHaveTextContent('When an application arrives: notify the team. (off)');
    await expectNoAxeViolations(container);
  });

  it('gives a review stage a scorecard of job-related criteria in a sheet, and an end stage none', async () => {
    const { container } = renderViews('editor');
    const stages = screen.getByRole('region', { name: 'Stages' });

    expect(within(stages).queryByRole('button', { name: 'Scorecard for Hired' })).toBeNull();
    await fireEvent.click(within(stages).getByRole('button', { name: 'Scorecard for Phone screen' }));
    const sheet = await screen.findByRole('dialog', { name: 'Scorecard: Phone screen' });
    expect(sheet).toHaveTextContent('Never personality, emotion or honesty.');
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Add scorecard' }));

    const criteria = within(sheet).getByRole('list', { name: 'Criteria' });
    expect(within(criteria).getAllByRole('textbox', { name: /^Criterion / }).map((input) => (input as HTMLInputElement).value)).toEqual([
      "Meets the role's requirements",
      'Relevant experience',
    ]);
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Add criterion' }));
    expect(within(criteria).getAllByRole('listitem')).toHaveLength(3);
    expect(within(stages).getByText('Scorecard: 3 criteria and a recommendation')).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });
});
