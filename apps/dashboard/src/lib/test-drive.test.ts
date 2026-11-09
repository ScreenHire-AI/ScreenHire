// The Test tab's samples (test-drive.ts): which sample candidates a template gets, and the few plain steps each answer
// is told in. The API's evaluator stays the judge, so the answers here are canned.
import type { Automation, HiringWorkflow, SimulatedAutomation, WorkflowSimulationResult } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { outcomeOf, samplesOf } from './test-drive.js';

const automation = (id: string, trigger: Automation['trigger'], extra: Partial<Automation> = {}): Automation => ({
  id,
  name: id,
  trigger,
  conditions: null,
  actions: [{ type: 'AddTag', tag: id }],
  ...extra,
});

const workflowWith = (automations: Automation[]): HiringWorkflow => ({
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone-screen', name: 'Phone screen', kind: 'Screening', slaDays: 3 },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: {
    questions: [{ id: 'right_to_work', type: 'YesNo', label: 'Do you have the right to work here?', required: true, knockout: { disqualifyingAnswer: false } }],
  },
  automations,
});

const knockout = automation('queue-review', { type: 'ApplicationSubmitted' }, {
  conditions: { match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }] },
  actions: [{ type: 'QueueForRejectionReview', reason: 'No right to work' }],
});
const route = automation('route', { type: 'VerificationAssessed' }, {
  conditions: { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'EqualTo', value: 'VerificationRecommended' }] },
  actions: [{ type: 'AssignReviewer' }],
});
const tagOnEntry = automation('tag-screen', { type: 'StageEntered', stageId: 'phone-screen' });

describe('samplesOf', () => {
  it('has four samples when a knockout and a verification route are on, in the order a person reads them', () => {
    const samples = samplesOf(workflowWith([knockout, route]));

    expect(samples.map((sample) => [sample.id, sample.name, sample.moment])).toEqual([
      ['meets', 'Meets every requirement', 'When they apply'],
      ['fails', 'Fails a knockout', 'When they apply'],
      ['signals', 'Checks suggest a review', 'When the checks finish'],
      ['accommodation', 'Asked for an accommodation', 'When they apply'],
    ]);
  });

  it('tries a stage that has steps of its own at its own moment, before the accommodation', () => {
    const samples = samplesOf(workflowWith([knockout, route, tagOnEntry]));

    expect(samples.map((sample) => [sample.id, sample.name, sample.moment])).toEqual([
      ['meets', 'Meets every requirement', 'When they apply'],
      ['fails', 'Fails a knockout', 'When they apply'],
      ['signals', 'Checks suggest a review', 'When the checks finish'],
      ['stage:phone-screen', 'Reaches Phone screen', 'When they reach Phone screen'],
      ['accommodation', 'Asked for an accommodation', 'When they apply'],
    ]);
    expect(samples[3]!.request.trigger).toEqual({ type: 'StageEntered', stageId: 'phone-screen' });
  });

  it('builds the requests from the template: the knockout answer that passes, and the one that fails', () => {
    const [meets, fails, signals, accommodation] = samplesOf(workflowWith([knockout, route]));

    expect(meets!.request).toMatchObject({ trigger: { type: 'ApplicationSubmitted' }, facts: { answers: [{ questionId: 'right_to_work', values: ['yes'] }] }, accommodationRequested: false });
    expect(fails!.request).toMatchObject({ trigger: { type: 'ApplicationSubmitted' }, facts: { answers: [{ questionId: 'right_to_work', values: ['no'] }] }, accommodationRequested: false });
    expect(signals!.request).toMatchObject({ trigger: { type: 'VerificationAssessed' }, facts: { verificationAuthenticity: 'VerificationRecommended' }, accommodationRequested: false });
    expect(accommodation!.request).toMatchObject({ trigger: { type: 'ApplicationSubmitted' }, facts: { answers: [{ questionId: 'right_to_work', values: ['yes'] }] }, accommodationRequested: true });
  });

  it('carries the workflow itself in each request', () => {
    const flow = workflowWith([knockout]);

    for (const sample of samplesOf(flow)) expect(sample.request.workflow).toBe(flow);
  });

  it('has no sample for the checks unless an automation reads them, and none that fails when no knockout is read', () => {
    const noChecks = samplesOf(workflowWith([knockout]));
    const noKnockout = samplesOf(workflowWith([automation('welcome', { type: 'ApplicationSubmitted' }), route]));

    expect(noChecks.map((sample) => sample.id)).toEqual(['meets', 'fails', 'accommodation']);
    expect(noKnockout.map((sample) => sample.id)).toEqual(['meets', 'signals', 'accommodation']);
  });

  it('tries the first moment there is when nothing runs on an application, naming its stage', () => {
    const samples = samplesOf(workflowWith([tagOnEntry]));

    expect(samples.map((sample) => sample.id)).toEqual(['meets', 'accommodation']);
    expect(samples[0]!.moment).toBe('When they reach a stage');
    expect(samples[0]!.request.trigger).toEqual({ type: 'StageEntered', stageId: 'phone-screen' });
  });

  it('has none when no automation is on', () => {
    expect(samplesOf(workflowWith([]))).toEqual([]);
    expect(samplesOf(workflowWith([{ ...knockout, enabled: false }]))).toEqual([]);
  });
});

describe('outcomeOf', () => {
  const simulated = (id: string, outcome: SimulatedAutomation['outcome'], actions: SimulatedAutomation['actions'] = []): SimulatedAutomation => ({
    automationId: id,
    name: id,
    outcome,
    rules: [],
    actions,
    reason: null,
  });
  const flow = workflowWith([knockout, route, tagOnEntry]);
  const answer = (...automations: SimulatedAutomation[]): WorkflowSimulationResult => ({ automations });
  const names = new Map([['application-received', 'Application received']]);

  it('tells one step per action of every automation that ran, in order', () => {
    const outcome = outcomeOf(
      flow,
      answer(
        simulated('tag-screen', 'Matched', [{ type: 'AddTag', tag: 'screened' }, { type: 'SendEmail', emailTemplate: 'application-received' }]),
        simulated('queue-review', 'NotMatched'),
        simulated('route', 'Matched', [{ type: 'NotifyTeam' }]),
      ),
      names,
    );

    expect(outcome.steps).toEqual(['Adds the tag "screened"', 'Sends the "Application received" email', 'Tells the team']);
    expect(outcome.note).toBe('Then a person moves them on');
  });

  it('says a queued review goes to the team\'s list and that nobody is turned down', () => {
    const outcome = outcomeOf(flow, answer(simulated('queue-review', 'Matched', [{ type: 'QueueForRejectionReview' }])), names);

    expect(outcome.steps).toHaveLength(1);
    expect(outcome.steps[0]).toMatch(/^Goes to the team's review list/);
    expect(outcome.steps[0]).toContain('Nobody is turned down yet');
  });

  it('never names the person a step asks, and never says reject, score or a percentage', () => {
    const outcome = outcomeOf(
      flow,
      answer(simulated('route', 'Matched', [{ type: 'AssignReviewer', assigneeUserId: '01a157d4-0000-7000-8000-0000000000aa' }, { type: 'QueueForRejectionReview' }])),
      names,
    );

    expect(outcome.steps[0]).toBe('Asks the team to review');
    const text = [...outcome.steps, outcome.note].join(' ');
    expect(text).not.toMatch(/01a157d4|reject|score|\d+\s?%/i);
  });

  it('says nothing runs when nothing matched', () => {
    const outcome = outcomeOf(flow, answer(simulated('queue-review', 'NotMatched')), names);

    expect(outcome.steps).toEqual([]);
    expect(outcome.note).toBe('Nothing runs, a person moves them on');
  });

  it('says timing steps are skipped for a candidate who asked for an accommodation, and otherwise that no step was', () => {
    const skipped = outcomeOf(flow, answer(simulated('tag-screen', 'Skipped')), names, true);
    const none = outcomeOf(flow, answer(simulated('queue-review', 'Matched', [{ type: 'AddTag', tag: 'x' }])), names, true);

    expect(skipped.note).toBe('Timing steps are skipped for them');
    expect(none.note).toBe('No steps skipped');
  });
});
