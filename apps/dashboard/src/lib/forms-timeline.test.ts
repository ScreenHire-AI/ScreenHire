import type { Automation, HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { addFormLater, laterStages, momentOf, removeForm, timelineOf } from './forms-timeline.js';

const send = (id: string, formId: string, trigger: Automation['trigger'], extra: Partial<Automation> = {}): Automation => ({
  id,
  name: `Send ${formId}`,
  trigger,
  conditions: null,
  actions: [{ type: 'SendQuestionnaire', questionnaireId: formId }],
  enabled: true,
  allowAutoReject: false,
  ...extra,
});

const workflow = (): HiringWorkflow => ({
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone', name: 'Phone call', kind: 'Screening' },
    { id: 'trial', name: 'Trial shift', kind: 'Interview' },
    { id: 'offer', name: 'Offer', kind: 'Offer' },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Rejected', kind: 'Rejected' },
    { id: 'withdrawn', name: 'Withdrawn', kind: 'Withdrawn' },
  ],
  questionnaire: { questions: [{ id: 'q1', type: 'ShortText', label: 'Why us?', required: false }] },
  automations: [],
  followUpQuestionnaires: [],
});

describe('laterStages', () => {
  it('offers the open stages after Applied, never the ones that end the candidacy', () => {
    expect(laterStages(workflow()).map((stage) => stage.id)).toEqual(['phone', 'trial', 'offer']);
  });
});

describe('addFormLater', () => {
  it('makes a form named after the stage and the automation that emails it when they reach it', () => {
    const before = workflow();
    const { workflow: after, formId } = addFormLater(before, { stageId: 'trial' });

    expect(before.followUpQuestionnaires).toEqual([]);
    expect(formId).toBe('trial_shift_questions');
    expect(after.followUpQuestionnaires).toEqual([
      { id: 'trial_shift_questions', name: 'Trial shift questions', questions: [expect.objectContaining({ label: 'New question' })] },
    ]);
    expect(after.automations).toEqual([
      {
        id: 'send_trial_shift_questions',
        name: 'Email the form when they reach Trial shift',
        trigger: { type: 'StageEntered', stageId: 'trial' },
        conditions: null,
        actions: [{ type: 'SendQuestionnaire', questionnaireId: 'trial_shift_questions' }],
        enabled: true,
        allowAutoReject: false,
      },
    ]);
  });

  it('waits whole days after the stage starts, as hours', () => {
    const { workflow: after } = addFormLater(workflow(), { stageId: 'phone', days: 3 });

    expect(after.followUpQuestionnaires![0]!.name).toBe('Phone call questions after 3 days');
    expect(after.automations[0]).toMatchObject({
      name: 'Email the form 3 days after Phone call starts',
      trigger: { type: 'TimeElapsed', stageId: 'phone', afterHours: 72 },
    });
  });

  it('keeps ids, names and question ids unique when the same moment is asked twice', () => {
    const once = addFormLater(workflow(), { stageId: 'offer' });
    const twice = addFormLater(once.workflow, { stageId: 'offer' });
    const forms = twice.workflow.followUpQuestionnaires!;

    expect(forms.map((form) => form.name)).toEqual(['Offer questions', 'Offer questions 2']);
    expect(new Set(forms.map((form) => form.id)).size).toBe(2);
    expect(new Set(twice.workflow.automations.map((automation) => automation.id)).size).toBe(2);
    const questions = [...twice.workflow.questionnaire.questions, ...forms.flatMap((form) => form.questions)];
    expect(new Set(questions.map((question) => question.id)).size).toBe(questions.length);
  });

  it('numbers a second form on a stage whose name fills the name limit, and keeps it within 80 characters', () => {
    const long = workflow();
    long.stages[2] = { ...long.stages[2]!, name: 'T'.repeat(80) };

    let current = addFormLater(long, { stageId: 'trial' }).workflow;
    current = addFormLater(current, { stageId: 'trial' }).workflow;
    current = addFormLater(current, { stageId: 'trial' }).workflow;
    const names = current.followUpQuestionnaires!.map((form) => form.name);

    expect(new Set(names).size).toBe(3);
    expect(names.every((name) => name.length <= 80)).toBe(true);
    expect(names.slice(1).map((name) => name.slice(-2))).toEqual([' 2', ' 3']);
  });

  it('holds the wait to the validator limit of 8760 hours', () => {
    const { workflow: after } = addFormLater(workflow(), { stageId: 'phone', days: 9999 });

    expect(after.automations[0]!.trigger.afterHours).toBe(8760);
  });
});

describe('timelineOf', () => {
  it('starts with the apply form, which is always sent', () => {
    expect(timelineOf(workflow())).toEqual([{ id: 'apply', when: 'When they apply', name: 'Apply form', sent: true }]);
  });

  it('names each form by when it reaches the candidate, in the order they meet it', () => {
    let current = workflow();
    current = addFormLater(current, { stageId: 'offer' }).workflow;
    current = addFormLater(current, { stageId: 'phone', days: 2 }).workflow;
    current = addFormLater(current, { stageId: 'phone' }).workflow;

    expect(timelineOf(current).map((form) => [form.when, form.name])).toEqual([
      ['When they apply', 'Apply form'],
      ['When they reach Phone call', 'Phone call questions'],
      ['2 days after Phone call starts', 'Phone call questions after 2 days'],
      ['When they reach Offer', 'Offer questions'],
    ]);
  });

  it('says so when no enabled automation sends a form, and uses an hour count that is not whole days', () => {
    const base = workflow();
    base.followUpQuestionnaires = [
      { id: 'idle', name: 'Idle', questions: [{ id: 'a', type: 'ShortText', label: 'A', required: false }] },
      { id: 'paused', name: 'Paused', questions: [{ id: 'b', type: 'ShortText', label: 'B', required: false }] },
      { id: 'hours', name: 'Hours', questions: [{ id: 'c', type: 'ShortText', label: 'C', required: false }] },
    ];
    base.automations = [
      send('p', 'paused', { type: 'StageEntered', stageId: 'trial' }, { enabled: false }),
      send('h', 'hours', { type: 'TimeElapsed', stageId: 'trial', afterHours: 36 }),
    ];

    const [, ...forms] = timelineOf(base);
    expect(forms.map((form) => [form.id, form.when, form.sent])).toEqual([
      ['hours', '36 hours after Trial shift starts', true],
      ['idle', 'Not sent yet', false],
      ['paused', 'Not sent yet', false],
    ]);
  });
});

describe('momentOf', () => {
  it('words the triggers that are not a stage in a short phrase', () => {
    const current = workflow();
    expect(momentOf(current, { type: 'StageExited', stageId: 'phone' })).toBe('When they leave Phone call');
    expect(momentOf(current, { type: 'ReviewerDecision' })).toBe('When a reviewer records a decision');
    expect(momentOf(current, { type: 'ApplicationSubmitted' })).toBe('Right after they apply');
  });
});

describe('timelineOf, a form sent on submission', () => {
  it('comes right after the apply form, before the stage forms', () => {
    let current = addFormLater(workflow(), { stageId: 'phone' }).workflow;
    current.followUpQuestionnaires = [...current.followUpQuestionnaires!, { id: 'extra', name: 'Extra', questions: [{ id: 'x', type: 'ShortText', label: 'X', required: false }] }];
    current.automations = [...current.automations, send('e', 'extra', { type: 'ApplicationSubmitted' })];

    expect(timelineOf(current).map((form) => [form.when, form.name])).toEqual([
      ['When they apply', 'Apply form'],
      ['Right after they apply', 'Extra'],
      ['When they reach Phone call', 'Phone call questions'],
    ]);
  });
});

describe('removeForm', () => {
  it('removes the form and the automation that existed to send it, and says which', () => {
    const { workflow: made, formId } = addFormLater(workflow(), { stageId: 'trial' });
    const result = removeForm(made, formId);

    expect(result.workflow.followUpQuestionnaires).toEqual([]);
    expect(result.workflow.automations).toEqual([]);
    expect(result.removed).toEqual(['Email the form when they reach Trial shift']);
    expect(made.followUpQuestionnaires).toHaveLength(1);
  });

  it('keeps the other steps of an automation that did more than send the form', () => {
    const current = addFormLater(workflow(), { stageId: 'trial' }).workflow;
    current.automations[0]!.actions.push({ type: 'AddTag', tag: 'trial' });

    const result = removeForm(current, 'trial_shift_questions');

    expect(result.workflow.automations).toHaveLength(1);
    expect(result.workflow.automations[0]!.actions).toEqual([{ type: 'AddTag', tag: 'trial' }]);
    expect(result.removed).toEqual(['Email the form when they reach Trial shift']);
  });

  it('removes automations that run when this form is answered, and leaves other forms alone', () => {
    let current = addFormLater(workflow(), { stageId: 'trial' }).workflow;
    current = addFormLater(current, { stageId: 'offer' }).workflow;
    current.automations.push(
      { id: 'on_trial', name: 'After the trial form', trigger: { type: 'QuestionnaireCompleted', questionnaireId: 'trial_shift_questions' }, conditions: null, actions: [{ type: 'AddTag', tag: 'done' }], enabled: true, allowAutoReject: false },
      { id: 'on_offer', name: 'After the offer form', trigger: { type: 'QuestionnaireCompleted', questionnaireId: 'offer_questions' }, conditions: null, actions: [{ type: 'AddTag', tag: 'done' }], enabled: true, allowAutoReject: false },
    );

    const result = removeForm(current, 'trial_shift_questions');

    expect(result.removed).toEqual(['Email the form when they reach Trial shift', 'After the trial form']);
    expect(result.workflow.followUpQuestionnaires!.map((form) => form.id)).toEqual(['offer_questions']);
    expect(result.workflow.automations.map((automation) => automation.id)).toEqual(['send_offer_questions', 'on_offer']);
  });

  it('removes an unnamed answered-trigger automation only when the last form goes', () => {
    let current = addFormLater(workflow(), { stageId: 'trial' }).workflow;
    current = addFormLater(current, { stageId: 'offer' }).workflow;
    current.automations.push(
      { id: 'any', name: 'After any form', trigger: { type: 'QuestionnaireCompleted' }, conditions: null, actions: [{ type: 'AddTag', tag: 'done' }], enabled: true, allowAutoReject: false },
    );

    const first = removeForm(current, 'trial_shift_questions');
    expect(first.workflow.automations.map((automation) => automation.id)).toContain('any');

    const last = removeForm(first.workflow, 'offer_questions');
    expect(last.workflow.automations).toEqual([]);
    expect(last.removed).toContain('After any form');
  });

  it("drops a rule that reads the removed form's answers and keeps the automation", () => {
    const current = addFormLater(workflow(), { stageId: 'trial' }).workflow;
    const answer = `answers.${current.followUpQuestionnaires![0]!.questions[0]!.id}`;
    current.automations.push({
      id: 'tag',
      name: 'Tag by answer',
      trigger: { type: 'StageEntered', stageId: 'offer' },
      conditions: { match: 'All', rules: [{ fact: answer, operator: 'EqualTo', value: 'yes' }, { fact: 'answers.q1', operator: 'EqualTo', value: 'x' }] },
      actions: [{ type: 'AddTag', tag: 'a' }],
      enabled: true,
      allowAutoReject: false,
    });

    const result = removeForm(current, 'trial_shift_questions');
    const kept = result.workflow.automations.find((automation) => automation.id === 'tag')!;

    expect(kept.conditions!.rules).toEqual([{ fact: 'answers.q1', operator: 'EqualTo', value: 'x' }]);
    expect(result.removed).toContain('Tag by answer');
  });

  it('changes nothing for the apply form or an unknown id', () => {
    const current = addFormLater(workflow(), { stageId: 'trial' }).workflow;

    expect(removeForm(current, 'apply')).toEqual({ workflow: current, removed: [] });
    expect(removeForm(current, 'nope')).toEqual({ workflow: current, removed: [] });
  });
});
