// The Overview tab's words (journey-text.ts): a workflow read back as what happens to an applicant. Generated text never
// names a person (docs/design/redesign-2026-11.md, Setup picks 1): a reviewer is "the team".
import type { Automation, HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { actionWords, automationsAt, automationWords, homeOf, journeyOf } from './journey-text.js';

const names = new Map([
  ['application-received', 'Application received'],
  ['application-rejected', 'Not this time'],
]);

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
    { id: 'phone-screen', name: 'Phone screen', kind: 'Screening', slaDays: 3, scorecardTemplateId: 'sc-1' },
    { id: 'interview', name: 'Interview', kind: 'Interview' },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: {
    questions: [{ id: 'right_to_work', type: 'YesNo', label: 'Do you have the right to work here?', required: true, knockout: { disqualifyingAnswer: false } }],
  },
  verificationPolicy: { aiUse: 'AllowedWithDisclosure' },
  followUpQuestionnaires: [
    {
      id: 'availability',
      name: 'Availability',
      questions: [
        { id: 'start', type: 'ShortText', label: 'When could you start?' },
        { id: 'days', type: 'ShortText', label: 'Which days?' },
      ],
    },
  ],
  automations,
  ...extra,
});

const flow = workflowWith([]);

describe('actionWords', () => {
  it('says what each step does, in third person', () => {
    expect(actionWords({ type: 'SendEmail', emailTemplate: 'application-received' }, flow, names)).toBe('Sends the "Application received" email');
    expect(actionWords({ type: 'AddTag', tag: 'screened' }, flow, names)).toBe('Adds the tag "screened"');
    expect(actionWords({ type: 'MoveToStage', stageId: 'interview' }, flow, names)).toBe('Moves them to Interview');
    expect(actionWords({ type: 'NotifyTeam' }, flow, names)).toBe('Tells the team');
    expect(actionWords({ type: 'CreateTask' }, flow, names)).toBe('Adds a task for the team');
    expect(actionWords({ type: 'CallWebhook' }, flow, names)).toBe('Tells another system');
    expect(actionWords({ type: 'ScheduleInterview' }, flow, names)).toBe('Offers an interview');
    expect(actionWords({ type: 'QueueForRejectionReview' }, flow, names)).toBe('Queues them for the team to look at first');
  });

  it('falls back to plain words when a name is missing', () => {
    expect(actionWords({ type: 'SendEmail', emailTemplate: 'gone' }, flow, names)).toBe('Sends an email');
    expect(actionWords({ type: 'SendEmail' }, flow, names)).toBe('Sends an email');
    expect(actionWords({ type: 'MoveToStage', stageId: 'gone' }, flow, names)).toBe('Moves them to another stage');
  });

  it('counts the questions of a form it sends', () => {
    expect(actionWords({ type: 'SendQuestionnaire', questionnaireId: 'availability' }, flow, names)).toBe('Sends the "Availability" form, 2 questions');
    const one = workflowWith([], { followUpQuestionnaires: [{ id: 'availability', name: 'Availability', questions: [{ id: 'start', type: 'ShortText', label: 'Start?' }] }] });
    expect(actionWords({ type: 'SendQuestionnaire', questionnaireId: 'availability' }, one, names)).toBe('Sends the "Availability" form, 1 question');
  });

  it('never names a person, whoever the step is saved with', () => {
    const words = actionWords({ type: 'AssignReviewer', assigneeUserId: '01a157d4-0000-7000-8000-0000000000aa' }, flow, names);

    expect(words).toBe('Asks the team to review');
    expect(words).not.toMatch(/01a157d4|Avery|Quinn/);
  });
});

describe('automationWords', () => {
  it('is just the steps for an automation of its stage, joined with then', () => {
    const entered = automation('x', { type: 'StageEntered', stageId: 'phone-screen' }, { actions: [{ type: 'SendEmail', emailTemplate: 'application-received' }, { type: 'AddTag', tag: 'screened' }] });

    expect(automationWords(entered, flow, names)).toBe('Sends the "Application received" email, then adds the tag "screened"');
  });

  it('says when it runs, for the triggers that are not the stage itself', () => {
    const words = (trigger: Automation['trigger']) => automationWords(automation('x', trigger, { actions: [{ type: 'NotifyTeam' }] }), flow, names);

    expect(words({ type: 'SlaBreached', stageId: 'phone-screen' })).toBe('Tells the team when the time limit passes');
    expect(words({ type: 'VerificationAssessed' })).toBe('When the checks finish, tells the team');
    expect(words({ type: 'ReviewerDecision' })).toBe('When a reviewer records a decision, tells the team');
    expect(words({ type: 'StageExited', stageId: 'interview' })).toBe('When they leave, tells the team');
    expect(words({ type: 'TimeElapsed', stageId: 'interview', afterHours: 48 })).toBe('48 hours after they arrive, tells the team');
    expect(words({ type: 'QuestionnaireCompleted', questionnaireId: 'availability' })).toBe('When they answer "Availability", tells the team');
    expect(words({ type: 'QuestionnaireCompleted' })).toBe('When they answer a form, tells the team');
    expect(words({ type: 'InterviewScheduled' })).toBe('When an interview is booked, tells the team');
    expect(words({ type: 'Manual' })).toBe('When someone runs it, tells the team');
  });

  it('puts a question condition first, as "If the answer to ..."', () => {
    const queued = automation('q', { type: 'ApplicationSubmitted' }, {
      conditions: { match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }] },
      actions: [{ type: 'QueueForRejectionReview' }],
    });

    expect(automationWords(queued, flow, names)).toBe('If the answer to "Do you have the right to work here?" is no, queues them for the team to look at first');
  });

  it('adds "and the other conditions match" when there is more than one condition, and "the conditions match" when it cannot say one plainly', () => {
    const rules = [
      { fact: 'answers.right_to_work', operator: 'EqualTo' as const, value: 'no' },
      { fact: 'application.source', operator: 'EqualTo' as const, value: 'Recruiter' },
    ];
    const several = automation('s', { type: 'StageEntered', stageId: 'interview' }, { conditions: { match: 'All', rules }, actions: [{ type: 'NotifyTeam' }] });
    const other = automation('o', { type: 'StageEntered', stageId: 'interview' }, { conditions: { match: 'All', rules: [rules[1]!] }, actions: [{ type: 'NotifyTeam' }] });

    expect(automationWords(several, flow, names)).toBe('If the answer to "Do you have the right to work here?" is no and the other conditions match, tells the team');
    expect(automationWords(other, flow, names)).toBe('If the conditions match, tells the team');
  });

  it('says a verification level in the words the screens use, Review or Verify, and never asks to turn anyone down', () => {
    const review = automation('r', { type: 'VerificationAssessed' }, {
      conditions: { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'EqualTo', value: 'ReviewSuggested' }] },
      actions: [{ type: 'AssignReviewer' }],
    });

    const words = automationWords(review, flow, names);

    expect(words).toBe('When the checks finish, if verification is Review, asks the team to review');
    expect(words).not.toMatch(/reject|turn.* down/i);
  });

  it('says Verify for the level that recommends verification, and Clear for no concerns', () => {
    const route = automation('r', { type: 'VerificationAssessed' }, {
      conditions: { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'In', values: ['ReviewSuggested', 'VerificationRecommended'] }] },
      actions: [{ type: 'AssignReviewer' }],
    });

    expect(automationWords(route, flow, names)).toBe('When the checks finish, if verification is Review or Verify, asks the team to review');
    const clear = automation('c', { type: 'VerificationAssessed' }, { conditions: { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'EqualTo', value: 'NoConcerns' }] } });
    expect(automationWords(clear, flow, names)).toContain('verification is Clear');
  });
});

describe('where an automation belongs', () => {
  it('is its own stage, or the first stage for an arriving application, and nowhere for the rest', () => {
    expect(homeOf(flow, automation('a', { type: 'StageEntered', stageId: 'interview' }))).toBe('interview');
    expect(homeOf(flow, automation('b', { type: 'SlaBreached', stageId: 'phone-screen' }))).toBe('phone-screen');
    expect(homeOf(flow, automation('c', { type: 'ApplicationSubmitted' }))).toBe('applied');
    expect(homeOf(flow, automation('d', { type: 'Manual' }))).toBeNull();
    expect(homeOf(flow, automation('e', { type: 'VerificationAssessed' }))).toBeNull();
  });

  it('lists a stage\'s automations that are on, with their place in the workflow', () => {
    const list = workflowWith([
      automation('first', { type: 'ApplicationSubmitted' }),
      automation('off', { type: 'ApplicationSubmitted' }, { enabled: false }),
      automation('other', { type: 'StageEntered', stageId: 'interview' }),
      automation('second', { type: 'ApplicationSubmitted' }, { actions: [{ type: 'NotifyTeam' }] }),
    ]);

    expect(automationsAt(list, 'applied', names)).toEqual([
      { index: 0, text: 'Adds the tag "first"' },
      { index: 3, text: 'Tells the team' },
    ]);
    expect(automationsAt(list, 'phone-screen', names)).toEqual([]);
  });
});

describe('journeyOf', () => {
  const list = workflowWith([
    automation('welcome', { type: 'ApplicationSubmitted' }, { actions: [{ type: 'SendEmail', emailTemplate: 'application-received' }] }),
    automation('screened', { type: 'StageEntered', stageId: 'phone-screen' }),
    automation('off', { type: 'StageEntered', stageId: 'phone-screen' }, { enabled: false }),
    automation('route', { type: 'VerificationAssessed' }, { actions: [{ type: 'AssignReviewer', assigneeUserId: 'someone' }] }),
    automation('sorry', { type: 'StageEntered', stageId: 'rejected' }, { actions: [{ type: 'SendEmail', emailTemplate: 'application-rejected' }] }),
  ]);

  it('has a numbered column per open stage, and none for a stage that ends the candidacy', () => {
    const { columns } = journeyOf(list, names);

    expect(columns.map((column) => [column.number, column.stage.name])).toEqual([
      [1, 'Applied'],
      [2, 'Phone screen'],
      [3, 'Interview'],
    ]);
  });

  it('gives each column its limit and the automations that are on there, in the order the workflow lists them', () => {
    const { columns } = journeyOf(list, names);

    expect(columns.map((column) => column.limit)).toEqual([null, '3-day limit', null]);
    expect(columns[0]!.automations).toEqual(['Sends the "Application received" email']);
    expect(columns[1]!.automations).toEqual(['Adds the tag "screened"']);
    expect(columns[2]!.automations).toEqual([]);
  });

  it('counts the apply form\'s questions on the first column only, and none when there are none', () => {
    expect(journeyOf(list, names).columns.map((column) => column.questions)).toEqual([1, null, null]);
    expect(journeyOf(workflowWith([], { questionnaire: { questions: [] } }), names).columns.every((column) => column.questions === null)).toBe(true);
  });

  it('puts an automation that belongs to no stage under any time, never naming who it asks', () => {
    const { anytime } = journeyOf(list, names);

    expect(anytime).toEqual(['When the checks finish, asks the team to review']);
    expect(anytime.join(' ')).not.toContain('someone');
  });

  it('still shows an automation whose stage is gone, under any time, rather than nowhere', () => {
    const orphan = automation('orphan', { type: 'StageEntered', stageId: 'removed-stage' });

    expect(journeyOf(workflowWith([orphan]), names).anytime).toEqual([automationWords(orphan, workflowWith([orphan]), names)]);
  });

  it('lists the end stages, each with what happens there on the same line', () => {
    expect(journeyOf(list, names).ends).toEqual(['Hired', 'Not progressing, sends the "Not this time" email']);
  });

  it('is empty for a workflow with no automations, with the stages and their ends still there', () => {
    const empty = journeyOf(workflowWith([]), names);

    expect(empty.anytime).toEqual([]);
    expect(empty.columns).toHaveLength(3);
    expect(empty.ends).toEqual(['Hired', 'Not progressing']);
  });
});
