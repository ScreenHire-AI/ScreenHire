import type { HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import {
  actionLabels,
  actionTypes,
  actionsFor,
  answerIsRead,
  describeAction,
  describePath,
  errorsUnder,
  factsFor,
  canRemindIn,
  hasReminder,
  idFrom,
  insertionIndex,
  insertStage,
  kindFromName,
  laneOf,
  newAction,
  newAutomation,
  newQuestion,
  newRule,
  newStage,
  newStageName,
  operatorsFor,
  problemErrors,
  reminderId,
  ruleOperand,
  summarize,
  syncReminder,
  timeLimitChoices,
  timeLimitLabel,
  triggerLabels,
  triggerTypes,
  withQuestionType,
  withoutStage,
  workflowPath,
} from './workflow.js';
import { zoneName } from './report-range.js';

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'screening', name: 'Screening', kind: 'Screening', slaDays: 3 },
    { id: 'rejected', name: 'Rejected', kind: 'Rejected' },
  ],
  questionnaire: {
    questions: [
      { id: 'work_authorization', type: 'YesNo', label: 'Right to work?', required: true, knockout: { disqualifyingAnswer: false, reason: null } },
      {
        id: 'shift',
        type: 'SingleChoice',
        label: 'Shift',
        options: [
          { id: 'days', label: 'Days' },
          { id: 'nights', label: 'Nights' },
        ],
      },
    ],
  },
  automations: [
    {
      id: 'tag_on_entry',
      name: 'Tag on entry',
      trigger: { type: 'StageEntered', stageId: 'screening' },
      conditions: { match: 'All', rules: [{ fact: 'answers.work_authorization', operator: 'EqualTo', value: 'no' }] },
      actions: [{ type: 'AddTag', tag: 'check' }],
    },
    { id: 'manual', name: 'By hand', trigger: { type: 'Manual' }, conditions: null, actions: [{ type: 'NotifyTeam', note: 'Look' }] },
  ],
};

describe('an answer an automation reads', () => {
  it('is one a condition names, so a knockout nobody reacts to can be told apart', () => {
    expect(answerIsRead(workflow, 'work_authorization')).toBe(true);
    expect(answerIsRead(workflow, 'shift')).toBe(false);
    expect(answerIsRead({ ...workflow, automations: [] }, 'work_authorization')).toBe(false);
    // An automation that is switched off does nothing with the answer, so the knockout still says nothing happens.
    const off = { ...workflow, automations: workflow.automations.map((automation) => ({ ...automation, enabled: false })) };
    expect(answerIsRead(off, 'work_authorization')).toBe(false);
  });
});

describe('facts', () => {
  it('offers one fact per question with its values, then the application facts and the verification levels', () => {
    const facts = factsFor(workflow);

    expect(facts.map((fact) => fact.fact)).toEqual([
      'answers.work_authorization',
      'answers.shift',
      'application.tags',
      'application.source',
      'application.hoursInStage',
      'verification.authenticity',
      'verification.aiAssistance',
    ]);
    expect(facts[0]!.values?.map((value) => value.value)).toEqual(['yes', 'no']);
    expect(facts[1]!.values?.map((value) => value.value)).toEqual(['days', 'nights']);
    expect(facts.filter((fact) => fact.signal).map((fact) => fact.fact)).toEqual(['verification.authenticity', 'verification.aiAssistance']);
    expect(facts.find((fact) => fact.fact === 'verification.authenticity')!.values?.map((value) => value.value)).toEqual([
      'NoConcerns',
      'ReviewSuggested',
      'VerificationRecommended',
    ]);
  });

  it('allows verification levels equality only, never a comparison (ADR-0004)', () => {
    expect(operatorsFor('Level')).toEqual(['EqualTo', 'In', 'Exists']);
    expect(operatorsFor('YesNo')).toEqual(['EqualTo', 'Exists']);
  });

  it('gives a new rule the operand its operator takes', () => {
    const level = factsFor(workflow).find((fact) => fact.fact === 'verification.authenticity')!;

    expect(newRule(level)).toEqual({ fact: 'verification.authenticity', operator: 'EqualTo', value: 'NoConcerns' });
    expect(ruleOperand('In', level)).toEqual({ values: ['NoConcerns'] });
    expect(ruleOperand('Exists', level)).toEqual({});
  });
});

describe('scheduling an interview', () => {
  it('offers the action and the interview booked trigger, but not what the engine still refuses', () => {
    expect(actionLabels.ScheduleInterview).toBe('Schedule an interview');
    expect(actionTypes).toContain('ScheduleInterview');
    expect(actionTypes).not.toContain('RequestVerification');
    expect(triggerLabels.InterviewScheduled).toBe('An interview is booked');
    expect(triggerTypes).toContain('InterviewScheduled');
    expect(triggerTypes).not.toContain('VerificationCompleted');
    expect(triggerTypes).not.toContain('CandidateReplied');
  });

  it('starts a new interview action with the editor browser zone and the four fields the validator needs', () => {
    expect(newAction('ScheduleInterview')).toEqual({
      type: 'ScheduleInterview',
      interviewerUserIds: [],
      interviewMinutes: 30,
      interviewWindowDays: 14,
      interviewTimeZone: zoneName(),
    });
    expect(newAction('ScheduleInterview').interviewTimeZone).toBeTruthy();
  });

  it('keeps it out of the actions a passed time limit offers', () => {
    expect(actionsFor('SlaBreached')).not.toContain('ScheduleInterview');
    expect(actionsFor('StageEntered')).toContain('ScheduleInterview');
  });

  it('names the interviewers, and says when an automation waits for a booking', () => {
    const members = [
      { userId: 'u1', displayName: 'Avery Quinn' },
      { userId: 'u2', displayName: 'Robin Ashby' },
    ];
    const action = { type: 'ScheduleInterview' as const, interviewerUserIds: ['u1', 'u2', 'gone'], interviewMinutes: 30, interviewWindowDays: 14, interviewTimeZone: 'UTC' };

    expect(describeAction(action, workflow, members)).toBe('Schedule an interview with Avery Quinn, Robin Ashby, a teammate');
    expect(describeAction({ type: 'ScheduleInterview' }, workflow, members)).toBe('Schedule an interview');
    expect(summarize({ id: 'booked', name: 'Booked', trigger: { type: 'InterviewScheduled' }, conditions: null, actions: [{ type: 'AddTag', tag: 'booked' }] }, workflow)).toBe(
      'When an interview is booked: add the tag "booked".',
    );
  });
});

describe('elements', () => {
  it('makes a fresh action carry only the field its type needs', () => {
    expect(newAction('AddTag')).toEqual({ type: 'AddTag', tag: '' });
    expect(newAction('Reject')).toEqual({ type: 'Reject', reason: '' });
    expect(newAction('AssignReviewer')).toEqual({ type: 'AssignReviewer' });
    expect(newAction('SendEmail')).toEqual({ type: 'SendEmail', emailTemplate: 'application-received' });
    expect(newAction('MoveToStage', workflow.stages)).toEqual({ type: 'MoveToStage', stageId: 'applied' });
  });

  it('makes ids that are valid and unused', () => {
    expect(idFrom('Right to work (UK)', [], 'question')).toBe('right_to_work_uk');
    expect(idFrom('Right to work', ['right_to_work', 'right_to_work_2'], 'question')).toBe('right_to_work_3');
    expect(idFrom('!!!', [], 'question')).toBe('question');
  });

  it('drops what a question type does not take when the type changes', () => {
    const yesNo = workflow.questionnaire.questions[0]!;

    const asText = withQuestionType(yesNo, 'ShortText');
    const asChoice = withQuestionType(yesNo, 'MultiChoice');

    expect(asText).toEqual({ id: 'work_authorization', type: 'ShortText', label: 'Right to work?', required: true, helpText: null });
    expect(asChoice.options).toHaveLength(2);
    expect(asChoice.knockout).toBeUndefined();
  });

  it('starts an automation on its lane: a stage entry, or an arriving application', () => {
    expect(newAutomation(workflow, 'screening').trigger).toEqual({ type: 'StageEntered', stageId: 'screening' });
    expect(newAutomation(workflow, null).trigger).toEqual({ type: 'ApplicationSubmitted' });
    expect(newAutomation(workflow, null).id).toBe('automation');
    // Complete from the start, and valid under every trigger, so "Write your own" never opens as a problem.
    expect(newAutomation(workflow, 'screening').actions).toEqual([{ type: 'NotifyTeam', note: 'Someone needs a look.' }]);
  });

  it('puts stage triggers in their stage lane and the rest in the whole-workflow lane', () => {
    expect(workflow.automations.map(laneOf)).toEqual(['screening', null]);
  });
});

describe('validator paths', () => {
  it('reads a save problem relative to the workflow, whatever the request called it', () => {
    expect(workflowPath('definition.workflow.automations[0].actions[0]')).toBe('automations[0].actions[0]');
    expect(workflowPath('workflow.stages')).toBe('stages');
    expect(problemErrors({ errors: { 'workflow.stages': ['Screening still has 1 candidate.'] } })).toEqual([
      { path: 'stages', message: 'Screening still has 1 candidate.' },
    ]);
    expect(problemErrors({ detail: 'nope' })).toEqual([]);
  });

  it('finds the errors inside an element without catching its neighbours', () => {
    const errors = [
      { path: 'automations[1].actions[0]', message: 'a' },
      { path: 'automations[10].name', message: 'b' },
      { path: 'automations[1]', message: 'c' },
    ];

    expect(errorsUnder(errors, 'automations[1]').map((error) => error.message)).toEqual(['a', 'c']);
  });

  it('words where an error is', () => {
    expect(describePath(workflow, 'automations[0].actions[0]')).toBe('The step "Add a tag" in "Tag on entry"');
    expect(describePath(workflow, 'automations[0].conditions.rules[0]')).toBe('Condition 1 in "Tag on entry"');
    expect(describePath(workflow, 'automations[0].name')).toBe('The automation "Tag on entry"');
    expect(describePath(workflow, 'questionnaire.questions[1].options')).toBe('The question "Shift"');
    expect(describePath(workflow, 'stages[1].name')).toBe('The stage "Screening"');
    expect(describePath(workflow, 'stages')).toBe('The stages');
    expect(describePath(workflow, 'questionnaire')).toBe('The questions');
    expect(describePath(workflow, 'anythingElse')).toBe('The workflow');
  });

  it('names the reviewer an AssignReviewer action asks, or the team', () => {
    const members = [{ userId: 'u2', displayName: 'Robin Ashby' }];
    expect(describeAction({ type: 'AssignReviewer', assigneeUserId: 'u2' }, workflow, members)).toBe('Ask Robin Ashby to review');
    expect(describeAction({ type: 'AssignReviewer', assigneeUserId: 'gone' }, workflow, members)).toBe('Ask a teammate to review');
    expect(describeAction({ type: 'AddTag', tag: 'Check closely' }, workflow, members)).toBe('Add the tag "Check closely"');
    // A tag nobody has named yet reads as unfinished, never as an empty pair of quotes.
    expect(describeAction({ type: 'AddTag' }, workflow, members)).toBe('Add a tag (no name yet)');
    expect(describeAction({ type: 'AddTag', tag: '  ' }, workflow, members)).toBe('Add a tag (no name yet)');
    expect(describeAction({ type: 'AssignReviewer' }, workflow, members)).toBe('Ask the team to review');
  });

  it('summarizes an automation in one sentence', () => {
    expect(summarize(workflow.automations[0]!, workflow)).toBe('When a candidate enters Screening, if 1 condition holds: add the tag "check".');
    expect(summarize(workflow.automations[1]!, workflow)).toBe('When someone runs it by hand: notify the team.');
  });
});

describe('stage time limits', () => {
  const timed: HiringWorkflow = {
    ...workflow,
    automations: [
      { id: 'any', name: 'Any', trigger: { type: 'SlaBreached' }, conditions: null, actions: [{ type: 'CreateTask', note: 'Reply' }] },
      { id: 'screen', name: 'Screen', trigger: { type: 'SlaBreached', stageId: 'screening' }, conditions: null, actions: [{ type: 'AddTag', tag: 'overdue' }] },
    ],
  };

  it('offers a passed time limit only the reminders, keeping a saved action outside them so its error shows', () => {
    expect(actionsFor('SlaBreached')).toEqual(['AddTag', 'AssignReviewer', 'NotifyTeam', 'CreateTask']);
    expect(actionsFor('SlaBreached', 'Reject')).toEqual(['AddTag', 'AssignReviewer', 'NotifyTeam', 'CreateTask', 'Reject']);
    expect(actionsFor('StageEntered')).toContain('QueueForRejectionReview');
  });

  it("puts a stage's time limit in its lane and any stage's in the whole workflow's", () => {
    expect(laneOf(timed.automations[0]!)).toBeNull();
    expect(laneOf(timed.automations[1]!)).toBe('screening');
    expect(summarize(timed.automations[0]!, timed)).toBe("When a stage's time limit passes: create a task.");
    expect(summarize(timed.automations[1]!, timed)).toBe('When the time limit in Screening passes: add the tag "overdue".');
  });
});

describe('scorecards and the reviewer decision', () => {
  const reviewed: HiringWorkflow = {
    ...workflow,
    stages: workflow.stages.map((stage) => (stage.id === 'screening' ? { ...stage, scorecardTemplateId: 'screening' } : stage)),
    automations: [
      { id: 'any', name: 'Any', trigger: { type: 'ReviewerDecision' }, conditions: null, actions: [{ type: 'AddTag', tag: 'reviewed' }] },
      { id: 'screen', name: 'Screen', trigger: { type: 'ReviewerDecision', stageId: 'screening' }, conditions: null, actions: [{ type: 'AddTag', tag: 'screened' }] },
    ],
  };

  it("offers the reviewer's recommendation only to a reviewer-decision automation", () => {
    expect(factsFor(reviewed).map((fact) => fact.fact)).not.toContain('reviewer.recommendation');
    const recommendation = factsFor(reviewed, 'ReviewerDecision').find((fact) => fact.fact === 'reviewer.recommendation');
    expect(recommendation?.signal).toBe(false);
    expect(recommendation?.values?.map((value) => value.value)).toEqual(['StrongNo', 'No', 'Yes', 'StrongYes']);
    expect(operatorsFor(recommendation!.type)).toEqual(['EqualTo', 'In', 'Exists']);
  });

  it("puts a stage's reviewer decisions in its lane and the rest in the whole workflow's", () => {
    expect(laneOf(reviewed.automations[0]!)).toBeNull();
    expect(laneOf(reviewed.automations[1]!)).toBe('screening');
    expect(summarize(reviewed.automations[1]!, reviewed)).toBe('When a reviewer records a decision on Screening: add the tag "screened".');
    expect(summarize(reviewed.automations[0]!, reviewed)).toBe('When a reviewer records a decision: add the tag "reviewed".');
  });
});

describe('follow-up questionnaires', () => {
  const withFollowUp: HiringWorkflow = {
    ...workflow,
    followUpQuestionnaires: [
      {
        id: 'availability',
        name: 'Availability',
        questions: [{ id: 'question', type: 'YesNo', label: 'Forklift licence?', required: true, knockout: { disqualifyingAnswer: false } }],
      },
    ],
    automations: [
      {
        id: 'send',
        name: 'Send it',
        trigger: { type: 'StageEntered', stageId: 'screening' },
        conditions: null,
        actions: [{ type: 'SendQuestionnaire', questionnaireId: 'availability' }],
      },
      { id: 'answered', name: 'Answered', trigger: { type: 'QuestionnaireCompleted', questionnaireId: 'availability' }, conditions: null, actions: [{ type: 'AddTag', tag: 'answered' }] },
    ],
  };

  it("offers a follow-up's answers as facts, named by their questionnaire", () => {
    const fact = factsFor(withFollowUp).find((option) => option.fact === 'answers.question');
    expect(fact).toMatchObject({ label: 'Answer (Availability): Forklift licence?', type: 'YesNo' });
    expect(factsFor(withFollowUp).find((option) => option.fact === 'application.source')?.values?.map((value) => value.value)).toEqual(['CareersSite', 'Recruiter']);
  });

  it('gives a new question an id no questionnaire of the workflow uses', () => {
    expect(newQuestion(withFollowUp).id).toBe('question_2');
  });

  it('starts a SendQuestionnaire action on the first follow-up, and says what each part does', () => {
    expect(newAction('SendQuestionnaire', withFollowUp.stages, withFollowUp.followUpQuestionnaires ?? [])).toEqual({ type: 'SendQuestionnaire', questionnaireId: 'availability' });
    expect(summarize(withFollowUp.automations[0]!, withFollowUp)).toBe('When a candidate enters Screening: send the "Availability" questionnaire.');
    expect(summarize(withFollowUp.automations[1]!, withFollowUp)).toBe('When the candidate answers "Availability": add the tag "answered".');
    expect(summarize({ ...withFollowUp.automations[1]!, trigger: { type: 'QuestionnaireCompleted' } }, withFollowUp)).toBe(
      'When a follow-up questionnaire is answered: add the tag "answered".',
    );
  });

  it("names a follow-up's problems by the questionnaire and its question", () => {
    expect(describePath(withFollowUp, 'followUpQuestionnaires[0].name')).toBe('The form "Availability"');
    expect(describePath(withFollowUp, 'followUpQuestionnaires[0].questions[0].id')).toBe('The question "Forklift licence?" in "Availability"');
  });
});

describe('kindFromName', () => {
  // A stage's kind is what its name says, so a person who types "Trial shift" never meets the word "Assessment". The
  // first rule that matches wins, in the order below, and a name that says nothing is Custom.
  it.each([
    ['Phone screen', 'Screening'],
    ['Case review', 'Screening'],
    ['Interview', 'Interview'],
    ['Final interview', 'Interview'],
    ['Offer', 'Offer'],
    ['Offer sent', 'Offer'],
    ['Reference check', 'Verification'],
    ['Background check', 'Verification'],
    ['Trial shift', 'Assessment'],
    ['Skills test', 'Assessment'],
    ['Take-home task', 'Assessment'],
    ['Group session', 'Assessment'],
    ['Hired', 'Hired'],
    ['Started', 'Hired'],
    ['Onboarding', 'Hired'],
    ['Rejected', 'Rejected'],
    ['Not selected', 'Rejected'],
    ['Declined', 'Rejected'],
    ['Withdrawn', 'Withdrawn'],
    ['Team chat', 'Custom'],
    ['New stage', 'Custom'],
    ['', 'Custom'],
  ] as const)('reads "%s" as %s', (name, kind) => {
    expect(kindFromName(name)).toBe(kind);
  });

  it('ignores case', () => {
    expect(kindFromName('PHONE SCREEN')).toBe('Screening');
    expect(kindFromName('trial SHIFT')).toBe('Assessment');
  });

  it('lets the word that ends a candidacy win over the one it contains', () => {
    // "Not hired" holds "hired", and "Interview declined" holds "interview": the ending is what the stage is.
    expect(kindFromName('Not hired')).toBe('Rejected');
    expect(kindFromName('Interview declined')).toBe('Rejected');
    expect(kindFromName('Withdrawn after the offer')).toBe('Withdrawn');
  });

  it('never gives the Applied kind, which only the first stage has', () => {
    expect(kindFromName('Applied')).toBe('Custom');
    expect(kindFromName('Application review')).toBe('Screening');
  });
});

describe('new stages', () => {
  it('starts as New stage, a custom stage, with an id the workflow does not use', () => {
    expect(newStage(workflow)).toEqual({ id: 'stage', name: 'New stage', kind: 'Custom' });
    expect(newStage({ ...workflow, stages: [...workflow.stages, { id: 'stage', name: 'A', kind: 'Custom' }] }).id).toBe('stage_2');
    expect(newStageName).toBe('New stage');
  });

  it('takes its kind from the name it is given', () => {
    expect(newStage(workflow, 'Trial shift')).toMatchObject({ name: 'Trial shift', kind: 'Assessment' });
    expect(newStage(workflow, 'New stage').kind).toBe('Custom');
  });
});

describe('placing stages', () => {
  const stages = [
    { id: 'applied', name: 'Applied', kind: 'Applied' as const },
    { id: 'screening', name: 'Screening', kind: 'Screening' as const },
    { id: 'interview', name: 'Interview', kind: 'Interview' as const },
    { id: 'hired', name: 'Hired', kind: 'Hired' as const },
    { id: 'rejected', name: 'Rejected', kind: 'Rejected' as const },
  ];
  const extra = { id: 'extra', name: 'Extra', kind: 'Custom' as const };

  it('puts a stage right after the one it is added after', () => {
    expect(insertionIndex(stages, 0)).toBe(1);
    expect(insertionIndex(stages, 1)).toBe(2);
  });

  it('keeps a new stage before the stages that end the candidacy', () => {
    expect(insertionIndex(stages, 2)).toBe(3);
    expect(insertionIndex(stages, 3)).toBe(3);
    expect(insertionIndex(stages, 4)).toBe(3);
    expect(insertionIndex(stages, 99)).toBe(3);
  });

  it('puts a stage at the end when no stage ends the candidacy', () => {
    expect(insertionIndex(stages.slice(0, 3), 9)).toBe(3);
  });

  it('inserts at the index, never before the first stage, and never past the end', () => {
    expect(insertStage(stages, 2, extra).map((stage) => stage.id)).toEqual(['applied', 'screening', 'extra', 'interview', 'hired', 'rejected']);
    expect(insertStage(stages, 0, extra).map((stage) => stage.id)).toEqual(['applied', 'extra', 'screening', 'interview', 'hired', 'rejected']);
    expect(insertStage(stages, 40, extra).at(-1)).toBe(extra);
    expect(stages).toHaveLength(5);
  });

  it('removes a stage with the reminder its time limit made, and no other automation', () => {
    const timed: HiringWorkflow = {
      ...workflow,
      automations: [
        ...workflow.automations,
        { id: reminderId('screening'), name: 'Remind', trigger: { type: 'SlaBreached', stageId: 'screening' }, conditions: null, actions: [{ type: 'NotifyTeam' }] },
      ],
    };

    const without = withoutStage(timed, 1);

    expect(without.stages.map((stage) => stage.id)).toEqual(['applied', 'rejected']);
    expect(without.automations.map((automation) => automation.id)).toEqual(['tag_on_entry', 'manual']);
    // Another stage's removal leaves the reminder, and an index with no stage changes nothing.
    expect(withoutStage(timed, 2).automations).toHaveLength(3);
    expect(withoutStage(timed, 7).stages).toHaveLength(3);
    expect(timed.stages).toHaveLength(3);
  });
});

describe('time limits and their reminder', () => {
  const remindFrom = (overrides: Partial<HiringWorkflow> = {}): HiringWorkflow => ({ ...workflow, automations: [], ...overrides });
  const personsReminder = {
    id: 'mine',
    name: 'Mine',
    trigger: { type: 'SlaBreached' as const, stageId: 'screening' },
    conditions: null,
    actions: [{ type: 'NotifyTeam' as const, note: 'Late' }],
  };

  it('offers None, 2, 3 and 5 days and 1 week, and says any other number in days', () => {
    expect(timeLimitChoices.map((choice) => choice.label)).toEqual(['None', '2 days', '3 days', '5 days', '1 week']);
    expect(timeLimitChoices.map((choice) => choice.days)).toEqual([null, 2, 3, 5, 7]);
    expect(timeLimitLabel(2)).toBe('2 days');
    expect(timeLimitLabel(7)).toBe('1 week');
    expect(timeLimitLabel(10)).toBe('10 days');
    expect(timeLimitLabel(1)).toBe('1 day');
  });

  it('names the reminder by its stage', () => {
    expect(reminderId('screening')).toBe('reminder-screening');
  });

  it('gives a stage with a limit one reminder that tells the team when the limit passes', () => {
    const synced = syncReminder(remindFrom(), 'screening');

    expect(synced.automations).toEqual([
      {
        id: 'reminder-screening',
        name: 'Remind the team when Screening waits too long',
        trigger: { type: 'SlaBreached', stageId: 'screening' },
        conditions: null,
        actions: [{ type: 'NotifyTeam', note: 'Someone has waited in Screening longer than the time limit.' }],
        enabled: true,
        allowAutoReject: false,
      },
    ]);
  });

  it('changes nothing once the reminder is in step, and does not edit the workflow it was given', () => {
    const once = syncReminder(remindFrom(), 'screening');
    const original = remindFrom();

    expect(syncReminder(once, 'screening')).toBe(once);
    syncReminder(original, 'screening');
    expect(original.automations).toEqual([]);
  });

  it("adds none when the team is already told: a person's automation, or one for any stage, that is on", () => {
    const own = remindFrom({ automations: [personsReminder] });
    expect(syncReminder(own, 'screening')).toBe(own);
    const anyStage = remindFrom({ automations: [{ ...personsReminder, trigger: { type: 'SlaBreached' } }] });
    expect(syncReminder(anyStage, 'screening')).toBe(anyStage);
  });

  it('adds one when the automation for that stage only tags, is off, or is for another stage', () => {
    const tags = remindFrom({ automations: [{ ...personsReminder, actions: [{ type: 'AddTag', tag: 'late' }] }] });
    const off = remindFrom({ automations: [{ ...personsReminder, enabled: false }] });
    const other = remindFrom({ automations: [{ ...personsReminder, trigger: { type: 'SlaBreached', stageId: 'applied' } }] });

    for (const input of [tags, off, other]) {
      expect(syncReminder(input, 'screening').automations.map((automation) => automation.id)).toContain('reminder-screening');
    }
  });

  it('follows a rename of the stage', () => {
    const synced = syncReminder(remindFrom(), 'screening');
    const renamed: HiringWorkflow = { ...synced, stages: synced.stages.map((stage) => (stage.id === 'screening' ? { ...stage, name: 'Phone screen' } : stage)) };

    expect(syncReminder(renamed, 'screening').automations[0]!.name).toBe('Remind the team when Phone screen waits too long');
  });

  it("removes only its own reminder when the limit goes, keeping a person's automations", () => {
    const synced = syncReminder(remindFrom({ automations: [{ ...personsReminder, enabled: false }] }), 'screening');
    expect(synced.automations.map((automation) => automation.id)).toEqual(['mine', 'reminder-screening']);

    const cleared: HiringWorkflow = { ...synced, stages: synced.stages.map((stage) => (stage.id === 'screening' ? { ...stage, slaDays: null } : stage)) };

    expect(syncReminder(cleared, 'screening').automations.map((automation) => automation.id)).toEqual(['mine']);
  });

  it('leaves a workflow with nothing to remove, an unknown stage, and a stage that ends the candidacy, as it is', () => {
    const none = remindFrom({ stages: workflow.stages.map((stage) => (stage.id === 'screening' ? { ...stage, slaDays: null } : stage)) });
    expect(syncReminder(none, 'screening')).toBe(none);
    expect(syncReminder(remindFrom(), 'nowhere').automations).toEqual([]);
    const ended = remindFrom({ stages: workflow.stages.map((stage) => (stage.id === 'rejected' ? { ...stage, slaDays: 4 } : stage)) });
    expect(syncReminder(ended, 'rejected')).toBe(ended);
  });

  it('cannot remind in a stage a verification check moves candidates into', () => {
    const moves = (trigger: HiringWorkflow['automations'][number]['trigger'], conditions: HiringWorkflow['automations'][number]['conditions'] = null) =>
      remindFrom({ automations: [{ id: 'route', name: 'Route', trigger, conditions, actions: [{ type: 'MoveToStage', stageId: 'screening' }] }] });
    const bySignal = moves({ type: 'ApplicationSubmitted' }, { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'EqualTo', value: 'ReviewSuggested' }] });

    expect(canRemindIn(moves({ type: 'VerificationAssessed' }), 'screening')).toBe(false);
    expect(canRemindIn(bySignal, 'screening')).toBe(false);
    expect(canRemindIn(moves({ type: 'VerificationAssessed' }), 'rejected')).toBe(true);
    expect(canRemindIn(moves({ type: 'ApplicationSubmitted' }), 'screening')).toBe(true);
    // So the sheet adds no reminder there.
    expect(syncReminder(moves({ type: 'VerificationAssessed' }), 'screening').automations.map((automation) => automation.id)).toEqual(['route']);
  });

  it('knows when something already tells the team', () => {
    expect(hasReminder(remindFrom({ automations: [personsReminder] }), 'screening')).toBe(true);
    expect(hasReminder(remindFrom({ automations: [personsReminder] }), 'applied')).toBe(false);
    expect(hasReminder(remindFrom({ automations: [{ ...personsReminder, enabled: false }] }), 'screening')).toBe(false);
    expect(hasReminder(remindFrom({ automations: [{ ...personsReminder, trigger: { type: 'SlaBreached' } }] }), 'applied')).toBe(true);
    expect(hasReminder(remindFrom(), 'screening')).toBe(false);
  });
});
