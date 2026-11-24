// What the Pipeline tab counts and names: a stage's chips, the automations its sheet lists by name, an automation as a short
// label, and what runs when a time limit passes.
import type { Automation, HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { anyStageChips, automationLabel, chipsEmpty, respondWithin, stageAutomations, stageChips, whenOverdue } from './stage-facts.js';

const automation = (id: string, trigger: Automation['trigger'], actions: Automation['actions'], extra: Partial<Automation> = {}): Automation => ({
  id,
  name: id,
  trigger,
  conditions: null,
  actions,
  ...extra,
});

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'screen', name: 'Phone screen', kind: 'Screening', slaDays: 3, meetingName: 'phone screen' },
    { id: 'trial', name: 'Trial', kind: 'Assessment', slaDays: 7, meetingName: 'trial shift' },
    { id: 'rejected', name: 'Rejected', kind: 'Rejected' },
  ],
  questionnaire: {
    questions: [
      { id: 'right_to_work', label: 'Right to work?', type: 'YesNo', required: true },
      { id: 'experience', label: 'Experience', type: 'ShortText', required: false },
    ],
  },
  automations: [
    automation('thanks', { type: 'ApplicationSubmitted' }, [{ type: 'SendEmail', emailTemplate: 'application-received' }]),
    automation('knockout', { type: 'ApplicationSubmitted' }, [{ type: 'QueueForRejectionReview', reason: 'No right to work' }], {
      conditions: { match: 'All', rules: [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }] },
    }),
    automation('checks', { type: 'VerificationAssessed' }, [{ type: 'AssignReviewer' }], {
      conditions: { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'In', values: ['ReviewSuggested', 'VerificationRecommended'] }] },
    }),
    automation('reminder-screen', { type: 'SlaBreached', stageId: 'screen' }, [{ type: 'NotifyTeam', note: 'Late' }]),
    automation('tag-left', { type: 'StageExited', stageId: 'screen' }, [{ type: 'AddTag', tag: 'screened' }]),
    automation('off', { type: 'StageEntered', stageId: 'screen' }, [{ type: 'AddTag', tag: 'off' }], { enabled: false }),
    automation('by-hand', { type: 'Manual' }, [{ type: 'CreateTask', note: 'Call them' }]),
    automation('any-late', { type: 'SlaBreached' }, [{ type: 'NotifyTeam', note: 'Late' }]),
    automation('sorry', { type: 'StageEntered', stageId: 'rejected' }, [{ type: 'SendEmail', emailTemplate: 'not-this-time' }]),
  ],
};

const names = new Map([
  ['application-received', 'Thank you'],
  ['not-this-time', 'Not this time'],
]);
const stage = (id: string) => workflow.stages.find((candidate) => candidate.id === id)!;
const labels = (id: string) => automationLabel(workflow.automations.find((candidate) => candidate.id === id)!, workflow, names);
const listed = (id: string) => stageAutomations(workflow, stage(id), names);

describe('respondWithin', () => {
  it('says the time limit as the choices name it', () => {
    expect(respondWithin(3)).toBe('Respond within 3 days');
    expect(respondWithin(7)).toBe('Respond within 1 week');
    expect(respondWithin(1)).toBe('Respond within 1 day');
  });
});

describe('stageAutomations', () => {
  it('lists every automation at the stage by its own name, what it sends or asks left to the Automations tab, with its id for the link', () => {
    const named = { ...workflow, automations: workflow.automations.map((candidate) => (candidate.id === 'thanks' ? { ...candidate, name: 'Say thank you' } : candidate)) };

    expect(stageAutomations(named, stage('applied'), names)).toEqual([
      { text: 'Say thank you', automationId: 'thanks' },
      { text: 'knockout', automationId: 'knockout' },
      { text: 'checks', automationId: 'checks' },
    ]);
  });

  it('says which are off, and leaves the sheet\'s own reminder to When overdue (W7)', () => {
    expect(listed('screen')).toEqual([
      { text: 'tag-left', automationId: 'tag-left' },
      { text: 'off, off', automationId: 'off' },
    ]);
  });

  it('reads one with no name yet as its short label, so a line is never blank', () => {
    const unnamed: HiringWorkflow = { ...workflow, automations: [automation('blank', { type: 'StageEntered', stageId: 'screen' }, [{ type: 'AddTag', tag: 'seen' }], { name: '' })] };

    expect(stageAutomations(unnamed, stage('screen'), names)).toEqual([{ text: 'Tag seen', automationId: 'blank' }]);
  });
});

describe('automationLabel', () => {
  it('is the condition, a comma and the steps, with no person named', () => {
    const reviewer = automation('ask', { type: 'StageEntered', stageId: 'screen' }, [{ type: 'AssignReviewer', assigneeUserId: '01a157d4-0000-7000-8000-0000000000aa' }]);

    expect(automationLabel(reviewer, workflow)).toBe('Team reviews');
  });

  it('says what starts it when that is not arriving, and nothing more when the condition already says the checks finished', () => {
    expect(labels('thanks')).toBe('Sends "Thank you"');
    expect(labels('knockout')).toBe('Right to work: No, team looks first');
    expect(labels('checks')).toBe('Verification: Review or Verify, team reviews');
    expect(labels('by-hand')).toBe('When run by hand: Task for the team');
    expect(labels('any-late')).toBe('When overdue: Tell the team');
  });

  it('can leave out what starts it, for a list that already says it', () => {
    expect(automationLabel(workflow.automations[3]!, workflow, names, false)).toBe('Tell the team');
  });
});

describe('whenOverdue', () => {
  it('says what runs when the stage\'s limit passes, its own reminders and any stage\'s', () => {
    expect(whenOverdue(workflow, 'screen', names).map((fact) => fact.text)).toEqual(['Tell the team', 'Tell the team']);
    expect(whenOverdue(workflow, 'trial', names).map((fact) => fact.automationId)).toEqual(['any-late']);
  });
});

describe('stageChips', () => {
  it('counts the first stage: its automations by their own names, the emails it sends by template name, and the application form\'s questions', () => {
    const chips = stageChips(workflow, stage('applied'), names);

    expect(chips.automations).toEqual([
      { id: 'knockout', name: 'knockout' },
      { id: 'checks', name: 'checks' },
    ]);
    expect(chips.emails).toEqual([{ automationId: 'thanks', name: 'Thank you' }]);
    expect(chips.forms).toEqual([{ id: 'apply', name: 'Application form', questions: 2 }]);
    expect(chips.questions).toBe(2);
    expect(chips.limitDays).toBeUndefined();
  });

  it('folds the stage\'s own reminder into its time limit and leaves out an automation that is off', () => {
    const chips = stageChips(workflow, stage('screen'), names);

    expect(chips.limitDays).toBe(3);
    expect(chips.automations.map((automation) => automation.id)).toEqual(['tag-left']);
  });

  it('counts a form sent at the stage with its questions, and names an automation with no name by its short label', () => {
    const withForm: HiringWorkflow = {
      ...workflow,
      followUpQuestionnaires: [{ id: 'screening', name: 'Screening questionnaire', questions: [1, 2, 3].map((n) => ({ id: `q${n}`, label: `Q${n}`, type: 'ShortText', required: false })) }],
      automations: [
        automation('send-screening', { type: 'StageEntered', stageId: 'screen' }, [{ type: 'SendQuestionnaire', questionnaireId: 'screening' }]),
        automation('unnamed', { type: 'StageEntered', stageId: 'screen' }, [{ type: 'AddTag', tag: 'vip' }], { name: '  ' }),
      ],
    };

    const chips = stageChips(withForm, stage('screen'), names);

    expect(chips.forms).toEqual([{ id: 'screening', name: 'Screening questionnaire', questions: 3 }]);
    expect(chips.questions).toBe(3);
    expect(chips.automations).toEqual([{ id: 'unnamed', name: 'Tag vip' }]);
  });

  it('counts an automation that sends a form no longer in the workflow by its name, so the stage never says nothing happens', () => {
    const orphan: HiringWorkflow = {
      ...workflow,
      followUpQuestionnaires: [],
      automations: [automation('send-gone', { type: 'StageEntered', stageId: 'screen' }, [{ type: 'SendQuestionnaire', questionnaireId: 'gone' }], { name: 'Send the old form' })],
    };

    const chips = stageChips(orphan, stage('screen'), names);

    expect(chips.forms).toEqual([]);
    expect(chips.automations).toEqual([{ id: 'send-gone', name: 'Send the old form' }]);
    expect(chipsEmpty({ ...chips, limitDays: undefined, scorecard: false })).toBe(false);
  });

  it('gives a stage that ends a candidacy no time limit or scorecard, only what it sends', () => {
    const chips = stageChips({ ...workflow, stages: workflow.stages.map((s) => (s.id === 'rejected' ? { ...s, slaDays: 2, scorecardTemplateId: 'x' } : s)) }, stage('rejected'), names);

    expect(chips).toEqual({ limitDays: undefined, automations: [], emails: [{ automationId: 'sorry', name: 'Not this time' }], forms: [], questions: 0, scorecard: false });
  });

  it('says a stage with nothing on it is empty', () => {
    const empty = stageChips({ ...workflow, automations: [], questionnaire: { questions: [] } }, stage('applied'), names);

    expect(chipsEmpty(empty)).toBe(true);
    expect(chipsEmpty(stageChips(workflow, stage('screen'), names))).toBe(false);
  });

  it('counts the automations that belong to no stage by their own names', () => {
    expect(anyStageChips(workflow, names).automations).toEqual([
      { id: 'by-hand', name: 'by-hand' },
      { id: 'any-late', name: 'any-late' },
    ]);
  });
});
