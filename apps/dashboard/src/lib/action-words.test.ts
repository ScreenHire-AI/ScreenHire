// A step in plain words (action-words.ts), as the Test drive says it. Generated text never names a person
// (docs/design/redesign-2026-11.md, Setup picks 1): a reviewer is "the team".
import type { Automation, HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { actionWords } from './action-words.js';

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
