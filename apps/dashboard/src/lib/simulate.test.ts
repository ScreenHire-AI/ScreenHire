// The guided test's rules (simulate.ts): which moments and questions are worth asking, the ready-made candidates built
// from what the automations read, and the words the answer comes in.
import type { Automation, HiringWorkflow, Question, WorkflowSimulationResult } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { describeMoment, describeRule, factsOf, followUpFor, hasFields, listenersOf, momentsOf, needsAnswer, presetsFor, readsOf, timelineOf, triggerFor } from './simulate.js';

const rightToWork: Question = { id: 'right_to_work', type: 'YesNo', label: 'Do you have the right to work here?', required: true, knockout: { disqualifyingAnswer: false, reason: 'The role needs it.' } };
const stack: Question = {
  id: 'stack',
  type: 'MultiChoice',
  label: 'Which of these have you used?',
  options: [
    { id: 'csharp', label: 'C#' },
    { id: 'svelte', label: 'Svelte' },
  ],
};

const automation = (id: string, trigger: Automation['trigger'], extra: Partial<Automation> = {}): Automation => ({
  id,
  name: id,
  trigger,
  conditions: null,
  actions: [{ type: 'AddTag', tag: id }],
  ...extra,
});

const workflowWith = (automations: Automation[], extra: Partial<HiringWorkflow> = {}): HiringWorkflow => ({
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone-screen', name: 'Phone screen', kind: 'Screening', slaDays: 3 },
    { id: 'interview', name: 'Interview', kind: 'Interview', scorecardTemplateId: 'sc-1' },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: { questions: [rightToWork, stack] },
  verificationPolicy: { aiUse: 'AllowedWithDisclosure' },
  automations,
  ...extra,
});

const knockoutRule = automation('queue-review', { type: 'ApplicationSubmitted' }, {
  conditions: { match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }] },
  actions: [{ type: 'QueueForRejectionReview', reason: 'No right to work' }],
});
const verifyRoute = automation('route', { type: 'VerificationAssessed' }, {
  conditions: { match: 'Any', rules: [{ fact: 'verification.authenticity', operator: 'EqualTo', value: 'VerificationRecommended' }, { fact: 'verification.aiAssistance', operator: 'EqualTo', value: 'StrongIndicators' }] },
});

describe('moments', () => {
  it('offers only what an automation that is on reacts to, in the order things happen to a candidate, with how many', () => {
    const flow = workflowWith([
      automation('a', { type: 'StageEntered', stageId: 'applied' }),
      automation('b', { type: 'ApplicationSubmitted' }),
      automation('c', { type: 'ApplicationSubmitted' }),
      automation('d', { type: 'SlaBreached' }, { enabled: false }),
    ]);

    expect(momentsOf(flow)).toEqual([
      { type: 'ApplicationSubmitted', label: 'A candidate applied', automations: 2 },
      { type: 'StageEntered', label: 'A candidate moved to a stage', automations: 1 },
    ]);
  });

  it('offers nothing for a workflow with no automation on', () => {
    expect(momentsOf(workflowWith([]))).toEqual([]);
    expect(momentsOf(workflowWith([automation('a', { type: 'Manual' }, { enabled: false })]))).toEqual([]);
  });
});

describe('the second question', () => {
  it('asks which stage only when automations react in more than one, and offers only those stages', () => {
    const flow = workflowWith([automation('a', { type: 'StageEntered', stageId: 'applied' }), automation('b', { type: 'StageEntered', stageId: 'phone-screen' })]);

    const followUp = followUpFor(flow, 'StageEntered');
    expect(needsAnswer(followUp)).toBe(true);
    expect(followUp?.options.map((option) => option.name)).toEqual(['Applied', 'Phone screen']);
    expect(triggerFor(flow, 'StageEntered', 'phone-screen')).toEqual({ type: 'StageEntered', stageId: 'phone-screen' });
  });

  it('does not ask when there is one answer, and takes it', () => {
    const flow = workflowWith([automation('a', { type: 'StageEntered', stageId: 'phone-screen' })]);

    expect(needsAnswer(followUpFor(flow, 'StageEntered'))).toBe(false);
    expect(triggerFor(flow, 'StageEntered')).toEqual({ type: 'StageEntered', stageId: 'phone-screen' });
  });

  it('does not ask for a moment that has no choice, or whose automations all watch any stage', () => {
    const flow = workflowWith([automation('a', { type: 'ApplicationSubmitted' }), automation('b', { type: 'SlaBreached' }), automation('c', { type: 'ReviewerDecision' })]);

    expect(followUpFor(flow, 'ApplicationSubmitted')).toBeNull();
    expect(followUpFor(flow, 'SlaBreached')).toBeNull();
    expect(triggerFor(flow, 'SlaBreached')).toEqual({ type: 'SlaBreached' });
    expect(triggerFor(flow, 'ReviewerDecision')).toEqual({ type: 'ReviewerDecision' });
  });

  it('offers a time limit only the stages that have one when one automation names a stage and another any', () => {
    const flow = workflowWith([automation('a', { type: 'SlaBreached' }), automation('b', { type: 'SlaBreached', stageId: 'phone-screen' })]);

    expect(followUpFor(flow, 'SlaBreached')?.options.map((option) => option.name)).toEqual(['Phone screen']);
  });

  it('asks which follow-up questionnaire when several are named', () => {
    const flow = workflowWith(
      [
        automation('a', { type: 'QuestionnaireCompleted', questionnaireId: 'q1' }),
        automation('b', { type: 'QuestionnaireCompleted', questionnaireId: 'q2' }),
      ],
      {
        followUpQuestionnaires: [
          { id: 'q1', name: 'Availability', questions: [] },
          { id: 'q2', name: 'Salary', questions: [] },
        ],
      },
    );

    const followUp = followUpFor(flow, 'QuestionnaireCompleted');
    expect(followUp?.options.map((option) => option.name)).toEqual(['Availability', 'Salary']);
    expect(triggerFor(flow, 'QuestionnaireCompleted', 'q2')).toEqual({ type: 'QuestionnaireCompleted', questionnaireId: 'q2' });
  });

  it('names the moment with its stage', () => {
    const flow = workflowWith([]);

    expect(describeMoment(flow, { type: 'StageEntered', stageId: 'phone-screen' })).toBe('A candidate moved to Phone screen');
    expect(describeMoment(flow, { type: 'ApplicationSubmitted' })).toBe('A candidate applied');
  });
});

describe('who reacts', () => {
  it('matches the engine: a stage trigger only its own stage, and a scorecard or time limit with no stage any stage', () => {
    const flow = workflowWith([
      automation('entered', { type: 'StageEntered', stageId: 'applied' }),
      automation('anyScorecard', { type: 'ReviewerDecision' }),
      automation('thatScorecard', { type: 'ReviewerDecision', stageId: 'interview' }),
      automation('off', { type: 'StageEntered', stageId: 'applied' }, { enabled: false }),
    ]);

    expect(listenersOf(flow, { type: 'StageEntered', stageId: 'applied' }).map((a) => a.id)).toEqual(['entered']);
    expect(listenersOf(flow, { type: 'StageEntered', stageId: 'phone-screen' })).toEqual([]);
    expect(listenersOf(flow, { type: 'ReviewerDecision', stageId: 'interview' }).map((a) => a.id)).toEqual(['anyScorecard', 'thatScorecard']);
    expect(listenersOf(flow, { type: 'ReviewerDecision', stageId: 'phone-screen' }).map((a) => a.id)).toEqual(['anyScorecard']);
  });
});

describe('what the automations read', () => {
  it('lists only the facts a listening automation tests, and nothing when none is tested', () => {
    const flow = workflowWith([knockoutRule, automation('tagged', { type: 'ApplicationSubmitted' })]);

    const reads = readsOf(flow, listenersOf(flow, { type: 'ApplicationSubmitted' }));
    expect(reads.questions.map((question) => question.id)).toEqual(['right_to_work']);
    expect(reads).toMatchObject({ tags: false, source: false, hoursInStage: false, authenticity: false, aiAssistance: false, recommendation: false, timing: false });

    const plain = workflowWith([automation('tagged', { type: 'ApplicationSubmitted' })]);
    expect(hasFields(readsOf(plain, listenersOf(plain, { type: 'ApplicationSubmitted' })))).toBe(false);
  });

  it('counts a time-passed trigger and a timing condition as timing, which an accommodation changes', () => {
    const flow = workflowWith([automation('late', { type: 'TimeElapsed', stageId: 'applied', afterHours: 48 })]);

    expect(readsOf(flow, listenersOf(flow, { type: 'TimeElapsed', stageId: 'applied' })).timing).toBe(true);
  });

  it('counts a telemetry condition as timing too, as the engine does', () => {
    const flow = workflowWith([{ ...automation('paste', { type: 'ApplicationSubmitted' }), conditions: { match: 'All', rules: [{ fact: 'telemetry.pasteCount', operator: 'GreaterThan', value: '3' }] } }]);

    expect(readsOf(flow, listenersOf(flow, { type: 'ApplicationSubmitted' })).timing).toBe(true);
  });
});

describe('ready-made candidates', () => {
  const presetsOf = (flow: HiringWorkflow, type: Automation['trigger']['type']) => {
    const trigger = triggerFor(flow, type);
    return presetsFor(flow, trigger, listenersOf(flow, trigger));
  };

  it('has one who meets every requirement and one who fails the knockout', () => {
    const flow = workflowWith([knockoutRule]);

    const presets = presetsOf(flow, 'ApplicationSubmitted');
    expect(presets.map((preset) => preset.name)).toEqual(['Meets every requirement', 'Fails a knockout']);
    expect(presets[0]!.candidate.answers).toEqual({ right_to_work: ['yes'] });
    expect(presets[1]!.candidate.answers).toEqual({ right_to_work: ['no'] });
    expect(presets[1]!.description).toBe('Answers No to "Do you have the right to work here?"');
  });

  it('takes the answer that fails from the knockout, whichever way it points', () => {
    const flow = workflowWith([knockoutRule], { questionnaire: { questions: [{ ...rightToWork, knockout: { disqualifyingAnswer: true } }] } });

    const presets = presetsOf(flow, 'ApplicationSubmitted');
    expect(presets[0]!.candidate.answers).toEqual({ right_to_work: ['no'] });
    expect(presets[1]!.candidate.answers).toEqual({ right_to_work: ['yes'] });
  });

  it('offers a knockout that nothing reads no candidate who fails it', () => {
    const flow = workflowWith([automation('tagged', { type: 'ApplicationSubmitted' })]);

    expect(presetsOf(flow, 'ApplicationSubmitted').map((preset) => preset.name)).toEqual(['Meets every requirement']);
  });

  it('has a candidate with verification signals only where an automation reads a level, described in words', () => {
    const flow = workflowWith([verifyRoute]);

    const presets = presetsOf(flow, 'VerificationAssessed');
    expect(presets.map((preset) => preset.name)).toEqual(['Meets every requirement', 'Has verification signals']);
    expect(presets[0]!.candidate).toMatchObject({ authenticity: 'NoConcerns', aiAssistance: 'NotIndicated' });
    expect(presets[1]!.candidate).toMatchObject({ authenticity: 'VerificationRecommended', aiAssistance: 'StrongIndicators' });
    expect(presets[1]!.description).toBe('Verification suggested and Strong signs of AI help');
    expect(JSON.stringify(presets)).not.toMatch(/\d+\s?%/);

    const other = workflowWith([knockoutRule]);
    expect(presetsOf(other, 'ApplicationSubmitted').some((preset) => preset.id === 'signals')).toBe(false);
  });

  it('sets only the levels an automation reads', () => {
    const flow = workflowWith([
      automation('route', { type: 'VerificationAssessed' }, { conditions: { match: 'All', rules: [{ fact: 'verification.aiAssistance', operator: 'EqualTo', value: 'StrongIndicators' }] } }),
    ]);

    const signals = presetsOf(flow, 'VerificationAssessed').find((preset) => preset.id === 'signals')!;
    expect(signals.candidate.authenticity).toBe('');
    expect(signals.candidate.aiAssistance).toBe('StrongIndicators');
  });

  it('gives a passed time limit the hours it takes, and offers an accommodation where timing counts', () => {
    const flow = workflowWith([
      automation('overdue', { type: 'SlaBreached', stageId: 'phone-screen' }, { conditions: { match: 'All', rules: [{ fact: 'application.hoursInStage', operator: 'GreaterThan', value: '48' }] } }),
    ]);

    const presets = presetsOf(flow, 'SlaBreached');
    expect(presets[0]!.candidate.hoursInStage).toBe('72');
    expect(presets.at(-1)).toMatchObject({ id: 'accommodation', candidate: { accommodation: true } });
  });

  it('sends only what was answered, as the API takes it', () => {
    const facts = factsOf({ answers: { right_to_work: ['no'], empty: [], stack: ['csharp', 'svelte'] }, tags: ' a, b ,', source: '', hoursInStage: '36', authenticity: '', aiAssistance: 'NotIndicated', recommendation: 'No', accommodation: false });

    expect(facts).toEqual({
      answers: [
        { questionId: 'right_to_work', values: ['no'] },
        { questionId: 'stack', values: ['csharp', 'svelte'] },
      ],
      tags: ['a', 'b'],
      source: null,
      hoursInStage: 36,
      verificationAuthenticity: null,
      verificationAiAssistance: 'NotIndicated',
      reviewerRecommendation: 'No',
    });
  });
});

describe('the answer', () => {
  const trigger = { type: 'ApplicationSubmitted' } as const;
  const resultOf = (automations: WorkflowSimulationResult['automations']): WorkflowSimulationResult => ({ automations });

  it('lists what would happen in order, then what would not with one line on why', () => {
    const flow = workflowWith([
      knockoutRule,
      automation('thanks', trigger, { actions: [{ type: 'AddTag', tag: 'seen' }, { type: 'NotifyTeam', note: 'New' }] }),
      automation('paused', trigger, { enabled: false }),
      automation('elsewhere', { type: 'StageEntered', stageId: 'applied' }),
    ]);

    const steps = timelineOf(
      flow,
      trigger,
      resultOf([
        { automationId: 'queue-review', name: 'queue-review', outcome: 'NotMatched', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', held: false }], actions: [], reason: null },
        { automationId: 'thanks', name: 'thanks', outcome: 'Matched', rules: [], actions: [{ type: 'AddTag', tag: 'seen' }, { type: 'NotifyTeam', note: 'New' }], reason: null },
      ]),
    );

    expect(steps.map((step) => [step.id, step.ran])).toEqual([
      ['thanks', true],
      ['queue-review', false],
      ['paused', false],
    ]);
    expect(steps[0]!.actions).toEqual(['Add the tag "seen"', 'Notify the team']);
    expect(steps[1]!.why).toBe('It needs "Do you have the right to work here? is No", and that did not hold.');
    expect(steps[2]!.why).toBe('It is switched off.');
  });

  it('says a stage automation waits for another stage', () => {
    const flow = workflowWith([automation('a', { type: 'StageEntered', stageId: 'applied' }), automation('b', { type: 'StageEntered', stageId: 'phone-screen' })]);

    const steps = timelineOf(flow, { type: 'StageEntered', stageId: 'applied' }, resultOf([{ automationId: 'a', name: 'a', outcome: 'Matched', rules: [], actions: [], reason: null }]));

    expect(steps.find((step) => step.id === 'b')?.why).toBe('It waits for Phone screen.');
  });

  it('says why an accommodation skipped an automation, and a rule that is negated or tests a list reads in words', () => {
    const late = automation('late', { type: 'TimeElapsed', stageId: 'applied', afterHours: 48 });
    const skipped = timelineOf(workflowWith([late]), { type: 'TimeElapsed', stageId: 'applied' }, resultOf([{ automationId: 'late', name: 'late', outcome: 'Skipped', rules: [], actions: [], reason: 'Not evaluated for this application.' }]));
    expect(skipped[0]!.why).toBe('This candidate asked for an accommodation, so it uses no timing and did not run.');

    const flow = workflowWith([]);
    expect(describeRule({ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'yes', negate: true }, flow, 'ApplicationSubmitted')).toBe('Do you have the right to work here? is not Yes');
    expect(describeRule({ fact: 'answers.stack', operator: 'In', values: ['csharp', 'svelte'] }, flow, 'ApplicationSubmitted')).toBe('Which of these have you used? is one of C# and Svelte');
    expect(describeRule({ fact: 'application.hoursInStage', operator: 'GreaterThan', value: '48', negate: true }, flow, 'ApplicationSubmitted')).toMatch(/ is not more than 48$/);
    expect(describeRule({ fact: 'verification.authenticity', operator: 'EqualTo', value: 'VerificationRecommended' }, flow, 'VerificationAssessed')).toBe('Verification: authenticity is Verification suggested');
  });

  it('reads an automation with any-of conditions that all failed as one line', () => {
    const flow = workflowWith([verifyRoute]);

    const steps = timelineOf(
      flow,
      { type: 'VerificationAssessed' },
      resultOf([
        {
          automationId: 'route',
          name: 'route',
          outcome: 'NotMatched',
          rules: [
            { fact: 'verification.authenticity', operator: 'EqualTo', held: false },
            { fact: 'verification.aiAssistance', operator: 'EqualTo', held: false },
          ],
          actions: [],
          reason: null,
        },
      ]),
    );

    expect(steps[0]!.why).toBe('None of its conditions held.');
  });
});
