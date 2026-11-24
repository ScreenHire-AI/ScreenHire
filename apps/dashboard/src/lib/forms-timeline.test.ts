import type { Automation, HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import {
  addLibraryForm,
  laterStages,
  questionClashes,
  removeForm,
  replaceForm,
  sendingOf,
  setSendMoment,
  timelineOf,
  useLibraryVersion,
  type LibraryVersion,
  type SendMoment,
} from './forms-timeline.js';

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

const library = (name: string, questionId = `q_${name.toLowerCase().replace(/\W+/g, '_')}`, version = 1): LibraryVersion => ({
  formTemplateId: `form-${name}`,
  version,
  content: { name, questions: [{ id: questionId, type: 'ShortText', label: `${name}?`, required: false }] },
});

/** A workflow with the library form `name`, sent at `moment`. */
function withForm(current: HiringWorkflow, name: string, moment: SendMoment = { kind: 'hand' }): { workflow: HiringWorkflow; formId: string } {
  const added = addLibraryForm(current, library(name));
  return { workflow: setSendMoment(added.workflow, added.formId, moment), formId: added.formId };
}

describe('laterStages', () => {
  it('offers the open stages after Applied, never the ones that end the candidacy', () => {
    expect(laterStages(workflow()).map((stage) => stage.id)).toEqual(['phone', 'trial', 'offer']);
  });
});

describe('a library form in a workflow', () => {
  it('is a copy of the version that names it, sent only by hand until its moment is chosen', () => {
    const before = workflow();
    const { workflow: after, formId } = addLibraryForm(before, library('Screening questionnaire'));

    expect(before.followUpQuestionnaires).toEqual([]);
    expect(formId).toBe('screening_questionnaire');
    expect(after.followUpQuestionnaires).toEqual([
      {
        id: 'screening_questionnaire',
        name: 'Screening questionnaire',
        questions: [{ id: 'q_screening_questionnaire', type: 'ShortText', label: 'Screening questionnaire?', required: false }],
        sections: null,
        formTemplateId: 'form-Screening questionnaire',
        formVersion: 1,
        kind: null,
      },
    ]);
    expect(after.automations).toEqual([]);
    expect(sendingOf(after, formId)).toEqual({ moment: { kind: 'hand' }, words: 'Only when sent by hand', editable: true });
  });

  it('writes the automation behind the moment chosen on it, and replaces it when the moment changes', () => {
    const { workflow: atPhone, formId } = withForm(workflow(), 'Screening questionnaire', { kind: 'stage', stageId: 'phone' });
    expect(atPhone.automations).toEqual([
      {
        id: 'send_screening_questionnaire',
        name: 'Email the form when they enter Phone call',
        trigger: { type: 'StageEntered', stageId: 'phone' },
        conditions: null,
        actions: [{ type: 'SendQuestionnaire', questionnaireId: formId }],
        enabled: true,
        allowAutoReject: false,
      },
    ]);
    expect(sendingOf(atPhone, formId).words).toBe('Sent when they enter Phone call');

    const later = setSendMoment(atPhone, formId, { kind: 'later', stageId: 'trial', days: 3 });
    expect(later.automations).toHaveLength(1);
    expect(later.automations[0]).toMatchObject({ name: 'Email the form 3 days after they enter Trial shift', trigger: { type: 'TimeElapsed', stageId: 'trial', afterHours: 72 } });
    expect(sendingOf(later, formId).moment).toEqual({ kind: 'later', stageId: 'trial', days: 3 });

    const applying = setSendMoment(later, formId, { kind: 'apply' });
    expect(applying.automations[0]!.trigger).toEqual({ type: 'ApplicationSubmitted' });
    expect(sendingOf(applying, formId).words).toBe('Sent when they apply');

    expect(setSendMoment(applying, formId, { kind: 'hand' }).automations).toEqual([]);
  });

  it('never rewrites an automation that does more than send it, and says which one sends it', () => {
    const { workflow: sent, formId } = withForm(workflow(), 'References', { kind: 'stage', stageId: 'offer' });
    sent.automations[0]!.actions.push({ type: 'AddTag', tag: 'asked' });

    const sending = sendingOf(sent, formId);
    expect(sending).toMatchObject({ editable: false, automationId: 'send_references', words: 'Sent when they enter Offer' });
    expect(setSendMoment(sent, formId, { kind: 'hand' }).automations).toEqual(sent.automations);
  });

  it('holds the wait to the validator limit of 8760 hours', () => {
    const { workflow: far } = withForm(workflow(), 'Far', { kind: 'later', stageId: 'offer', days: 5000 });
    expect(far.automations[0]!.trigger.afterHours).toBe(8760);
  });

  it('takes another version under the same id, so what sends it still does', () => {
    const { workflow: sent, formId } = withForm(workflow(), 'References', { kind: 'stage', stageId: 'offer' });
    const updated = useLibraryVersion(sent, formId, { ...library('References', 'q_new', 2) });

    expect(updated.followUpQuestionnaires![0]).toMatchObject({ id: formId, formVersion: 2, questions: [{ id: 'q_new' }] });
    expect(updated.automations).toEqual(sent.automations);
  });

  it('names the questions another form here already asks, and its own questions only for an update of it', () => {
    const { workflow: current, formId } = withForm(workflow(), 'References');
    const clashing = { name: 'Clash', questions: [{ id: 'q1', type: 'ShortText' as const, label: 'Why us again?', required: false }, { id: 'resume', type: 'File' as const, label: 'CV', required: false }] };

    expect(questionClashes(current, clashing)).toEqual(['Why us again?', 'CV']);
    expect(questionClashes(current, library('References').content)).toEqual(['References?']);
    expect(questionClashes(current, library('References').content, formId)).toEqual([]);
  });
});

describe('timelineOf', () => {
  it('starts with the application form and its question count', () => {
    expect(timelineOf(workflow())).toEqual([
      { id: 'apply', name: 'Application form', when: 'When they apply', whenShort: 'When they apply', moment: 'apply', questions: 1, library: false, equality: false },
    ]);
  });

  it('puts the forms in the order the candidate meets them, with those sent only by hand last', () => {
    let current = withForm(workflow(), 'Availability').workflow;
    current = withForm(current, 'References', { kind: 'later', stageId: 'offer', days: 3 }).workflow;
    current = withForm(current, 'Trial', { kind: 'stage', stageId: 'trial' }).workflow;
    current = withForm(current, 'Equality', { kind: 'apply' }).workflow;

    expect(timelineOf(current).map((form) => [form.name, form.when, form.questions])).toEqual([
      ['Application form', 'When they apply', 1],
      ['Equality', 'Sent when they apply', 1],
      ['Trial', 'Sent when they enter Trial shift', 1],
      ['References', 'Sent 3 days after they enter Offer', 1],
      ['Availability', 'Only when sent by hand', 1],
    ]);
    // The card's chip says the same in a few words, with the moment for its icon.
    expect(timelineOf(current).map((form) => [form.whenShort, form.moment])).toEqual([
      ['When they apply', 'apply'],
      ['When they apply', 'apply'],
      ['Entering Trial shift', 'stage'],
      ['3 days after Offer', 'later'],
      ['By hand only', 'hand'],
    ]);
  });

  it('marks an equality monitoring form and a form that copies no library form', () => {
    const current: HiringWorkflow = {
      ...workflow(),
      followUpQuestionnaires: [
        { id: 'own', name: 'Our own', questions: [{ id: 'a', type: 'ShortText', label: 'A?', required: false }] },
        { id: 'eq', name: 'Equality', questions: [{ id: 'b', type: 'YesNo', label: 'B?', required: false }], formTemplateId: 'f', formVersion: 1, kind: 'EqualityMonitoring' },
      ],
    };
    expect(timelineOf(current).slice(1).map((form) => [form.id, form.library, form.equality])).toEqual([
      ['own', false, false],
      ['eq', true, true],
    ]);
  });

  it('counts only an automation that is on, and words a wait in hours that is not whole days', () => {
    const { workflow: current, formId } = withForm(workflow(), 'Check-in');
    const off: Automation = {
      id: 'off',
      name: 'Off',
      trigger: { type: 'StageEntered', stageId: 'phone' },
      conditions: null,
      actions: [{ type: 'SendQuestionnaire', questionnaireId: formId }],
      enabled: false,
      allowAutoReject: false,
    };
    expect(sendingOf({ ...current, automations: [off] }, formId).words).toBe('Only when sent by hand');
    expect(sendingOf({ ...current, automations: [{ ...off, enabled: true, trigger: { type: 'TimeElapsed', stageId: 'phone', afterHours: 5 } }] }, formId))
      .toMatchObject({ editable: false, words: 'Sent 5 hours after they enter Phone call' });
  });
});

describe('replaceForm', () => {
  const tagBy = (fact: string, trigger: Automation['trigger'] = { type: 'StageEntered', stageId: 'offer' }): Automation => ({
    id: `tag-${fact}`,
    name: `Tag by ${fact}`,
    trigger,
    conditions: { match: 'All', rules: [{ fact, operator: 'EqualTo', value: 'yes' }] },
    actions: [{ type: 'AddTag', tag: 'a' }],
    enabled: true,
    allowAutoReject: false,
  });

  it('swaps the questions under the same id, and keeps when it is sent and what runs once it is answered', () => {
    const { workflow: current, formId } = withForm(workflow(), 'Screening questionnaire', { kind: 'later', stageId: 'trial', days: 3 });
    current.automations.push({
      id: 'answered',
      name: 'After it is answered',
      trigger: { type: 'QuestionnaireCompleted', questionnaireId: formId },
      conditions: null,
      actions: [{ type: 'AddTag', tag: 'answered' }],
      enabled: true,
      allowAutoReject: false,
    });

    const result = replaceForm(current, formId, library('References', 'q_ref', 4));

    expect(result.removed).toEqual([]);
    expect(result.workflow.followUpQuestionnaires).toMatchObject([{ id: formId, name: 'References', formTemplateId: 'form-References', formVersion: 4, questions: [{ id: 'q_ref' }] }]);
    expect(sendingOf(result.workflow, formId).moment).toEqual({ kind: 'later', stageId: 'trial', days: 3 });
    expect(result.workflow.automations).toEqual(current.automations);
    expect(timelineOf(result.workflow)[1]).toMatchObject({ id: formId, name: 'References', whenShort: '3 days after Trial shift' });
  });

  it('takes along an automation that reads an answer the new form does not ask, and says which', () => {
    const { workflow: current, formId } = withForm(workflow(), 'Trial', { kind: 'stage', stageId: 'trial' });
    current.automations.push(tagBy('answers.q_trial'), tagBy('answers.q1'));

    const result = replaceForm(current, formId, library('References'));

    expect(result.removed).toEqual(['Tag by answers.q_trial']);
    expect(result.workflow.automations.map((automation) => automation.name)).toEqual(['Email the form when they enter Trial shift', 'Tag by answers.q1']);
  });

  it('never points a rule at a different question that only shares its id', () => {
    const { workflow: current, formId } = withForm(workflow(), 'Trial', { kind: 'stage', stageId: 'trial' });
    current.automations.push(tagBy('answers.q_trial'));

    // Another library form whose own question happens to have the id.
    expect(replaceForm(current, formId, library('References', 'q_trial')).removed).toEqual(['Tag by answers.q_trial']);

    // A copy of the form in the library asks the same question, so the rule still reads what it did.
    const copy: LibraryVersion = { ...library('Trial'), formTemplateId: 'form-copy', content: { ...library('Trial').content, name: 'Trial (copy)' } };
    const kept = replaceForm(current, formId, copy);
    expect(kept.removed).toEqual([]);
    expect(kept.workflow.automations).toEqual(current.automations);
  });

  it('reads a choice question as another one when its choices say something else', () => {
    const { workflow: current, formId } = withForm(workflow(), 'Trial', { kind: 'stage', stageId: 'trial' });
    const asking = (label: string): LibraryVersion => ({
      ...library('Shifts'),
      content: { name: 'Shifts', questions: [{ id: 'q_shift', type: 'SingleChoice', label: 'Which shift?', required: false, options: [{ id: 'option_1', label }] }] },
    });
    const days = useLibraryVersion(current, formId, asking('Days'));
    days.automations.push(tagBy('answers.q_shift'));

    expect(replaceForm(days, formId, asking('Days')).removed).toEqual([]);
    expect(replaceForm(days, formId, asking('Nights')).removed).toEqual(['Tag by answers.q_shift']);
  });

  it('lets nothing act on an equality monitoring form that takes its place, and keeps when it is sent', () => {
    const { workflow: current, formId } = withForm(workflow(), 'Trial', { kind: 'stage', stageId: 'trial' });
    const answered: Automation = { ...tagBy('none', { type: 'QuestionnaireCompleted', questionnaireId: formId }), name: 'After it is answered', conditions: null };
    current.automations.push(tagBy('answers.q_trial'), answered);

    // Even the same question: its answers are only counted.
    const result = replaceForm(current, formId, { ...library('Trial'), formTemplateId: 'form-equality', kind: 'EqualityMonitoring' });

    expect(result.removed).toEqual(['Tag by answers.q_trial', 'After it is answered']);
    expect(result.workflow.automations.map((automation) => automation.name)).toEqual(['Email the form when they enter Trial shift']);
    expect(sendingOf(result.workflow, formId).moment).toEqual({ kind: 'stage', stageId: 'trial' });
  });

  it('changes nothing for the application form or an unknown id', () => {
    const current = withForm(workflow(), 'Trial', { kind: 'stage', stageId: 'trial' }).workflow;

    expect(replaceForm(current, 'apply', library('References'))).toEqual({ workflow: current, removed: [] });
    expect(replaceForm(current, 'nope', library('References'))).toEqual({ workflow: current, removed: [] });
  });
});

describe('removeForm', () => {
  it('removes the form and the automation that existed to send it, and says which', () => {
    const { workflow: made, formId } = withForm(workflow(), 'Trial', { kind: 'stage', stageId: 'trial' });
    const result = removeForm(made, formId);

    expect(result.workflow.followUpQuestionnaires).toEqual([]);
    expect(result.workflow.automations).toEqual([]);
    expect(result.removed).toEqual(['Email the form when they enter Trial shift']);
    expect(made.followUpQuestionnaires).toHaveLength(1);
  });

  it('keeps the other steps of an automation that did more than send the form', () => {
    const { workflow: current, formId } = withForm(workflow(), 'Trial', { kind: 'stage', stageId: 'trial' });
    current.automations[0]!.actions.push({ type: 'AddTag', tag: 'trial' });

    const result = removeForm(current, formId);

    expect(result.workflow.automations).toHaveLength(1);
    expect(result.workflow.automations[0]!.actions).toEqual([{ type: 'AddTag', tag: 'trial' }]);
    expect(result.removed).toEqual(['Email the form when they enter Trial shift']);
  });

  it('removes automations that run when this form is answered, and leaves other forms alone', () => {
    let current = withForm(workflow(), 'Trial', { kind: 'stage', stageId: 'trial' }).workflow;
    current = withForm(current, 'Offer', { kind: 'stage', stageId: 'offer' }).workflow;
    current.automations.push(
      { id: 'on_trial', name: 'After the trial form', trigger: { type: 'QuestionnaireCompleted', questionnaireId: 'trial' }, conditions: null, actions: [{ type: 'AddTag', tag: 'done' }], enabled: true, allowAutoReject: false },
      { id: 'on_offer', name: 'After the offer form', trigger: { type: 'QuestionnaireCompleted', questionnaireId: 'offer' }, conditions: null, actions: [{ type: 'AddTag', tag: 'done' }], enabled: true, allowAutoReject: false },
    );

    const result = removeForm(current, 'trial');

    expect(result.removed).toEqual(['Email the form when they enter Trial shift', 'After the trial form']);
    expect(result.workflow.followUpQuestionnaires!.map((form) => form.id)).toEqual(['offer']);
    expect(result.workflow.automations.map((automation) => automation.id)).toEqual(['send_offer', 'on_offer']);
  });

  it('removes an unnamed answered-trigger automation only when the last form goes', () => {
    let current = withForm(workflow(), 'Trial', { kind: 'stage', stageId: 'trial' }).workflow;
    current = withForm(current, 'Offer', { kind: 'stage', stageId: 'offer' }).workflow;
    current.automations.push(
      { id: 'any', name: 'After any form', trigger: { type: 'QuestionnaireCompleted' }, conditions: null, actions: [{ type: 'AddTag', tag: 'done' }], enabled: true, allowAutoReject: false },
    );

    const first = removeForm(current, 'trial');
    expect(first.workflow.automations.map((automation) => automation.id)).toContain('any');

    const last = removeForm(first.workflow, 'offer');
    expect(last.workflow.automations).toEqual([]);
    expect(last.removed).toContain('After any form');
  });

  it("drops a rule that reads the removed form's answers and keeps the automation", () => {
    const { workflow: current, formId } = withForm(workflow(), 'Trial', { kind: 'stage', stageId: 'trial' });
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

    const result = removeForm(current, formId);
    const kept = result.workflow.automations.find((automation) => automation.id === 'tag')!;

    expect(kept.conditions!.rules).toEqual([{ fact: 'answers.q1', operator: 'EqualTo', value: 'x' }]);
    expect(result.removed).toContain('Tag by answer');
  });

  it('changes nothing for the application form or an unknown id', () => {
    const current = withForm(workflow(), 'Trial', { kind: 'stage', stageId: 'trial' }).workflow;

    expect(removeForm(current, 'apply')).toEqual({ workflow: current, removed: [] });
    expect(removeForm(current, 'nope')).toEqual({ workflow: current, removed: [] });
  });
});
