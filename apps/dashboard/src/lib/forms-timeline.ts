// The Questions tab's forms on the candidate's timeline (design system section 11, Form timeline): pure functions over a
// workflow. A follow-up form has no "when" of its own; its moment is the trigger of the automation that sends it, so the
// timeline words that trigger, and "Ask more later" makes the form and that automation together. No Svelte runes here, so
// the words and the cascade are tested without a page.
import type { Automation, AutomationTrigger, FollowUpQuestionnaire, HiringWorkflow, WorkflowStage } from '@screenhire/api-client';
import { followUpsOf, idFrom, isTerminal, newQuestion, triggerLabels } from '#lib/workflow.js';

/** One card of the strip. `id` is `apply` or a follow-up questionnaire's id. */
export type TimelineForm = {
  id: string;
  /** When it reaches the candidate: "When they apply", "When they reach Trial shift", "3 days after Trial shift starts". */
  when: string;
  /** The form's name; the apply form is "Apply form". */
  name: string;
  /** Whether an enabled automation sends it. The apply form is always sent. */
  sent: boolean;
};

export const APPLY_FORM_ID = 'apply';

/** The most a form may wait after a stage starts: the validator's 8760 hours. */
export const MAX_DAYS_LATER = 365;

const NAME_LENGTH = 80;
const clip = (text: string, length = NAME_LENGTH): string => (text.length > length ? text.slice(0, length).trimEnd() : text);
const lowerFirst = (text: string): string => text.replace(/^./, (first) => first.toLowerCase());
const plural = (count: number, word: string): string => `${count} ${word}${count === 1 ? '' : 's'}`;

/** The stages "Ask more later" offers: open stages after Applied (the apply form already covers that moment). */
export const laterStages = (workflow: HiringWorkflow): WorkflowStage[] =>
  workflow.stages.filter((stage) => stage.kind !== 'Applied' && !isTerminal(stage.kind));

/** The automations that email this form, whether they are enabled or not. */
const sendersOf = (workflow: HiringWorkflow, formId: string): Automation[] =>
  workflow.automations.filter((automation) => automation.actions.some((action) => action.type === 'SendQuestionnaire' && action.questionnaireId === formId));

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
    // The apply form's card says "When they apply"; a form an automation sends on the same event comes right after it.
    case 'ApplicationSubmitted':
      return 'Right after they apply';
    case 'StageEntered':
      return `When they reach ${stageName(workflow, trigger.stageId)}`;
    case 'StageExited':
      return `When they leave ${stageName(workflow, trigger.stageId)}`;
    case 'TimeElapsed':
      return trigger.afterHours ? `${waitWords(trigger.afterHours)} after ${stageName(workflow, trigger.stageId)} starts` : `When they reach ${stageName(workflow, trigger.stageId)}`;
    default:
      return `When ${lowerFirst(triggerLabels[trigger.type] ?? 'it runs')}`;
  }
}

// Where a moment sits on the timeline: by the stage's place in the pipeline, then by the wait inside it. A form sent on
// submission comes first, right after the apply form. Other triggers that belong to no stage go last, in the order the
// workflow lists them.
function position(workflow: HiringWorkflow, trigger: AutomationTrigger): [number, number] {
  if (trigger.type === 'ApplicationSubmitted') return [-1, 0];
  const stage = workflow.stages.findIndex((candidate) => candidate.id === trigger.stageId);
  if (stage < 0) return [Number.MAX_SAFE_INTEGER, 0];
  return [stage, trigger.type === 'TimeElapsed' ? (trigger.afterHours ?? 0) : trigger.type === 'StageExited' ? Number.MAX_SAFE_INTEGER : 0];
}

/** The forms in the order the candidate meets them: the apply form, then each follow-up by its moment. */
export function timelineOf(workflow: HiringWorkflow): TimelineForm[] {
  const later = followUpsOf(workflow).map((form, order) => {
    const senders = sendersOf(workflow, form.id).filter((automation) => automation.enabled !== false);
    const first = senders[0];
    return {
      order,
      at: first ? position(workflow, first.trigger) : ([Number.MAX_SAFE_INTEGER, order] as [number, number]),
      card: {
        id: form.id,
        when: first ? momentOf(workflow, first.trigger) : 'Not sent yet',
        name: form.name || form.id,
        sent: Boolean(first),
      } satisfies TimelineForm,
    };
  });
  later.sort((a, b) => a.at[0] - b.at[0] || a.at[1] - b.at[1] || a.order - b.order);
  return [{ id: APPLY_FORM_ID, when: 'When they apply', name: 'Apply form', sent: true }, ...later.map((entry) => entry.card)];
}

/** What "Ask more later" asks: the stage, and a number of days after it starts, or none to send it on reaching the stage. */
export type FormLater = { stageId: string; days?: number | null };

/**
 * Adds a follow-up form for the moment and the automation that emails it. The form is named after the moment ("Trial shift
 * questions"), has one question to start from, and its id and name are unique. The automation is named after the moment,
 * not the form, so renaming the form does not leave it stale. Returns a new workflow; the argument is not changed.
 */
export function addFormLater(workflow: HiringWorkflow, later: FormLater): { workflow: HiringWorkflow; formId: string } {
  const stage = stageName(workflow, later.stageId);
  const days = later.days && later.days > 0 ? Math.min(Math.floor(later.days), MAX_DAYS_LATER) : null;
  const followUps = followUpsOf(workflow);

  const base = days ? `${stage} questions after ${plural(days, 'day')}` : `${stage} questions`;
  const names = new Set(followUps.map((form) => form.name));
  let name = clip(base);
  // The number goes on after the stem is cut, so a long stage name still gets a name the set does not hold.
  for (let n = 2; names.has(name); n++) name = `${clip(base, NAME_LENGTH - ` ${n}`.length)} ${n}`;

  const formId = idFrom(name, followUps.map((form) => form.id), 'questionnaire');
  const form: FollowUpQuestionnaire = { id: formId, name, questions: [newQuestion(workflow)] };

  const trigger: AutomationTrigger = days
    ? { type: 'TimeElapsed', stageId: later.stageId, afterHours: days * 24 }
    : { type: 'StageEntered', stageId: later.stageId };
  const automation: Automation = {
    id: idFrom(`send ${formId}`, workflow.automations.map((existing) => existing.id), 'automation'),
    name: clip(days ? `Email the form ${plural(days, 'day')} after ${stage} starts` : `Email the form when they reach ${stage}`),
    trigger,
    conditions: null,
    actions: [{ type: 'SendQuestionnaire', questionnaireId: formId }],
    enabled: true,
    allowAutoReject: false,
  };

  return { workflow: { ...workflow, followUpQuestionnaires: [...followUps, form], automations: [...workflow.automations, automation] }, formId };
}

/**
 * Removes a follow-up form and what would be left pointing at it, so the workflow stays valid. Returns the names of the
 * automations that were removed or lost a step, for the confirmation ("This also removes ..."):
 * - an automation whose only step sent this form is removed, and one that does more keeps its other steps;
 * - an automation that runs when this form is answered is removed;
 * - an automation that runs when any form is answered is removed once the last form goes;
 * - a rule that reads one of the form's answers is dropped from the conditions that held it.
 * The apply form cannot be removed, and an unknown id changes nothing.
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
