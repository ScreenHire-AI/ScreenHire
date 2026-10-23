import type { HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import {
  describeAction,
  describePath,
  errorsUnder,
  factsFor,
  idFrom,
  laneOf,
  moveItem,
  newAction,
  newAutomation,
  newRule,
  operatorsFor,
  problemErrors,
  ruleOperand,
  summarize,
  withQuestionType,
  workflowPath,
} from './workflow.js';

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
  integrityPolicy: { aiUse: 'AllowedWithDisclosure' },
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

describe('facts', () => {
  it('offers one fact per question with its values, then the application facts and the integrity levels', () => {
    const facts = factsFor(workflow);

    expect(facts.map((fact) => fact.fact)).toEqual([
      'answers.work_authorization',
      'answers.shift',
      'application.tags',
      'application.source',
      'application.hoursInStage',
      'integrity.authenticity',
      'integrity.aiAssistance',
    ]);
    expect(facts[0]!.values?.map((value) => value.value)).toEqual(['yes', 'no']);
    expect(facts[1]!.values?.map((value) => value.value)).toEqual(['days', 'nights']);
    expect(facts.filter((fact) => fact.signal).map((fact) => fact.fact)).toEqual(['integrity.authenticity', 'integrity.aiAssistance']);
    expect(facts.find((fact) => fact.fact === 'integrity.authenticity')!.values?.map((value) => value.value)).toEqual([
      'NoConcerns',
      'ReviewSuggested',
      'VerificationRecommended',
    ]);
  });

  it('allows integrity levels equality only, never a comparison (ADR-0004)', () => {
    expect(operatorsFor('Level')).toEqual(['EqualTo', 'In', 'Exists']);
    expect(operatorsFor('YesNo')).toEqual(['EqualTo', 'Exists']);
  });

  it('gives a new rule the operand its operator takes', () => {
    const level = factsFor(workflow).find((fact) => fact.fact === 'integrity.authenticity')!;

    expect(newRule(level)).toEqual({ fact: 'integrity.authenticity', operator: 'EqualTo', value: 'NoConcerns' });
    expect(ruleOperand('In', level)).toEqual({ values: ['NoConcerns'] });
    expect(ruleOperand('Exists', level)).toEqual({});
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
  });

  it('puts stage triggers in their stage lane and the rest in the whole-workflow lane', () => {
    expect(workflow.automations.map(laneOf)).toEqual(['screening', null]);
  });

  it('reorders with buttons, staying put at either end', () => {
    expect(moveItem(['a', 'b', 'c'], 0, 1)).toEqual(['b', 'a', 'c']);
    expect(moveItem(['a', 'b', 'c'], 0, -1)).toEqual(['a', 'b', 'c']);
    expect(moveItem(['a', 'b', 'c'], 2, 1)).toEqual(['a', 'b', 'c']);
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
    expect(describePath(workflow, 'automations[0].actions[0]')).toBe('Automation "Tag on entry", action 1');
    expect(describePath(workflow, 'questionnaire.questions[1].options')).toBe('Question "Shift"');
    expect(describePath(workflow, 'stages[1].name')).toBe('Stage "Screening"');
    expect(describePath(workflow, 'stages')).toBe('Stages');
  });

  it('names the reviewer an AssignReviewer action asks, or the team', () => {
    const members = [{ userId: 'u2', displayName: 'Robin Ashby' }];
    expect(describeAction({ type: 'AssignReviewer', assigneeUserId: 'u2' }, workflow, members)).toBe('Ask Robin Ashby to review');
    expect(describeAction({ type: 'AssignReviewer', assigneeUserId: 'gone' }, workflow, members)).toBe('Ask a teammate to review');
    expect(describeAction({ type: 'AssignReviewer' }, workflow, members)).toBe('Ask the team to review');
  });

  it('summarizes an automation in one sentence', () => {
    expect(summarize(workflow.automations[0]!, workflow)).toBe('When a candidate enters Screening, if 1 condition holds: add the tag "check".');
    expect(summarize(workflow.automations[1]!, workflow)).toBe('When someone runs it by hand: notify the team.');
  });
});
