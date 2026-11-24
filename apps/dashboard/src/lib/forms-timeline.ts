// The Forms tab's forms (design system section 11, Form cards; workflow-engine.md section 7, Forms): pure functions over a
// workflow. The application form is the template's own; every other form is a copy of a library form, and when it is
// sent is the automation behind it: choosing "When they enter Screening" on the form writes that automation, and choosing
// "Only when sent by hand" removes it. No Svelte runes here, so the words, the moments and the cascade are tested without
// a page.
import type {
  Automation,
  AutomationTrigger,
  FollowUpQuestionnaire,
  FormContent,
  FormKind,
  HiringWorkflow,
  WorkflowStage,
} from '@screenhire/api-client';
import { allQuestions, followUpsOf, idFrom, isTerminal, triggerLabels } from '#lib/workflow.js';

/** One card of the strip. `id` is `apply` or a follow-up form's id. */
export type TimelineForm = {
  id: string;
  /** The form's name; the template's own is "Application form". */
  name: string;
  /** When it reaches the candidate: "When they apply", "Sent when they enter Screening", "Only when sent by hand". */
  when: string;
  /** The same in the few words a card's chip holds: "When they apply", "Entering Screening", "By hand only". */
  whenShort: string;
  /** Which moment it is, for the chip's icon; `other` when an automation that does more sends it. */
  moment: SendMoment['kind'] | 'other';
  /** How many questions it asks. */
  questions: number;
  /** Whether it is a copy of a library form (every form but the application form, and a job's own older ones). */
  library: boolean;
  /** Whether it is an equality monitoring form. */
  equality: boolean;
};

export const APPLY_FORM_ID = 'apply';

/** The most a form may wait after a stage starts: the validator's 8760 hours. */
export const MAX_DAYS_LATER = 365;

/** When a form is sent, as the form's own choice says it. */
export type SendMoment =
  | { kind: 'apply' }
  | { kind: 'stage'; stageId: string }
  | { kind: 'later'; stageId: string; days: number }
  | { kind: 'hand' };

/**
 * How a form is sent now. `editable` is false when an automation that does more than send it (a condition, other steps, a
 * trigger the form's choice does not offer) sends it: the form says so and links to it, and never rewrites it.
 */
export type FormSending = { moment: SendMoment; words: string; editable: boolean; automationId?: string };

const NAME_LENGTH = 80;
const clip = (text: string, length = NAME_LENGTH): string => (text.length > length ? text.slice(0, length).trimEnd() : text);
const lowerFirst = (text: string): string => text.replace(/^./, (first) => first.toLowerCase());
/** "1 question", "3 questions": a count and its word. */
export const plural = (count: number, word: string): string => `${count} ${word}${count === 1 ? '' : 's'}`;
const isOn = (automation: Automation) => automation.enabled !== false;

/** The stages a form can be sent at: open stages after Applied (the application form already covers that moment). */
export const laterStages = (workflow: HiringWorkflow): WorkflowStage[] =>
  workflow.stages.filter((stage) => stage.kind !== 'Applied' && !isTerminal(stage.kind));

/** The automations that email this form, whether they are enabled or not. */
const sendersOf = (workflow: HiringWorkflow, formId: string): Automation[] =>
  workflow.automations.filter((automation) => automation.actions.some((action) => action.type === 'SendQuestionnaire' && action.questionnaireId === formId));

/** An automation that does nothing but send this form, on a moment the form's own choice offers. */
function sendsOnly(automation: Automation, formId: string): boolean {
  const { trigger } = automation;
  return (
    automation.actions.length === 1 &&
    automation.actions[0].type === 'SendQuestionnaire' &&
    automation.actions[0].questionnaireId === formId &&
    (automation.conditions?.rules.length ?? 0) === 0 &&
    (trigger.type === 'ApplicationSubmitted' || trigger.type === 'StageEntered' || (trigger.type === 'TimeElapsed' && (trigger.afterHours ?? 0) % 24 === 0))
  );
}

function stageName(workflow: HiringWorkflow, stageId: string | null | undefined): string {
  return workflow.stages.find((stage) => stage.id === stageId)?.name || 'a stage';
}

/** How long after a stage starts, as a person says it: whole days when it is, else hours. */
function waitWords(hours: number): string {
  return hours % 24 === 0 ? plural(hours / 24, 'day') : plural(hours, 'hour');
}

/** The moment a trigger fires, worded from the candidate's side. */
export function momentOf(workflow: HiringWorkflow, trigger: AutomationTrigger): string {
  switch (trigger.type) {
    case 'ApplicationSubmitted':
      return 'When they apply';
    case 'StageEntered':
      return `When they enter ${stageName(workflow, trigger.stageId)}`;
    case 'StageExited':
      return `When they leave ${stageName(workflow, trigger.stageId)}`;
    case 'TimeElapsed':
      return trigger.afterHours
        ? `${waitWords(trigger.afterHours)} after they enter ${stageName(workflow, trigger.stageId)}`
        : `When they enter ${stageName(workflow, trigger.stageId)}`;
    default:
      return `When ${lowerFirst(triggerLabels[trigger.type] ?? 'it runs')}`;
  }
}

/** A moment as the form's card says it. */
export function sendWords(workflow: HiringWorkflow, moment: SendMoment): string {
  switch (moment.kind) {
    case 'apply':
      return 'Sent when they apply';
    case 'stage':
      return `Sent when they enter ${stageName(workflow, moment.stageId)}`;
    case 'later':
      return `Sent ${plural(moment.days, 'day')} after they enter ${stageName(workflow, moment.stageId)}`;
    default:
      return 'Only when sent by hand';
  }
}

function momentFrom(trigger: AutomationTrigger): SendMoment {
  if (trigger.type === 'ApplicationSubmitted') return { kind: 'apply' };
  if (trigger.type === 'TimeElapsed' && trigger.afterHours) return { kind: 'later', stageId: trigger.stageId ?? '', days: trigger.afterHours / 24 };
  return { kind: 'stage', stageId: trigger.stageId ?? '' };
}

/** How the form `formId` is sent: by the first automation that is on and sends it, or only by hand when none does. */
export function sendingOf(workflow: HiringWorkflow, formId: string): FormSending {
  const senders = sendersOf(workflow, formId).filter(isOn);
  const first = senders[0];
  if (!first) return { moment: { kind: 'hand' }, words: sendWords(workflow, { kind: 'hand' }), editable: true };
  if (senders.length === 1 && sendsOnly(first, formId)) {
    const moment = momentFrom(first.trigger);
    return { moment, words: sendWords(workflow, moment), editable: true, automationId: first.id };
  }
  return { moment: momentFrom(first.trigger), words: `Sent ${lowerFirst(momentOf(workflow, first.trigger))}`, editable: false, automationId: first.id };
}

/**
 * Sets when the form `formId` is sent: the automations that only send it go, and one for `moment` takes their place,
 * unless the form is now sent only by hand. The automation is named after the moment, not the form, so renaming the form
 * never leaves it stale. Returns a new workflow; the argument is not changed.
 */
export function setSendMoment(workflow: HiringWorkflow, formId: string, moment: SendMoment): HiringWorkflow {
  const kept = workflow.automations.filter((automation) => !sendsOnly(automation, formId));
  if (moment.kind === 'hand') return { ...workflow, automations: kept };

  const days = moment.kind === 'later' ? Math.min(Math.max(1, Math.floor(moment.days)), MAX_DAYS_LATER) : 0;
  const trigger: AutomationTrigger =
    moment.kind === 'apply'
      ? { type: 'ApplicationSubmitted' }
      : moment.kind === 'later'
        ? { type: 'TimeElapsed', stageId: moment.stageId, afterHours: days * 24 }
        : { type: 'StageEntered', stageId: moment.stageId };
  const stage = moment.kind === 'apply' ? '' : stageName(workflow, moment.stageId);
  const name =
    moment.kind === 'apply'
      ? 'Email the form when they apply'
      : moment.kind === 'later'
        ? `Email the form ${plural(days, 'day')} after they enter ${stage}`
        : `Email the form when they enter ${stage}`;
  const automation: Automation = {
    id: idFrom(`send ${formId}`, kept.map((existing) => existing.id), 'automation'),
    name: clip(name, 120),
    trigger,
    conditions: null,
    actions: [{ type: 'SendQuestionnaire', questionnaireId: formId }],
    enabled: true,
    allowAutoReject: false,
  };
  return { ...workflow, automations: [...kept, automation] };
}

// Where a form sits in the strip: the application form, then the forms sent on applying, then by the stage they are sent
// at and the wait inside it, then the forms sent some other way, and last the forms sent only by hand, in the workflow's
// order within each.
function position(workflow: HiringWorkflow, sending: FormSending, order: number): [number, number, number] {
  if (sending.moment.kind === 'hand' && sending.editable) return [Number.MAX_SAFE_INTEGER, 0, order];
  if (sending.moment.kind === 'apply') return [-1, 0, order];
  const stageId = sending.moment.kind === 'hand' ? '' : sending.moment.stageId;
  const stage = workflow.stages.findIndex((candidate) => candidate.id === stageId);
  if (stage < 0) return [Number.MAX_SAFE_INTEGER - 1, 0, order];
  return [stage, sending.moment.kind === 'later' ? sending.moment.days : 0, order];
}

/** When a form is sent, as its card's chip says it. */
export function shortWords(workflow: HiringWorkflow, sending: FormSending): string {
  if (!sending.editable) return sending.words.replace(/^Sent /, '').replace(/^./, (first) => first.toUpperCase());
  const { moment } = sending;
  switch (moment.kind) {
    case 'apply':
      return 'When they apply';
    case 'stage':
      return `Entering ${stageName(workflow, moment.stageId)}`;
    case 'later':
      return `${plural(moment.days, 'day')} after ${stageName(workflow, moment.stageId)}`;
    default:
      return 'By hand only';
  }
}

/** The forms in the order the candidate meets them: the application form, then each other form by when it is sent. */
export function timelineOf(workflow: HiringWorkflow): TimelineForm[] {
  const later = followUpsOf(workflow).map((form, order) => {
    const sending = sendingOf(workflow, form.id);
    return {
      at: position(workflow, sending, order),
      card: {
        id: form.id,
        name: form.name || form.id,
        when: sending.words,
        whenShort: shortWords(workflow, sending),
        moment: sending.editable ? sending.moment.kind : 'other',
        questions: form.questions.length,
        library: Boolean(form.formTemplateId),
        equality: form.kind === 'EqualityMonitoring',
      } satisfies TimelineForm,
    };
  });
  later.sort((a, b) => a.at[0] - b.at[0] || a.at[1] - b.at[1] || a.at[2] - b.at[2]);
  return [
    { id: APPLY_FORM_ID, name: 'Application form', when: 'When they apply', whenShort: 'When they apply', moment: 'apply', questions: workflow.questionnaire.questions.length, library: false, equality: false },
    ...later.map((entry) => entry.card),
  ];
}

/** "4 questions". */
export const questionCount = (count: number): string => plural(count, 'question');

/** A published library form version as a template adds it. */
export type LibraryVersion = { formTemplateId: string; version: number; kind?: FormKind; content: FormContent };

// Ids the apply form gives meaning to, which no other form may use (DefinitionValidator.ApplyFormQuestionIds).
const applyFormIds = ['resume', 'linkedin_export'];

/**
 * The questions of `content` whose ids the workflow already uses, on another form or as the application form's own
 * (`exceptFormId`'s own questions excepted, for an update of that form). A question id is unique across a workflow's
 * forms, so such a form cannot be added until one of them changes.
 */
export function questionClashes(workflow: HiringWorkflow, content: FormContent, exceptFormId?: string): string[] {
  const own = new Set(followUpsOf(workflow).find((form) => form.id === exceptFormId)?.questions.map((question) => question.id) ?? []);
  const taken = new Set([...applyFormIds, ...allQuestions(workflow).map((question) => question.id).filter((id) => !own.has(id))]);
  return content.questions.filter((question) => taken.has(question.id)).map((question) => question.label || question.id);
}

// A deep copy through JSON: the version may come from a page's state, whose proxies structuredClone refuses.
const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

function copyOf(version: LibraryVersion, id: string): FollowUpQuestionnaire {
  return {
    id,
    name: version.content.name,
    questions: clone(version.content.questions),
    sections: version.content.sections ? clone(version.content.sections) : null,
    formTemplateId: version.formTemplateId,
    formVersion: version.version,
    kind: version.kind === 'EqualityMonitoring' ? 'EqualityMonitoring' : null,
  };
}

/**
 * Adds a copy of a library form version, sent only by hand until its moment is chosen. Its id comes from its name and is
 * unique among the workflow's forms. Returns a new workflow; the argument is not changed.
 */
export function addLibraryForm(workflow: HiringWorkflow, version: LibraryVersion): { workflow: HiringWorkflow; formId: string } {
  const followUps = followUpsOf(workflow);
  const formId = idFrom(version.content.name, followUps.map((form) => form.id), 'form');
  return { workflow: { ...workflow, followUpQuestionnaires: [...followUps, copyOf(version, formId)] }, formId };
}

/** The form `formId` holding another version of its library form, under the same id, so what sends it still does. */
export function useLibraryVersion(workflow: HiringWorkflow, formId: string, version: LibraryVersion): HiringWorkflow {
  return { ...workflow, followUpQuestionnaires: followUpsOf(workflow).map((form) => (form.id === formId ? copyOf(version, formId) : form)) };
}

/**
 * Replaces the form `formId` with a copy of another library form, under the same id, so when it is sent and what runs once
 * it is answered stay. An automation whose conditions read an answer the new form does not ask goes with the old questions,
 * and its name is returned for the confirmation ("This also removes ..."). Only the same id with the same kind, words and
 * choices is the same question: two library forms can both have a question with a made-up id such as `question`, and a rule
 * left behind would silently read the other one. Dropping only that rule would instead run the automation for more people.
 * Nothing may act on an equality monitoring form (DefinitionValidator.ValidateEqualityUse), so one in its place takes along
 * every automation that reads the form or runs once it is answered. The application form cannot be replaced, and an unknown
 * id changes nothing.
 */
export function replaceForm(workflow: HiringWorkflow, formId: string, version: LibraryVersion): { workflow: HiringWorkflow; removed: string[] } {
  const form = followUpsOf(workflow).find((candidate) => candidate.id === formId);
  if (!form) return { workflow, removed: [] };

  type AskedQuestion = FormContent['questions'][number];
  const same = (a: AskedQuestion, b: AskedQuestion) =>
    a.id === b.id &&
    a.type === b.type &&
    a.label.trim() === b.label.trim() &&
    (a.options ?? []).every((option) => b.options?.some((next) => next.id === option.id && next.label.trim() === option.label.trim()));
  const countedOnly = version.kind === 'EqualityMonitoring';
  const gone = new Set(
    form.questions
      .filter((question) => countedOnly || !version.content.questions.some((next) => same(question, next)))
      .map((question) => `answers.${question.id}`),
  );
  const goes = ({ conditions, trigger }: Automation) =>
    (conditions?.rules.some((rule) => gone.has(rule.fact)) ?? false) ||
    (countedOnly && trigger.type === 'QuestionnaireCompleted' && trigger.questionnaireId === formId);
  const swapped = useLibraryVersion(workflow, formId, version);
  return {
    workflow: { ...swapped, automations: swapped.automations.filter((automation) => !goes(automation)) },
    removed: workflow.automations.filter(goes).map((automation) => automation.name),
  };
}

/**
 * Removes a follow-up form and what would be left pointing at it, so the workflow stays valid. Returns the names of the
 * automations that were removed or lost a step, for the confirmation ("This also removes ..."):
 * - an automation whose only step sent this form is removed, and one that does more keeps its other steps;
 * - an automation that runs when this form is answered is removed;
 * - an automation that runs when any form is answered is removed once the last form goes;
 * - a rule that reads one of the form's answers is dropped from the conditions that held it.
 * The application form cannot be removed, and an unknown id changes nothing.
 */
export function removeForm(workflow: HiringWorkflow, id: string): { workflow: HiringWorkflow; removed: string[] } {
  const followUps = followUpsOf(workflow);
  const form = followUps.find((candidate) => candidate.id === id);
  if (!form) return { workflow, removed: [] };

  const rest = followUps.filter((candidate) => candidate.id !== id);
  const answers = new Set(form.questions.map((question) => `answers.${question.id}`));
  const removed: string[] = [];
  const automations: Automation[] = [];

  for (const automation of workflow.automations) {
    const trigger = automation.trigger;
    const answeredIt = trigger.type === 'QuestionnaireCompleted' && (trigger.questionnaireId === id || (!trigger.questionnaireId && rest.length === 0));
    if (answeredIt) {
      removed.push(automation.name);
      continue;
    }

    const actions = automation.actions.filter((action) => !(action.type === 'SendQuestionnaire' && action.questionnaireId === id));
    const sentIt = actions.length !== automation.actions.length;
    if (sentIt && actions.length === 0) {
      removed.push(automation.name);
      continue;
    }

    const rules = automation.conditions?.rules.filter((rule) => !answers.has(rule.fact));
    const readIt = rules !== undefined && rules.length !== automation.conditions!.rules.length;
    if (!sentIt && !readIt) {
      automations.push(automation);
      continue;
    }

    removed.push(automation.name);
    automations.push({
      ...automation,
      actions,
      conditions: rules && rules.length > 0 ? { ...automation.conditions!, rules } : readIt ? null : automation.conditions,
    });
  }

  return { workflow: { ...workflow, followUpQuestionnaires: rest, automations }, removed };
}
