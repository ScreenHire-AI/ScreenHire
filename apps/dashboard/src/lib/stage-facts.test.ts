// The Pipeline tab's short facts: a stage read back as one fact a line, an automation as a short label, the automations
// that belong to no stage and the stages that end a candidacy.
import type { Automation, HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { anyStageChips, anyStageFacts, automationLabel, chipsEmpty, respondWithin, stageChips, stageFacts, whenOverdue } from './stage-facts.js';

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
const texts = (id: string) => stageFacts(workflow, stage(id), names).map((fact) => fact.text);

describe('respondWithin', () => {
  it('says the time limit as the choices name it', () => {
    expect(respondWithin(3)).toBe('Respond within 3 days');
    expect(respondWithin(7)).toBe('Respond within 1 week');
    expect(respondWithin(1)).toBe('Respond within 1 day');
  });
});

describe('stageFacts', () => {
  it('puts what happens on arriving and when the checks finish on the first stage, then how many questions it asks', () => {
    expect(texts('applied')).toEqual(['Sends "Thank you"', 'Right to work: No, team looks first', 'Verification: Review or Verify, team reviews', 'Asks 2 questions']);
  });

  it('says the time limit, leaves out its own reminder and anything switched off, and prefixes a trigger that is not arriving', () => {
    expect(texts('screen')).toEqual(['Respond within 3 days', 'When they leave: Tag screened']);
  });

  it('names the meeting only when it is not the stage\'s own name', () => {
    expect(texts('trial')).toEqual(['Respond within 1 week', 'Meeting: trial shift']);
  });

  it('gives an end stage no time limit, and the email it sends', () => {
    expect(texts('rejected')).toEqual(['Sends "Not this time"']);
  });

  it('carries the automation id on each automation and email line, for its link', () => {
    expect(stageFacts(workflow, stage('applied'), names).map((fact) => fact.automationId)).toEqual(['thanks', 'knockout', 'checks', undefined]);
  });
});

describe('automationLabel', () => {
  it('is the condition, a comma and the steps, with no person named', () => {
    const reviewer = automation('ask', { type: 'StageEntered', stageId: 'screen' }, [{ type: 'AssignReviewer', assigneeUserId: '01a157d4-0000-7000-8000-0000000000aa' }]);

    expect(automationLabel(reviewer, workflow)).toBe('Team reviews');
  });

  it('can leave out what starts it, for a list that already says it', () => {
    expect(automationLabel(workflow.automations[3]!, workflow, names, false)).toBe('Tell the team');
  });
});

describe('anyStageFacts', () => {
  it('lists the automations that belong to no stage', () => {
    expect(anyStageFacts(workflow, names).map((fact) => fact.text)).toEqual(['When run by hand: Task for the team', 'When overdue: Tell the team']);
  });

  it('gives a stage that ends a candidacy what runs there', () => {
    const rejected = workflow.stages.find((stage) => stage.name === 'Rejected')!;
    expect(stageFacts(workflow, rejected, names).map((fact) => fact.text)).toEqual(['Sends "Not this time"']);
  });
});

describe('whenOverdue', () => {
  it('says what runs when the stage\'s limit passes, its own reminders and any stage\'s', () => {
    expect(whenOverdue(workflow, 'screen', names).map((fact) => fact.text)).toEqual(['Tell the team', 'Tell the team']);
    expect(whenOverdue(workflow, 'trial', names).map((fact) => fact.automationId)).toEqual(['any-late']);
  });
});

describe('stageFacts, a stage that sends a form', () => {
  it('says "Sends" with the form\'s name, as its own fact that links to the automation behind it', () => {
    const withForm: HiringWorkflow = {
      ...workflow,
      followUpQuestionnaires: [{ id: 'screening', name: 'Screening questionnaire', questions: [{ id: 'q_a', label: 'A?', type: 'ShortText', required: false }] }],
      automations: [automation('send-screening', { type: 'StageEntered', stageId: 'screen' }, [{ type: 'SendQuestionnaire', questionnaireId: 'screening' }])],
    };

    expect(stageFacts(withForm, stage('screen'), names)).toEqual([
      { kind: 'limit', text: 'Respond within 3 days' },
      { kind: 'form', text: 'Sends "Screening questionnaire"', automationId: 'send-screening' },
    ]);
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
