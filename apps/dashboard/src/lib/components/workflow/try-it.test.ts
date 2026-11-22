// The Try it tab: who is applying, then one stage at a time with what happens there drawn as the real thing, then the end
// with publish and change something. The simulate endpoint is stubbed from each request; the component only shows it.
import type { HiringWorkflow } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub } from '../../../test-support/fixtures.js';
import TryItHarness from '../../../test-support/try-it-harness.svelte';
import WithQueryClient from '../../../test-support/with-query-client.svelte';

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'screening', name: 'Phone call', kind: 'Screening', slaDays: 3 },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: {
    questions: [{ id: 'licence', type: 'YesNo', label: 'Do you hold a driving licence?', required: true, knockout: { disqualifyingAnswer: false } }],
  },
  automations: [
    { id: 'welcome', name: 'Welcome', trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'SendEmail', emailTemplate: 'application-received' }] },
    {
      id: 'queue',
      name: 'Queue for review',
      trigger: { type: 'ApplicationSubmitted' },
      conditions: { match: 'All', rules: [{ fact: 'answers.licence', operator: 'EqualTo', value: 'no' }] },
      actions: [{ type: 'QueueForRejectionReview', reason: 'No licence' }],
    },
    { id: 'call', name: 'Book the call', trigger: { type: 'StageEntered', stageId: 'screening' }, conditions: null, actions: [{ type: 'CreateTask', note: 'Call them' }] },
    { id: 'late', name: 'Remind the team', trigger: { type: 'SlaBreached', stageId: 'screening' }, conditions: null, actions: [{ type: 'NotifyTeam', note: 'Late' }] },
  ],
};

type Body = {
  workflow: HiringWorkflow;
  trigger: { type: string; stageId?: string; questionnaireId?: string };
  facts: { answers: { questionId: string; values: string[] }[]; verificationAuthenticity: string };
  chain: string[];
};

// The evaluator's answer for one moment, standing in for the API: the automations of the request's workflow that react
// to the trigger, each matched when its one answer rule holds.
function answerFor(body: Body) {
  const holds = (rule: { fact: string; value?: string | null }) =>
    body.facts.answers.some((answer) => `answers.${answer.questionId}` === rule.fact && answer.values.includes(rule.value ?? ''));
  return {
    automations: body.workflow.automations
      .filter(
        (automation) =>
          automation.trigger.type === body.trigger.type &&
          (automation.trigger.stageId ?? null) === (body.trigger.stageId ?? null) &&
          (automation.trigger.questionnaireId ?? null) === (body.trigger.questionnaireId ?? null),
      )
      .map((automation) => {
        const matched = (automation.conditions?.rules ?? []).every(holds);
        return { automationId: automation.id, name: automation.name, outcome: matched ? 'Matched' : 'NotMatched', rules: [], actions: matched ? automation.actions : [], reason: null };
      }),
  };
}

const template = (id: string, name: string) => ({ id, name, builtIn: true, edited: false, rejectOnly: false, subject: `${name} {{ candidateName }}`, body: 'Hi', updatedAt: null, lastUpdated: null, questionnaireOnly: false, archived: false });

function stubApi(options: { sendingEnabled?: boolean; send?: () => Response } = {}) {
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/email-templates': () =>
        Response.json({ templates: [template('application-received', 'Application received'), template('questionnaire-request', 'Follow-up questions')], variables: [], sendingEnabled: options.sendingEnabled ?? true }),
      'GET /v1/organization': () => Response.json({ name: 'Brightwater Studio', questionsPerPage: 5 }),
      'GET /v1/organization/branding': () => Response.json({ logo: null }),
      'POST /v1/email-templates/preview': (init) => {
        const { subject } = JSON.parse(String(init?.body)) as { subject: string };
        return Response.json({ subject: subject.replace('{{ candidateName }}', 'Jordan Lee'), html: '<p>Hi Jordan Lee</p>', text: 'Hi Jordan Lee,\nWe have it.' });
      },
      'POST /v1/workflows/simulate': (init) => Response.json(answerFor(JSON.parse(String(init?.body)))),
      'POST /v1/workflows/test-emails': options.send ?? (() => Response.json({ to: 'me@example.com', count: 1 })),
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

const sent = (fetchMock: ReturnType<typeof stubApi>, url: string): Body[] =>
  fetchMock.mock.calls.filter(([called]) => called === url).map(([, init]) => JSON.parse(String(init?.body)));

const renderTry = (props: Record<string, unknown> = {}, flow: HiringWorkflow = workflow) =>
  render(WithQueryClient, { props: { component: TryItHarness, props: { workflow: structuredClone(flow), ...props } } });

const stageHeading = (name: string) => screen.findByRole('heading', { level: 3, name });
const things = () => [...document.querySelectorAll<HTMLElement>('[data-thing]')].map((thing) => thing.dataset.thing);

afterEach(() => vi.unstubAllGlobals());

describe('who is applying', () => {
  it('offers four big cards, says nothing real is created, and passes axe', async () => {
    stubApi();
    renderTry();

    expect(screen.getByRole('heading', { level: 3, name: 'Who is applying?' })).toBeInTheDocument();
    const cards = screen.getAllByRole('listitem').map((item) => item.dataset.scenario);
    expect(cards).toEqual(['fit', 'cannot', 'check', 'form']);
    expect(screen.getByText('Nothing real is created and no one is emailed.')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("leaves out Can't work here where no question is a must-have", () => {
    stubApi();
    renderTry({}, { ...workflow, questionnaire: { questions: [] } });

    expect(screen.queryByRole('button', { name: /Can't work here/ })).toBeNull();
    expect(screen.getByRole('button', { name: /A good fit/ })).toBeInTheDocument();
  });
});

describe('a good fit, one stage at a time', () => {
  it('walks them through every stage to the end, one thing at a time, and passes axe at each step', async () => {
    const fetchMock = stubApi();
    const run = vi.fn();
    renderTry({ finish: { label: 'Looks good, publish', run } });

    await fireEvent.click(screen.getByRole('button', { name: /A good fit/ }));

    // Applying: the email as an inbox shows it, with Jordan's name in it, and the line that says who decides.
    const applied = await stageHeading('Applied');
    await waitFor(() => expect(applied).toHaveFocus());
    expect(screen.getByRole('list', { name: 'Pipeline' }).querySelector('[aria-current="step"]')).toHaveTextContent('Applied');
    expect(await screen.findByText('Application received Jordan Lee')).toBeInTheDocument();
    expect(screen.getByText('Hi Jordan Lee,')).toBeInTheDocument();
    // One preview for the one email: the answer arriving must not ask for it again.
    expect(sent(fetchMock, '/v1/email-templates/preview')).toHaveLength(1);
    expect(things()).toEqual(['email', 'decide']);
    expect(screen.getByText('You decide when Jordan moves on.')).toBeInTheDocument();
    expect(sent(fetchMock, '/v1/workflows/simulate').map((body) => body.trigger.type)).toEqual(['ApplicationSubmitted', 'VerificationAssessed', 'StageEntered']);
    await expectNoAxeViolations();

    await fireEvent.click(screen.getByRole('button', { name: 'Move Jordan to Phone call' }));
    await stageHeading('Phone call');
    expect(screen.getByText('Adds a task for the team')).toBeInTheDocument();
    await expectNoAxeViolations();

    // Three days pass: the time limit is over, and the reminder goes to the team.
    await fireEvent.click(screen.getByRole('button', { name: 'Let 3 days pass' }));
    expect(await screen.findByText('3 days later')).toBeInTheDocument();
    expect(screen.getByText('Tells the team')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Let 3 days pass' })).toBeNull();
    expect(sent(fetchMock, '/v1/workflows/simulate').at(-1)).toMatchObject({ trigger: { type: 'SlaBreached', stageId: 'screening' }, facts: { hoursInStage: 72 } });

    await fireEvent.click(screen.getByRole('button', { name: 'Move Jordan to Hired' }));
    await stageHeading('Hired');
    expect(screen.getByText('Nothing happens by itself here. You decide when Jordan moves on.')).toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'See the whole process' }));
    expect(await screen.findByRole('heading', { level: 3, name: "That's your hiring process" })).toBeInTheDocument();
    expect(screen.getByText('Jordan went through 3 stages and got 1 email.')).toBeInTheDocument();
    await expectNoAxeViolations();

    await fireEvent.click(screen.getByRole('button', { name: 'Looks good, publish' }));
    expect(run).toHaveBeenCalledOnce();
  });

  it('opens an email in full from its inbox row, saying nothing is sent', async () => {
    stubApi();
    renderTry();
    await fireEvent.click(screen.getByRole('button', { name: /A good fit/ }));
    await stageHeading('Applied');

    await fireEvent.click(await screen.findByRole('button', { name: /Application received Jordan Lee/ }));

    const sheet = await screen.findByRole('dialog', { name: 'Application received' });
    expect(sheet).toHaveTextContent('What the candidate gets. Nothing is sent.');
    expect(within(sheet).getByText('Application received Jordan Lee')).toBeInTheDocument();
  });

  it('sends the emails of a stage to the person, marked as a test, and says when they have sent too many', async () => {
    let status = 200;
    const fetchMock = stubApi({ send: () => (status === 200 ? Response.json({ to: 'me@example.com', count: 1 }) : new Response(null, { status })) });
    renderTry();
    await fireEvent.click(screen.getByRole('button', { name: /A good fit/ }));
    await stageHeading('Applied');

    await fireEvent.click(screen.getByRole('button', { name: 'Send me these emails' }));
    expect(await screen.findByText('Sent the email to me@example.com, marked as a test.')).toBeInTheDocument();
    expect(sent(fetchMock, '/v1/workflows/test-emails')).toEqual([{ templateIds: ['application-received'] }]);

    status = 429;
    await fireEvent.click(screen.getByRole('button', { name: 'Send me these emails' }));
    expect(await screen.findByText('You have sent a few already. Try again in a few minutes.')).toBeInTheDocument();
  });

  it('offers no Send me these emails where the organization cannot send, or on a stage with no email', async () => {
    stubApi({ sendingEnabled: false });
    renderTry();
    await fireEvent.click(screen.getByRole('button', { name: /A good fit/ }));
    await stageHeading('Applied');
    expect(screen.queryByRole('button', { name: 'Send me these emails' })).toBeNull();

    vi.unstubAllGlobals();
    stubApi();
    await fireEvent.click(screen.getByRole('button', { name: 'Move Jordan to Phone call' }));
    await stageHeading('Phone call');
    expect(screen.queryByRole('button', { name: 'Send me these emails' })).toBeNull();
  });

  it('starts again after an edit, because the walk was about the pipeline as it was', async () => {
    stubApi();
    renderTry();
    await fireEvent.click(screen.getByRole('button', { name: /A good fit/ }));
    await stageHeading('Applied');

    await fireEvent.click(screen.getByRole('button', { name: 'Rename a stage' }));

    expect(await screen.findByRole('heading', { level: 3, name: 'Who is applying?' })).toBeInTheDocument();
  });
});

describe('the other candidates', () => {
  it("puts the one who can't work here on the review list, in plain words, and turns nobody down", async () => {
    stubApi();
    renderTry();
    await fireEvent.click(screen.getByRole('button', { name: /Can't work here/ }));
    await stageHeading('Applied');

    expect(screen.getByText('Jordan said no to "Do you hold a driving licence?". They wait on your review list, and nobody is turned down until you decide.')).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/knockout|reject/i);
    expect(screen.getByRole('button', { name: 'Move Jordan to Phone call' })).toBeInTheDocument();
  });

  it('says only that the checks flagged something and that a person decides', async () => {
    const fetchMock = stubApi();
    renderTry();
    await fireEvent.click(screen.getByRole('button', { name: /Something to check/ }));
    await stageHeading('Applied');

    expect(screen.getByText('The checks flagged something about Jordan. A person looks and decides what happens.')).toBeInTheDocument();
    expect(sent(fetchMock, '/v1/workflows/simulate')[1]).toMatchObject({ trigger: { type: 'VerificationAssessed' }, facts: { verificationAuthenticity: 'VerificationRecommended' } });
    expect(document.body.textContent).not.toMatch(/Verification is|\bVerify\b|score|%/);
  });

  it('takes the answers of the form filled in by hand, with Jordan Lee filled in', async () => {
    const fetchMock = stubApi();
    renderTry();
    await fireEvent.click(screen.getByRole('button', { name: /Fill in the form yourself/ }));

    expect(screen.getByRole('textbox', { name: /First name/ })).toHaveValue('Jordan');
    await fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    await fireEvent.click(screen.getByRole('radio', { name: 'No' }));
    while (screen.queryByRole('button', { name: 'Next' })) await fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Send application' }));

    await stageHeading('Applied');
    expect(screen.getByText(/Jordan said no to "Do you hold a driving licence\?"/)).toBeInTheDocument();
    expect(sent(fetchMock, '/v1/workflows/simulate')[0]!.facts.answers).toEqual([{ questionId: 'licence', values: ['no'] }]);
  });
});

describe('automations that carry on by themselves', () => {
  it('answers a form a stage sends, and follows the move its answer makes', async () => {
    stubApi();
    const flow: HiringWorkflow = {
      ...workflow,
      followUpQuestionnaires: [{ id: 'trial', name: 'Trial shift questions', questions: [{ id: 'when', type: 'ShortText', label: 'When can you start?', required: true }] }],
      automations: [
        { id: 'send', name: 'Send the form', trigger: { type: 'StageEntered', stageId: 'screening' }, conditions: null, actions: [{ type: 'SendQuestionnaire', questionnaireId: 'trial' }] },
        { id: 'answered', name: 'Move on', trigger: { type: 'QuestionnaireCompleted', questionnaireId: 'trial' }, conditions: null, actions: [{ type: 'MoveToStage', stageId: 'hired' }] },
      ],
    };
    renderTry({}, flow);
    await fireEvent.click(screen.getByRole('button', { name: /A good fit/ }));
    await stageHeading('Applied');

    await fireEvent.click(screen.getByRole('button', { name: 'Move Jordan to Phone call' }));

    await stageHeading('Hired');
    expect(screen.getByText('Jordan moved on to Hired by itself.')).toBeInTheDocument();
    expect(await screen.findByText('Follow-up questions Jordan Lee')).toBeInTheDocument();
  });

  it('stops following automations that move them back and forth', async () => {
    const fetchMock = stubApi();
    const flow: HiringWorkflow = {
      ...workflow,
      automations: [
        { id: 'there', name: 'There', trigger: { type: 'StageEntered', stageId: 'screening' }, conditions: null, actions: [{ type: 'MoveToStage', stageId: 'hired' }] },
        { id: 'back', name: 'Back', trigger: { type: 'StageEntered', stageId: 'hired' }, conditions: null, actions: [{ type: 'MoveToStage', stageId: 'screening' }] },
      ],
    };
    renderTry({}, flow);
    await fireEvent.click(screen.getByRole('button', { name: /A good fit/ }));
    await stageHeading('Applied');

    await fireEvent.click(screen.getByRole('button', { name: 'Move Jordan to Phone call' }));

    expect(await screen.findByText('Jordan moved on to Hired, then Phone call, then Hired by itself. It keeps moving them on, so Try it stops here.')).toBeInTheDocument();
    const entered = sent(fetchMock, '/v1/workflows/simulate').filter((body) => body.trigger.type === 'StageEntered').slice(1);
    expect(entered).toHaveLength(4);
    expect(entered.at(-1)!.chain).toEqual(['there', 'back', 'there']);
  });
});

describe('the end', () => {
  it('opens the stage chosen under Change something, and offers neither button where the process cannot change', async () => {
    stubApi();
    const onchange = vi.fn();
    const { unmount } = renderTry({ onchange }, { ...workflow, stages: [workflow.stages[0]!, workflow.stages[2]!] });
    await fireEvent.click(screen.getByRole('button', { name: /A good fit/ }));
    await stageHeading('Applied');
    await fireEvent.click(screen.getByRole('button', { name: 'Move Jordan to Hired' }));
    await stageHeading('Hired');
    await fireEvent.click(screen.getByRole('button', { name: 'See the whole process' }));

    await fireEvent.pointerDown(await screen.findByRole('button', { name: 'Change something' }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Applied' }));
    await waitFor(() => expect(onchange).toHaveBeenCalledExactlyOnceWith('applied'));
    unmount();

    stubApi();
    renderTry({ onchange, editable: false }, { ...workflow, stages: [workflow.stages[2]!] });
    await fireEvent.click(screen.getByRole('button', { name: /A good fit/ }));
    await stageHeading('Hired');
    await fireEvent.click(screen.getByRole('button', { name: 'See the whole process' }));
    await screen.findByRole('heading', { level: 3, name: "That's your hiring process" });
    expect(screen.queryByRole('button', { name: 'Change something' })).toBeNull();
    expect(screen.queryByRole('button', { name: /Looks good/ })).toBeNull();
  });
});

const blankTag = (flow: HiringWorkflow = workflow): HiringWorkflow => ({
  ...flow,
  automations: [...flow.automations, { id: 'tag', name: 'Tag screened', trigger: { type: 'StageEntered', stageId: 'screening' }, conditions: null, actions: [{ type: 'AddTag', tag: '' }] }],
});

describe('paused by a problem', () => {
  it('shows only the notice, with the first message and how many things to fix, and runs nothing', async () => {
    const fetchMock = stubApi();
    renderTry({ extra: [{ path: 'stages', message: 'A workflow needs a hired stage.' }] }, blankTag());

    const notice = await screen.findByRole('status');
    expect(notice).toHaveTextContent('Paused until 2 things are fixed');
    expect(notice).toHaveTextContent('The tag in "Tag screened" has no name. Type one, or remove that step.');
    expect(screen.queryByRole('heading', { level: 3 })).toBeNull();
    expect(sent(fetchMock, '/v1/workflows/simulate')).toHaveLength(0);
  });

  it('fixes a blank Add a tag step in place, and the cards come back', async () => {
    stubApi();
    renderTry({}, blankTag());
    const notice = await screen.findByRole('status');

    await fireEvent.input(within(notice).getByRole('textbox', { name: 'Tag name' }), { target: { value: 'screened' } });
    await fireEvent.click(within(notice).getByRole('button', { name: 'Add the tag' }));

    expect(await screen.findByRole('heading', { level: 3, name: 'Who is applying?' })).toBeInTheDocument();
    expect(screen.queryByText(/Paused until/)).toBeNull();
  });

  it('removes that step, and the automation it was the only step of', async () => {
    stubApi();
    renderTry({}, blankTag());
    const notice = await screen.findByRole('status');

    await fireEvent.click(within(notice).getByRole('button', { name: 'Remove that step' }));

    await screen.findByRole('heading', { level: 3, name: 'Who is applying?' });
    expect(screen.getByTestId('automation-count')).toHaveTextContent(String(workflow.automations.length));
  });

  it('offers Fix it for any other problem, and no fix where the version cannot be changed', async () => {
    stubApi();
    const onfix = vi.fn();
    const { unmount } = renderTry({ onfix, extra: [{ path: 'stages[1].name', message: 'Give the stage a name.' }] });
    await fireEvent.click(within(await screen.findByRole('status')).getByRole('button', { name: 'Fix it' }));
    expect(onfix).toHaveBeenCalledExactlyOnceWith('stages[1].name');
    unmount();

    renderTry({ editable: false, extra: [{ path: 'stages[1].name', message: 'Give the stage a name.' }] });
    const notice = await screen.findByRole('status');
    expect(within(notice).queryByRole('button')).toBeNull();
  });
});
