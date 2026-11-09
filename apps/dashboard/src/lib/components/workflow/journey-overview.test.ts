// The Overview tab: a workflow read back as what happens to an applicant, a column per open stage, the automations that
// belong to no stage, then the end stages. The stage name is a button only where the page can open the stage.
import type { HiringWorkflow } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import JourneyOverview from './journey-overview.svelte';

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone-screen', name: 'Phone screen', kind: 'Screening', slaDays: 3, scorecard: { name: 'Phone screen guide', questions: [] } },
    { id: 'interview', name: 'Interview', kind: 'Interview', scorecardTemplateId: 'sc-1' },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: {
    questions: [
      { id: 'right_to_work', type: 'YesNo', label: 'Do you have the right to work here?', required: true },
      { id: 'notice', type: 'ShortText', label: 'Notice period' },
    ],
  },
  automations: [
    { id: 'welcome', name: 'Welcome', trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'SendEmail', emailTemplate: 'application-received' }] },
    { id: 'ask-review', name: 'Ask for a review', trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'AssignReviewer', assigneeUserId: '01a157d4-0000-7000-8000-0000000000aa' }] },
    { id: 'off', name: 'Off', enabled: false, trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'NotifyTeam' }] },
    { id: 'route', name: 'Route', trigger: { type: 'VerificationAssessed' }, conditions: null, actions: [{ type: 'NotifyTeam' }] },
    { id: 'sorry', name: 'Sorry', trigger: { type: 'StageEntered', stageId: 'rejected' }, conditions: null, actions: [{ type: 'SendEmail', emailTemplate: 'application-rejected' }] },
  ],
};

const renderOverview = (props: { onopen?: (id: string) => void; questionsHref?: string; flow?: HiringWorkflow } = {}) => {
  const { flow = workflow, ...rest } = props;
  return render(WithQueryClient, { props: { component: JourneyOverview, props: { workflow: structuredClone(flow), ...rest } } });
};

const columns = () => within(screen.getByRole('list', { name: 'Stages in order' })).getAllByRole('listitem', {}).filter((item) => item.parentElement?.tagName === 'OL');
const column = (name: string) => columns().find((item) => item.textContent?.includes(name))!;

beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/email-templates': () =>
          Response.json({
            templates: [
              { id: 'application-received', name: 'Application received', builtIn: true, edited: false, rejectOnly: false, subject: 's', body: 'b', updatedAt: null, lastUpdated: { at: null, by: 'ScreenHire' }, questionnaireOnly: false, archived: false },
              { id: 'application-rejected', name: 'Not this time', builtIn: true, edited: false, rejectOnly: true, subject: 's', body: 'b', updatedAt: null, lastUpdated: { at: null, by: 'ScreenHire' }, questionnaireOnly: false, archived: false },
            ],
            sendingEnabled: false,
            variables: [],
          }),
        'GET /v1/scorecard-templates': () => Response.json([{ id: 'sc-1', name: 'Interview guide', builtIn: false, questions: 1, ratingQuestions: 1, lastUpdated: { at: null, by: null } }]),
      }),
    ),
  );
});

afterEach(() => vi.unstubAllGlobals());

describe('journey overview', () => {
  it('is a panel named Candidate journey with a column per open stage, numbered, and the end stages apart', async () => {
    const { container } = renderOverview();

    expect(screen.getByRole('region', { name: 'Candidate journey' })).toBeInTheDocument();
    const heads = columns().map((item) => item.textContent?.replace(/\s+/g, ' ').trim() ?? '');
    expect(heads).toHaveLength(3);
    ['1 Applied', '2 Phone screen', '3 Interview'].forEach((head, index) => expect(heads[index]).toMatch(new RegExp('^' + head)));
    expect(screen.getByRole('heading', { name: 'Ends' })).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it('says what each stage has: the question count on the first, the limit, the scorecard and its automations in words', async () => {
    renderOverview({ questionsHref: '?tab=questions' });

    const first = column('Applied');
    expect(within(first).getByRole('link', { name: 'Asks 2 questions' })).toHaveAttribute('href', '?tab=questions');
    expect(first).toHaveTextContent('Sends the "Application received" email');

    const screening = column('Phone screen');
    expect(screening).toHaveTextContent('3-day limit');
    expect(screening).toHaveTextContent('Scorecard: Phone screen guide');
    expect(screening).toHaveTextContent('Asks the team to review');
    // The one that is off is not part of the journey, and no column but the first counts questions.
    expect(screening).not.toHaveTextContent('Tells the team');
    expect(screening).not.toHaveTextContent('Asks 2');

    expect(await within(column('Interview')).findByText('Interview guide')).toBeInTheDocument();
  });

  it('never names the person a step asks to review', () => {
    renderOverview();

    expect(document.body.textContent).not.toMatch(/01a157d4|Avery|Quinn/);
  });

  it('puts the automations that belong to no stage under Any time, and the end stages under Ends with what happens there', async () => {
    renderOverview();

    const anytime = screen.getByRole('heading', { name: 'Any time' }).parentElement!;
    expect(anytime).toHaveTextContent('When the checks finish, tells the team');
    const ends = screen.getByRole('heading', { name: 'Ends' }).parentElement!;
    expect(ends).toHaveTextContent('Hired');
    expect(await within(ends).findByText('Not progressing, sends the "Not this time" email')).toBeInTheDocument();
  });

  it('has no Any time when every automation belongs to a stage', () => {
    renderOverview({ flow: { ...workflow, automations: workflow.automations.filter((automation) => automation.id !== 'route') } });

    expect(screen.queryByRole('heading', { name: 'Any time' })).toBeNull();
  });

  it('has the stage name as the one button that opens the stage, and a plain line for the question count when it has no address', async () => {
    const onopen = vi.fn();
    renderOverview({ onopen });

    await fireEvent.click(within(column('Interview')).getByRole('button', { name: 'Interview' }));

    expect(onopen).toHaveBeenCalledExactlyOnceWith('interview');
    expect(within(column('Applied')).queryByRole('link')).toBeNull();
    expect(column('Applied')).toHaveTextContent('Asks 2 questions');
  });

  it('reads as text, with no buttons, where the page cannot open a stage', async () => {
    const { container } = renderOverview();

    expect(within(screen.getByRole('region', { name: 'Candidate journey' })).queryByRole('button')).toBeNull();
    expect(column('Phone screen')).toHaveTextContent('Phone screen');
    await expectNoAxeViolations(container);
  });

  it('puts a zap beside the automations only', () => {
    renderOverview();

    // Icons are decorative (aria-hidden), one per automation line and none on the limit, scorecard or question count.
    const lines = (item: HTMLElement) => Array.from(item.querySelectorAll('li')).filter((line) => line.querySelector('svg'));
    expect(lines(column('Phone screen')).map((line) => line.textContent?.trim())).toEqual(['Asks the team to review']);
    expect(lines(column('Applied'))).toHaveLength(1);
  });

  it('lists more than five stages in order, as a vertical list', () => {
    const many: HiringWorkflow = {
      ...workflow,
      stages: [
        { id: 'applied', name: 'Applied', kind: 'Applied' },
        ...['One', 'Two', 'Three', 'Four', 'Five', 'Six'].map((name) => ({ id: name.toLowerCase(), name, kind: 'Custom' as const })),
        { id: 'hired', name: 'Hired', kind: 'Hired' },
      ],
      automations: [],
    };
    renderOverview({ flow: many });

    expect(columns()).toHaveLength(7);
    const heads = columns().map((item) => item.textContent?.replace(/\s+/g, ' ').trim() ?? '');
    ['1 Applied', '2 One', '3 Two', '4 Three', '5 Four', '6 Five', '7 Six'].forEach((head, index) => expect(heads[index]).toMatch(new RegExp('^' + head)));
  });
});
