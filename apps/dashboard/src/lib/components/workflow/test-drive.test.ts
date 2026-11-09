// The Test tab: four sample candidates built from the template, run through the simulate endpoint a moment after each
// change, and what happens to each in a few plain steps. While a problem blocks the workflow it shows only the notice.
import type { HiringWorkflow } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub } from '../../../test-support/fixtures.js';
import TestDriveHarness from '../../../test-support/test-drive-harness.svelte';
import WithQueryClient from '../../../test-support/with-query-client.svelte';

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone-screen', name: 'Phone screen', kind: 'Screening', slaDays: 3 },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: {
    questions: [{ id: 'right_to_work', type: 'YesNo', label: 'Do you have the right to work here?', required: true, knockout: { disqualifyingAnswer: false } }],
  },
  verificationPolicy: { aiUse: 'AllowedWithDisclosure' },
  automations: [
    {
      id: 'queue-review',
      name: 'Queue for review',
      trigger: { type: 'ApplicationSubmitted' },
      conditions: { match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }] },
      actions: [{ type: 'QueueForRejectionReview', reason: 'No right to work' }],
    },
    {
      id: 'route',
      name: 'Route for review',
      trigger: { type: 'VerificationAssessed' },
      conditions: { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'EqualTo', value: 'VerificationRecommended' }] },
      actions: [{ type: 'AssignReviewer', assigneeUserId: '01a157d4-0000-7000-8000-0000000000aa' }],
    },
    { id: 'welcome', name: 'Welcome', trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'NotifyTeam', note: 'New' }] },
  ],
};

type Body = { trigger: { type: string }; facts: { answers: { questionId: string; values: string[] }[]; verificationAuthenticity: string | null }; accommodationRequested: boolean };

// What the evaluator would answer for each sample, from its request. The test decides; the component only words it.
function answerFor(body: Body) {
  const automation = (id: string, name: string, outcome: string, actions: unknown[]) => ({ automationId: id, name, outcome, rules: [], actions, reason: null });
  if (body.trigger.type === 'VerificationAssessed') {
    return { automations: body.facts.verificationAuthenticity === 'VerificationRecommended' ? [automation('route', 'Route for review', 'Matched', [{ type: 'AssignReviewer', assigneeUserId: '01a157d4-0000-7000-8000-0000000000aa' }])] : [] };
  }
  const fails = body.facts.answers.some((answer) => answer.questionId === 'right_to_work' && answer.values.includes('no'));
  return {
    automations: [
      automation('queue-review', 'Queue for review', fails ? 'Matched' : 'NotMatched', fails ? [{ type: 'QueueForRejectionReview' }] : []),
      automation('welcome', 'Welcome', 'Matched', [{ type: 'NotifyTeam' }]),
    ],
  };
}

function stubApi(answer: (body: Body) => Response = (body) => Response.json(answerFor(body))) {
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/email-templates': () => Response.json({ templates: [], sendingEnabled: false }),
      'POST /v1/workflows/simulate': (init) => answer(JSON.parse(String(init?.body))),
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

const sent = (fetchMock: ReturnType<typeof stubApi>): Body[] =>
  fetchMock.mock.calls.filter(([url]) => url === '/v1/workflows/simulate').map(([, init]) => JSON.parse(String(init?.body)));

const renderDrive = (props: Record<string, unknown> = {}, flow: HiringWorkflow = workflow) =>
  render(WithQueryClient, { props: { component: TestDriveHarness, props: { workflow: structuredClone(flow), ...props } } });

const sample = (id: string) => document.querySelector<HTMLElement>(`[data-sample="${id}"]`)!;
const blankTag = (flow: HiringWorkflow = workflow): HiringWorkflow => ({
  ...flow,
  automations: [...flow.automations, { id: 'tag-screened', name: 'Tag screened', trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'AddTag', tag: '' }] }],
});

afterEach(() => vi.unstubAllGlobals());

describe('test drive samples', () => {
  it('runs one simulate call per sample once the tab opens, and shows each with its name, steps in words and a note', async () => {
    const fetchMock = stubApi();
    const { container } = renderDrive();

    await screen.findByRole('heading', { level: 3, name: 'Meets every requirement' });
    expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual([
      'Meets every requirement',
      'Fails a knockout',
      'Checks suggest a review',
      'Asked for an accommodation',
    ]);
    expect(sent(fetchMock)).toHaveLength(4);
    expect(sent(fetchMock).map((body) => [body.trigger.type, body.accommodationRequested])).toEqual([
      ['ApplicationSubmitted', false],
      ['ApplicationSubmitted', false],
      ['VerificationAssessed', false],
      ['ApplicationSubmitted', true],
    ]);

    expect(within(sample('meets')).getAllByRole('listitem').map((item) => item.textContent?.trim())).toEqual(['1Tells the team']);
    expect(sample('meets')).toHaveTextContent('Then a person moves them on');
    expect(sample('fails')).toHaveTextContent("Goes to the team's review list. Nobody is turned down yet");
    expect(sample('signals')).toHaveTextContent('Asks the team to review');
    expect(sample('accommodation')).toHaveTextContent('No steps skipped');
    await expectNoAxeViolations(container);
  });

  it('names no person in a step, and shows no score, percentage or reject control', async () => {
    stubApi();
    renderDrive();
    await screen.findByRole('heading', { level: 3, name: 'Checks suggest a review' });

    const text = screen.getByRole('region', { name: 'What happens to these candidates' }).textContent ?? '';
    expect(text).not.toMatch(/01a157d4|Avery|Quinn/);
    expect(text).not.toMatch(/\d+\s?%|score/i);
    expect(screen.queryByRole('button', { name: /reject/i })).toBeNull();
  });

  it('says a check never turns anyone down, once, under the list', async () => {
    stubApi();
    renderDrive();
    await screen.findByRole('heading', { level: 3, name: 'Checks suggest a review' });

    expect(screen.getAllByText('A check never turns anyone down.')).toHaveLength(1);
  });

  it('leaves that line out when no automation reads the checks', async () => {
    stubApi();
    renderDrive({}, { ...workflow, automations: workflow.automations.filter((automation) => automation.id !== 'route') });
    await screen.findByRole('heading', { level: 3, name: 'Meets every requirement' });

    expect(screen.queryByText('A check never turns anyone down.')).toBeNull();
    expect(screen.queryByRole('heading', { level: 3, name: 'Checks suggest a review' })).toBeNull();
  });

  it('shows a sample\'s moment only when it differs from the sample above it', async () => {
    stubApi();
    renderDrive();
    await screen.findByRole('heading', { level: 3, name: 'Checks suggest a review' });

    expect(within(sample('meets')).getByText('When they apply')).toBeInTheDocument();
    expect(within(sample('fails')).queryByText('When they apply')).toBeNull();
    expect(within(sample('signals')).getByText('When the checks finish')).toBeInTheDocument();
    expect(within(sample('accommodation')).getByText('When they apply')).toBeInTheDocument();
  });

  it('runs again after a change, a moment later, with the workflow as it is then', async () => {
    const fetchMock = stubApi();
    renderDrive();
    await waitFor(() => expect(sent(fetchMock)).toHaveLength(4));

    await fireEvent.click(screen.getByRole('button', { name: 'Rename a stage' }));

    await waitFor(() => expect(sent(fetchMock)).toHaveLength(8));
    expect(sent(fetchMock).slice(4).every((body) => (body as unknown as { workflow: HiringWorkflow }).workflow.stages[1]!.name === 'Renamed')).toBe(true);
  });

  it('runs once for changes that follow each other inside the delay', async () => {
    vi.useFakeTimers();
    try {
      const fetchMock = stubApi();
      renderDrive({ delayMs: 400 });

      await vi.advanceTimersByTimeAsync(300);
      expect(sent(fetchMock)).toHaveLength(0);
      await fireEvent.click(screen.getByRole('button', { name: 'Rename a stage' }));
      await vi.advanceTimersByTimeAsync(300);
      // The change restarted the wait, so nothing has run 600 ms in.
      expect(sent(fetchMock)).toHaveLength(0);

      await vi.advanceTimersByTimeAsync(200);
      await vi.waitFor(() => expect(sent(fetchMock)).toHaveLength(4));
      expect((sent(fetchMock)[0] as unknown as { workflow: HiringWorkflow }).workflow.stages[1]!.name).toBe('Renamed');
    } finally {
      vi.useRealTimers();
    }
  });

  it('says it could not run the samples when the API fails, without a stack of detail', async () => {
    stubApi(() => new Response(null, { status: 500 }));
    renderDrive();

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not run the samples');
  });
});

describe('show in the map', () => {
  it('is a link to the Automations tab for each sample that ran something, and hands the result to the page', async () => {
    stubApi();
    const onshow = vi.fn();
    renderDrive({ onshow });

    const link = await screen.findByRole('link', { name: 'Show in the map: Meets every requirement' });
    expect(link).toHaveAttribute('href', '?tab=automations');
    await fireEvent.click(link);

    expect(onshow).toHaveBeenCalledTimes(1);
    expect(onshow.mock.calls[0]![0].automations.map((automation: { automationId: string }) => automation.automationId)).toEqual(['queue-review', 'welcome']);
    // The sample with the checks answers with one automation, and the one with none would offer nothing to show.
    expect(within(sample('signals')).getByRole('link', { name: 'Show in the map: Checks suggest a review' })).toBeInTheDocument();
  });

  it('is not offered where the page cannot show a path', async () => {
    stubApi();
    renderDrive();
    await screen.findByRole('heading', { level: 3, name: 'Meets every requirement' });

    expect(screen.queryByRole('link', { name: /^Show in the map/ })).toBeNull();
  });
});

describe('paused by a problem', () => {
  it('shows only the notice, with the first message and how many things to fix, and runs nothing', async () => {
    vi.useFakeTimers();
    try {
      const fetchMock = stubApi();
      renderDrive({ delayMs: 50 }, blankTag());

      // Well past the delay: a blocked workflow never reaches the endpoint.
      await vi.advanceTimersByTimeAsync(500);
      expect(sent(fetchMock)).toHaveLength(0);
    } finally {
      vi.useRealTimers();
    }
    expect(screen.queryByRole('heading', { level: 3 })).toBeNull();
  });

  it('shows the first message and how many things to fix in the notice', async () => {
    stubApi();
    renderDrive({}, blankTag());

    const notice = await screen.findByRole('status');
    expect(notice).toHaveTextContent('Paused until 1 thing is fixed');
    expect(notice).toHaveTextContent('The tag in "Tag screened" has no name. Type one, or remove that step.');
    expect(notice).toHaveTextContent('Testing and publishing resume after the fix.');
    expect(screen.queryByRole('heading', { level: 3 })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Try your own candidate' })).toBeNull();
  });

  it('counts the things when there is more than one', async () => {
    stubApi();
    renderDrive({ extra: [{ path: 'stages', message: 'A workflow needs a hired stage.' }] }, blankTag());

    expect(await screen.findByText('Paused until 2 things are fixed')).toBeInTheDocument();
  });

  it('fixes a blank Add a tag step in place: type the tag, add it, and the samples run', async () => {
    const fetchMock = stubApi();
    renderDrive({}, blankTag());
    const notice = await screen.findByRole('status');
    const add = within(notice).getByRole('button', { name: 'Add the tag' });
    expect(add).toBeDisabled();

    await fireEvent.input(within(notice).getByRole('textbox', { name: 'Tag name' }), { target: { value: 'screened' } });
    expect(add).toBeEnabled();
    await fireEvent.click(add);

    await screen.findByRole('heading', { level: 3, name: 'Meets every requirement' });
    expect(screen.queryByText(/Paused until/)).toBeNull();
    expect((sent(fetchMock)[0] as unknown as { workflow: HiringWorkflow }).workflow.automations.at(-1)!.actions[0]).toEqual({ type: 'AddTag', tag: 'screened' });
  });

  it('adds the tag on Enter, trimmed', async () => {
    stubApi();
    renderDrive({}, blankTag());
    const notice = await screen.findByRole('status');

    await fireEvent.input(within(notice).getByRole('textbox', { name: 'Tag name' }), { target: { value: '  screened  ' } });
    await fireEvent.keyDown(within(notice).getByRole('textbox', { name: 'Tag name' }), { key: 'Enter' });

    await screen.findByRole('heading', { level: 3, name: 'Meets every requirement' });
  });

  it('removes that step, and the automation it was the only step of, and the samples run', async () => {
    stubApi();
    renderDrive({}, blankTag());
    const notice = await screen.findByRole('status');

    await fireEvent.click(within(notice).getByRole('button', { name: 'Remove that step' }));

    await screen.findByRole('heading', { level: 3, name: 'Meets every requirement' });
    expect(screen.getByTestId('automation-count')).toHaveTextContent(String(workflow.automations.length));
  });

  it('removes only the step when the automation has others', async () => {
    stubApi();
    const flow = blankTag();
    flow.automations.at(-1)!.actions = [{ type: 'NotifyTeam' }, { type: 'AddTag', tag: '' }];
    renderDrive({}, flow);
    const notice = await screen.findByRole('status');

    await fireEvent.click(within(notice).getByRole('button', { name: 'Remove that step' }));

    await screen.findByRole('heading', { level: 3, name: 'Meets every requirement' });
    expect(screen.getByTestId('automation-count')).toHaveTextContent(String(workflow.automations.length + 1));
  });

  it('offers Fix it for any other problem, which hands its path to the page', async () => {
    stubApi();
    const onfix = vi.fn();
    renderDrive({ onfix, extra: [{ path: 'stages[1].name', message: 'Give the stage a name.' }] });

    const notice = await screen.findByRole('status');
    expect(notice).toHaveTextContent('Give the stage a name.');
    expect(within(notice).queryByRole('textbox')).toBeNull();
    await fireEvent.click(within(notice).getByRole('button', { name: 'Fix it' }));

    expect(onfix).toHaveBeenCalledExactlyOnceWith('stages[1].name');
  });

  it('offers no fix where the version cannot be changed, and says so', async () => {
    stubApi();
    renderDrive({ editable: false, extra: [{ path: 'stages[1].name', message: 'Give the stage a name.' }] });

    const notice = await screen.findByRole('status');
    expect(notice).toHaveTextContent('This version cannot be changed, so it cannot be tested.');
    expect(within(notice).queryByRole('button')).toBeNull();
    expect(within(notice).queryByRole('textbox')).toBeNull();
  });
});

describe('an empty workflow and your own candidate', () => {
  it('says there is nothing to test yet and offers adding an automation where the page can', async () => {
    stubApi();
    const onadd = vi.fn();
    renderDrive({ onadd }, { ...workflow, automations: [] });

    expect(await screen.findByText('Nothing to test yet')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Add an automation' }));
    expect(onadd).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: 'Try your own candidate' })).toBeNull();
  });

  it('offers no Add an automation where the page cannot', async () => {
    stubApi();
    renderDrive({}, { ...workflow, automations: [] });

    expect(await screen.findByText('Nothing to test yet')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add an automation' })).toBeNull();
  });

  it('swaps the samples for the guided test under Try your own candidate, and back', async () => {
    stubApi();
    renderDrive();
    await screen.findByRole('heading', { level: 3, name: 'Meets every requirement' });

    await fireEvent.click(screen.getByRole('button', { name: 'Try your own candidate' }));

    expect(screen.getByRole('region', { name: 'Try your own candidate' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3 })).toHaveTextContent('What happened?');
    expect(screen.queryByRole('region', { name: 'What happens to these candidates' })).toBeNull();

    await fireEvent.click(screen.getByRole('button', { name: 'Back to the samples' }));

    expect(await screen.findByRole('region', { name: 'What happens to these candidates' })).toBeInTheDocument();
    expect(await screen.findByRole('heading', { level: 3, name: 'Meets every requirement' })).toBeInTheDocument();
  });
});
