// An automation read and edited as one sentence with blanks (design system section 11, Automation sentence): "When
// [someone applies] and [their answer to Right to work] [is] [No], [ask] [the team] to review and [add the tag] [Not
// eligible]". Every blank is a short list of plain choices. This module is the model behind it and nothing else: it turns an
// automation into parts, and each blank's `apply` returns the automation with that one choice made. The definition stays the
// one format, the validator (DefinitionValidator) stays the judge, and the full form is still one press away (Edit as rules).
//
// The guardrail lives here too (workflow-engine.md section 2, guardrails 1 and 6): a check can only ask for a closer look,
// a person decides. `violations` ports the validator's reasoning, including its one-remove rule (a tag a check adds, a stage
// it moves into), and every choice that would create a violation the workflow did not have is not offered.
import type {
  ActionType,
  Automation,
  AutomationAction,
  AutomationCondition,
  ConditionOperator,
  HiringWorkflow,
  StageKind,
  TriggerType,
} from '@screenhire/api-client';
import {
  actionsFor,
  allQuestions,
  builtInEmailTemplates,
  describeAction,
  factsFor,
  followUpName,
  followUpsOf,
  hasTimeLimit,
  newAction,
  newRule,
  operatorsFor,
  ruleOperand,
  timeLimitActions,
  triggerLabels,
  triggerMayHaveQuestionnaire,
  triggerNeedsStage,
  triggerTypes,
  type FactOption,
} from './workflow.js';

// ---- The guardrail -------------------------------------------------------------------------------------------------

/** What an automation driven by a check may do: ask for a closer look, never act on the person. DefinitionValidator.SignalSafeActions. */
export const checkSafeActions: readonly ActionType[] = ['RequestVerification', 'AssignReviewer', 'AddTag', 'MoveToStage'];

/** The review stages a check may route into. Not Custom, Offer or an end stage. DefinitionValidator.SignalSafeStages. */
export const checkSafeStageKinds: readonly StageKind[] = ['Screening', 'Assessment', 'Verification', 'Interview'];

/** The one line that says why the choices are few, shown once in the then-list. */
export const checkGuardrailLine = 'A check can only ask for a closer look. A person decides.';

/**
 * Said under a list when it leaves choices out because they would let a check or a time limit start something it may not:
 * an email, an interview or turning someone down.
 */
export const leftOutLine = 'Some choices are left out: a check or a time limit cannot start emails, interviews or turning someone down.';

/** Said under the stages when a check moves people into some of them. */
export const stagesRefusedLine = 'Stages a check moves people into are left out: what runs there can only ask for a closer look.';

const isCheckFact = (fact: string): boolean => fact.startsWith('verification.') || fact.startsWith('integrity.');

const drivenByCheck = (automation: Automation): boolean =>
  automation.trigger.type === 'VerificationAssessed' ||
  automation.trigger.type === 'VerificationCompleted' ||
  (automation.conditions?.rules ?? []).some((rule) => isCheckFact(rule.fact));

const drivenByTimeLimit = (automation: Automation): boolean => automation.trigger.type === 'SlaBreached';

const toNumber = (text: string | null | undefined): number | null => {
  const trimmed = text?.trim() ?? '';
  return /^[-+]?\d+(\.\d+)?$/.test(trimmed) ? Number(trimmed) : null;
};

const sameValue = (value: string, operand: string | null | undefined): boolean => {
  if (operand === null || operand === undefined) return false;
  const left = toNumber(value);
  const right = toNumber(operand);
  return left !== null && right !== null ? left === right : value.toLowerCase() === operand.toLowerCase();
};

/** One rule against the tags an application carries, as AutomationEvaluator.Holds reads them. */
export function ruleHoldsForTags(rule: AutomationCondition, tags: readonly string[]): boolean {
  const compare = (test: (order: number) => boolean) => {
    const right = toNumber(rule.value);
    return right !== null && tags.some((tag) => toNumber(tag) !== null && test(Math.sign(toNumber(tag)! - right)));
  };
  const held = (() => {
    switch (rule.operator) {
      case 'Exists':
        return tags.length > 0;
      case 'EqualTo':
        return tags.some((tag) => sameValue(tag, rule.value));
      case 'In':
        return tags.some((tag) => (rule.values ?? []).some((operand) => sameValue(tag, operand)));
      case 'Contains': {
        const part = rule.value;
        return part !== null && part !== undefined && tags.some((tag) => tag.toLowerCase().includes(part.toLowerCase()));
      }
      case 'GreaterThan':
        return compare((order) => order > 0);
      case 'GreaterThanOrEqual':
        return compare((order) => order >= 0);
      case 'LessThan':
        return compare((order) => order < 0);
      case 'LessThanOrEqual':
        return compare((order) => order <= 0);
      default:
        return false;
    }
  })();
  return held !== Boolean(rule.negate);
}

function readsTaintedTag(rule: AutomationCondition, tainted: ReadonlySet<string>): boolean {
  return tainted.size > 0 && (Boolean(rule.negate) || [...tainted].some((tag) => ruleHoldsForTags(rule, [tag])));
}

function drivenAtOneRemove(automation: Automation, taintedTags: ReadonlySet<string>, taintedStages: ReadonlySet<string>, movesOut: boolean): boolean {
  const { trigger } = automation;
  if (
    (trigger.stageId && taintedStages.has(trigger.stageId)) ||
    (trigger.type === 'StageExited' && movesOut) ||
    ((trigger.type === 'ReviewerDecision' || trigger.type === 'SlaBreached') && !trigger.stageId && movesOut)
  ) {
    return true;
  }
  return (automation.conditions?.rules ?? []).some((rule) => rule.fact === 'application.tags' && readsTaintedTag(rule, taintedTags));
}

/**
 * The automations something can drive, directly or at one remove, by the validator's own fixed point
 * (DefinitionValidator.DrivenAutomations): a tag a driven automation adds, a stage it moves into, any StageExited trigger
 * once one moves applications, and a stageless reviewer decision or time limit once one does.
 */
export function drivenAutomations(automations: readonly Automation[], drivenDirectly: (automation: Automation) => boolean): Set<number> {
  const driven = new Set<number>();
  const tags = new Set<string>();
  const stages = new Set<string>();
  let movesOut = false;
  let found: boolean;
  do {
    found = false;
    automations.forEach((automation, index) => {
      if (driven.has(index) || !(drivenDirectly(automation) || drivenAtOneRemove(automation, tags, stages, movesOut))) return;
      driven.add(index);
      found = true;
      for (const action of automation.actions) {
        if (action.type === 'AddTag' && action.tag !== null && action.tag !== undefined) tags.add(action.tag.toLowerCase());
        else if (action.type === 'MoveToStage' && action.stageId) {
          stages.add(action.stageId);
          movesOut = true;
        }
      }
    });
  } while (found);
  return driven;
}

/** The indexes of the automations a check drives, which may only ask for a closer look. */
export const checkDrivenIds = (workflow: HiringWorkflow): Set<number> => drivenAutomations(workflow.automations, drivenByCheck);

/**
 * The steps the guardrails refuse, as `automation id#step` keys: a step a check drives outside the check-safe actions
 * and stages (guardrail 1), and a step a passed time limit drives outside its reminders (section 8). The validator's
 * messages stay what people read; this is what keeps the builder from offering the pair.
 */
export function violations(workflow: HiringWorkflow): Set<string> {
  const stageOf = (id: string | null | undefined) => workflow.stages.find((stage) => stage.id === id);
  const checks = drivenAutomations(workflow.automations, drivenByCheck);
  const delays = drivenAutomations(workflow.automations, drivenByTimeLimit);
  const found = new Set<string>();
  workflow.automations.forEach((automation, index) => {
    automation.actions.forEach((action, step) => {
      const key = `${automation.id}#${step}`;
      const target = stageOf(action.stageId);
      const onLimit = automation.trigger.type === 'SlaBreached';
      if (onLimit && !timeLimitActions.includes(action.type)) found.add(key);
      if (checks.has(index) && (!checkSafeActions.includes(action.type) || (action.type === 'MoveToStage' && target && !checkSafeStageKinds.includes(target.kind)))) {
        found.add(key);
      }
      if (
        delays.has(index) &&
        !onLimit &&
        !timeLimitActions.includes(action.type) &&
        !(action.type === 'MoveToStage' && target && checkSafeStageKinds.includes(target.kind))
      ) {
        found.add(key);
      }
    });
  });
  return found;
}

// Limits of this check: it asks about the choices a list can offer. Text a person types (a tag's name, a tag condition's
// word) is not a list, so a tag typed to match one a check adds is caught by the validator's message on that blank, which is
// marked at once, and not here. Keys are `automation id#step`, so a step swapped for another refused one is not new.
const withAutomation = (workflow: HiringWorkflow, index: number, automation: Automation): HiringWorkflow => ({
  ...workflow,
  automations: workflow.automations.map((existing, i) => (i === index ? automation : existing)),
});

/** Whether making `automation` the one at `index` creates a refused step the workflow did not already have. */
export function breaks(workflow: HiringWorkflow, index: number, automation: Automation): boolean {
  const before = violations(workflow);
  return [...violations(withAutomation(workflow, index, automation))].some((key) => !before.has(key));
}

/** The same, for a whole new workflow (an automation added at the end). */
export function breaksWorkflow(before: HiringWorkflow, after: HiringWorkflow): boolean {
  const known = violations(before);
  return [...violations(after)].some((key) => !known.has(key));
}

// ---- Parts and blanks -----------------------------------------------------------------------------------------------

export interface Choice {
  id: string;
  label: string;
  /** A heading the choice sits under (the facts: Their answers, The application, ScreenHire's checks). */
  group?: string;
}

interface BlankBase {
  /** Unique within the sentence. */
  id: string;
  /** The blank's name for a screen reader: "Trigger", "Condition 1", "Action 2". */
  name: string;
  /** What the button says. */
  text: string;
  /** The definition path whose validator messages mark this blank invalid, and (for a first blank) `data-focus`. */
  errorPath: string;
  focusPath?: string;
  /** One quiet line at the end of the list. */
  note?: string;
  /** Removes the part the blank belongs to (a condition, a step): offered inside the list. */
  remove?: { label: string; apply: () => Automation };
  /** A plus blank ("and if", "then also") that adds a part rather than showing a choice. */
  adds?: boolean;
  /** Shown instead of `text` while nothing is chosen. */
  placeholder?: string;
}

export interface ChoiceBlank extends BlankBase {
  kind: 'choice';
  choices: readonly Choice[];
  selected: string | null;
  apply: (id: string) => Automation;
}

export interface MultiBlank extends BlankBase {
  kind: 'multi';
  choices: readonly Choice[];
  selected: readonly string[];
  apply: (ids: readonly string[]) => Automation;
}

export interface TextBlank extends BlankBase {
  kind: 'text';
  value: string;
  numeric?: boolean;
  hint?: string;
  maxLength?: number;
  apply: (value: string) => Automation;
}

export type Blank = ChoiceBlank | MultiBlank | TextBlank;

export type Part = { type: 'text'; text: string; tight?: boolean } | { type: 'blank'; blank: Blank };

/** What the sentence needs besides the workflow: the organization's emails and the people who can be asked to review. */
export interface SentenceContext {
  emailTemplates: readonly { id: string; name: string }[];
  reviewers: readonly { userId: string; displayName: string }[];
  /** Whether the team has loaded, so a reviewer who is not in the list is "a former teammate" rather than unknown. */
  teamKnown?: boolean;
}

const text = (value: string, tight = false): Part => ({ type: 'text', text: value, ...(tight && { tight }) });
const blank = (value: Blank): Part => ({ type: 'blank', blank: value });

// ---- Triggers ------------------------------------------------------------------------------------------------------

/** The kind of trigger as the words after "When": "someone applies". */
export const triggerKindWords: Partial<Record<TriggerType, string>> = {
  ApplicationSubmitted: 'someone applies',
  StageEntered: 'someone moves into',
  StageExited: 'someone leaves',
  TimeElapsed: 'someone has waited',
  QuestionnaireCompleted: 'someone answers',
  VerificationAssessed: 'the checks finish',
  ReviewerDecision: 'a reviewer decides on',
  SlaBreached: 'the time limit passes in',
  InterviewScheduled: 'an interview is booked',
  Manual: 'someone runs it by hand',
};

const stageName = (workflow: HiringWorkflow, id: string | null | undefined): string => workflow.stages.find((stage) => stage.id === id)?.name ?? id ?? 'a stage';

/** "2 days", "1 week", "30 hours": how long a candidate has waited. */
export function waitLabel(hours: number): string {
  if (hours % 168 === 0) return hours === 168 ? '1 week' : `${hours / 168} weeks`;
  if (hours % 24 === 0) return hours === 24 ? '1 day' : `${hours / 24} days`;
  return hours === 1 ? '1 hour' : `${hours} hours`;
}

/** The plain waits a time-passes trigger offers; a saved other value stays offered. */
export const waitChoices: readonly number[] = [24, 48, 72, 120, 168];

/**
 * The automation with its trigger changed to `type`: a trigger that names a stage keeps the current one (or the first), a
 * passed time limit keeps its stage only when that stage has a limit, a time wait starts at 48 hours, and a completed
 * questionnaire starts on the first follow-up.
 */
export function changeTrigger(automation: Automation, workflow: HiringWorkflow, type: TriggerType): Automation {
  const current = automation.trigger.stageId;
  const keepsStage = type === 'SlaBreached' && workflow.stages.some((stage) => stage.id === current && hasTimeLimit(stage));
  const stageId = triggerNeedsStage(type) ? (current ?? workflow.stages[0]?.id ?? '') : keepsStage ? current : undefined;
  const questionnaireId = triggerMayHaveQuestionnaire(type) ? followUpsOf(workflow)[0]?.id : undefined;
  return {
    ...automation,
    trigger: {
      type,
      ...(stageId !== undefined && stageId !== null && { stageId }),
      ...(type === 'TimeElapsed' && { afterHours: automation.trigger.afterHours ?? 48 }),
      ...(questionnaireId !== undefined && { questionnaireId }),
    },
  };
}

/** A trigger in the few words a map node carries: "Someone applies", "Moves to Phone call", "2 days pass in Phone call". */
export function triggerTitle(trigger: Automation['trigger'], workflow: HiringWorkflow): string {
  const stage = stageName(workflow, trigger.stageId);
  switch (trigger.type) {
    case 'ApplicationSubmitted':
      return 'Someone applies';
    case 'StageEntered':
      return `Moves to ${stage}`;
    case 'StageExited':
      return `Leaves ${stage}`;
    case 'TimeElapsed':
      return `${waitLabel(trigger.afterHours ?? 0)} pass in ${stage}`;
    case 'QuestionnaireCompleted':
      return trigger.questionnaireId ? `Answers ${followUpName(workflow, trigger.questionnaireId)}` : 'Answers a follow-up';
    case 'VerificationAssessed':
    case 'VerificationCompleted':
      return 'The checks finish';
    case 'ReviewerDecision':
      return trigger.stageId ? `A reviewer decides on ${stage}` : 'A reviewer decides';
    case 'SlaBreached':
      return trigger.stageId ? `Time limit passes in ${stage}` : 'A time limit passes';
    case 'InterviewScheduled':
      return 'An interview is booked';
    case 'Manual':
      return 'Run by hand';
    default:
      return triggerLabels[trigger.type] ?? trigger.type;
  }
}

function triggerParts(automation: Automation, index: number, workflow: HiringWorkflow): Part[] {
  const path = `automations[${index}]`;
  const { trigger } = automation;
  const errorPath = `${path}.trigger`;
  const set = (next: Partial<Automation['trigger']>): Automation => ({ ...automation, trigger: { ...trigger, ...next } });

  // A kind that would make this automation a check's, with a step a check cannot start, is left out.
  let refused = false;
  // A passed time limit cannot read the hours in the stage (the validator refuses it), so it is not offered while a
  // condition does.
  const readsHours = (automation.conditions?.rules ?? []).some((rule) => rule.fact === 'application.hoursInStage');
  const kinds = triggerTypes.filter((type) => {
    if (type === trigger.type) return true;
    if (type === 'SlaBreached' && readsHours) return false;
    if (!breaks(workflow, index, changeTrigger(automation, workflow, type))) return true;
    refused = true;
    return false;
  });
  const parts: Part[] = [
    text('When'),
    blank({
      kind: 'choice',
      id: 'trigger',
      name: 'Trigger',
      text: triggerKindWords[trigger.type] ?? triggerLabels[trigger.type] ?? trigger.type,
      errorPath,
      focusPath: errorPath,
      choices: kinds.map((type) => ({ id: type, label: capital(triggerKindWords[type] ?? triggerLabels[type] ?? type) })),
      selected: trigger.type,
      note: refused ? leftOutLine : undefined,
      apply: (id) => changeTrigger(automation, workflow, id as TriggerType),
    }),
  ];

  const stageBlank = (optional: boolean, only: (stage: HiringWorkflow['stages'][number]) => boolean): Blank => {
    let hidden = false;
    const stages = workflow.stages.filter((stage) => {
      if (!only(stage) && stage.id !== trigger.stageId) return false;
      if (stage.id === trigger.stageId || !breaks(workflow, index, withTrigger(automation, { ...trigger, stageId: stage.id }))) return true;
      hidden = true;
      return false;
    });
    const choices: Choice[] = [
      ...(optional ? [{ id: '', label: 'Any stage' }] : []),
      ...stages.map((stage) => ({ id: stage.id, label: stage.name })),
    ];
    if (trigger.stageId && !choices.some((choice) => choice.id === trigger.stageId)) choices.push({ id: trigger.stageId, label: stageName(workflow, trigger.stageId) });
    return {
      kind: 'choice',
      id: 'trigger-stage',
      name: 'Stage',
      text: trigger.stageId ? stageName(workflow, trigger.stageId) : 'any stage',
      errorPath,
      choices,
      selected: trigger.stageId ?? '',
      note: hidden ? stagesRefusedLine : undefined,
      apply: (id) => ({ ...automation, trigger: stripped({ ...trigger, stageId: id || undefined }) }),
    };
  };

  switch (trigger.type) {
    case 'StageEntered':
    case 'StageExited':
      parts.push(blank(stageBlank(false, () => true)));
      break;
    case 'TimeElapsed': {
      const hours = trigger.afterHours ?? 48;
      const options = waitChoices.includes(hours) ? waitChoices : [...waitChoices, hours].toSorted((a, b) => a - b);
      parts.push(
        blank({
          kind: 'choice',
          id: 'trigger-wait',
          name: 'Wait',
          text: waitLabel(hours),
          errorPath,
          choices: options.map((value) => ({ id: String(value), label: waitLabel(value) })),
          selected: String(hours),
          apply: (id) => set({ afterHours: Number(id) }),
        }),
        text('in'),
        blank(stageBlank(false, () => true)),
      );
      break;
    }
    case 'ReviewerDecision':
      parts.push(text('on'), blank(stageBlank(true, (stage) => Boolean(stage.scorecard ?? stage.scorecardTemplateId))));
      break;
    case 'SlaBreached':
      parts.push(blank(stageBlank(true, hasTimeLimit)));
      break;
    case 'QuestionnaireCompleted': {
      const followUps = followUpsOf(workflow);
      const choices: Choice[] = [{ id: '', label: 'Any follow-up' }, ...followUps.map((followUp) => ({ id: followUp.id, label: followUp.name || followUp.id }))];
      if (trigger.questionnaireId && !choices.some((choice) => choice.id === trigger.questionnaireId)) choices.push({ id: trigger.questionnaireId, label: trigger.questionnaireId });
      parts.push(
        blank({
          kind: 'choice',
          id: 'trigger-questionnaire',
          name: 'Follow-up questionnaire',
          text: trigger.questionnaireId ? followUpName(workflow, trigger.questionnaireId) : 'any follow-up',
          errorPath,
          choices,
          selected: trigger.questionnaireId ?? '',
          apply: (id) => ({ ...automation, trigger: stripped({ ...trigger, questionnaireId: id || undefined }) }),
        }),
      );
      break;
    }
    default:
      break;
  }
  return parts;
}

const withTrigger = (automation: Automation, trigger: Automation['trigger']): Automation => ({ ...automation, trigger });

/** A trigger without the members the API treats as absent. */
function stripped(trigger: Automation['trigger']): Automation['trigger'] {
  return {
    type: trigger.type,
    ...(trigger.stageId ? { stageId: trigger.stageId } : {}),
    ...(trigger.afterHours !== null && trigger.afterHours !== undefined ? { afterHours: trigger.afterHours } : {}),
    ...(trigger.questionnaireId ? { questionnaireId: trigger.questionnaireId } : {}),
  };
}

const capital = (value: string): string => value.replace(/^./, (first) => first.toUpperCase());

// ---- Conditions ----------------------------------------------------------------------------------------------------

export const factGroupAnswers = 'Their answers';
export const factGroupApplication = 'The application';
export const factGroupChecks = "ScreenHire's checks";

/** The group a fact is listed under. */
export const factGroup = (fact: string): string =>
  fact.startsWith('answers.') ? factGroupAnswers : isCheckFact(fact) ? factGroupChecks : factGroupApplication;

/** A fact in the few words a list holds: the question's own label, "Tags", "Verification". */
function factLabel(option: FactOption, workflow: HiringWorkflow): string {
  if (option.fact.startsWith('answers.')) {
    const id = option.fact.slice('answers.'.length);
    const question = allQuestions(workflow).find((candidate) => candidate.id === id);
    const from = followUpsOf(workflow).find((followUp) => followUp.questions.some((candidate) => candidate.id === id));
    const label = question?.label || id;
    return from ? `${label} (${from.name || from.id})` : label;
  }
  if (option.fact === 'application.tags') return 'Tags';
  if (option.fact === 'application.source') return 'Where they applied';
  if (option.fact === 'application.hoursInStage') return 'Hours in the stage';
  if (option.fact === 'verification.aiAssistance') return 'AI help';
  if (option.fact === 'reviewer.recommendation') return "The reviewer's recommendation";
  return option.label;
}

/** The fact as the chip in the sentence says it: "their answer to Right to work", "their tags", "Verification". */
function factPhrase(option: FactOption, workflow: HiringWorkflow): string {
  if (option.fact.startsWith('answers.')) return `their answer to ${factLabel(option, workflow)}`;
  if (option.fact === 'application.tags') return 'their tags';
  if (option.fact === 'application.source') return 'where they applied';
  if (option.fact === 'application.hoursInStage') return 'the hours in the stage';
  if (option.fact === 'reviewer.recommendation') return "the reviewer's recommendation";
  return factLabel(option, workflow);
}

const listType = (type: FactOption['type']): boolean => type === 'TextList' || type === 'MultiChoice';

/** Whether a fact is picked from a fixed list of values, which the value blank ticks. */
const hasValues = (option: FactOption | undefined): option is FactOption & { values: NonNullable<FactOption['values']> } => Boolean(option?.values?.length);

/** The operator a ticked list stores for one value, and the operator for two or more is always `In`. */
const primaryOperator = (option: FactOption): ConditionOperator => (option.type === 'MultiChoice' ? 'Contains' : 'EqualTo');

/** Words for a relation, by fact type. */
export function relationWords(operator: ConditionOperator, negate: boolean, type: FactOption['type']): string {
  const list = listType(type);
  switch (operator) {
    case 'EqualTo':
      return negate ? 'is not' : 'is';
    case 'In':
      return negate ? 'is not one of' : 'is one of';
    case 'Contains':
      return list ? (negate ? 'does not include' : 'includes') : negate ? 'does not contain' : 'contains';
    case 'GreaterThan':
      return negate ? 'is not more than' : 'is more than';
    case 'GreaterThanOrEqual':
      return negate ? 'is not at least' : 'is at least';
    case 'LessThan':
      return negate ? 'is not less than' : 'is less than';
    case 'LessThanOrEqual':
      return negate ? 'is not at most' : 'is at most';
    case 'Exists':
      return negate ? 'is not given' : 'is given';
  }
}

/** The relations a fact offers, as `operator|negate` ids. A fact with a fixed list offers is, is not, is given and is not given. */
export function relationChoices(option: FactOption | undefined, current: AutomationCondition): Choice[] {
  const type = option?.type ?? 'Text';
  const operators: ConditionOperator[] = hasValues(option)
    ? [primaryOperator(option), 'Exists']
    : operatorsFor(type);
  const negatable = new Set<ConditionOperator>(['EqualTo', 'In', 'Contains', 'Exists']);
  const ids: { operator: ConditionOperator; negate: boolean }[] = operators.flatMap((operator) => [
    { operator, negate: false },
    ...(negatable.has(operator) ? [{ operator, negate: true }] : []),
  ]);
  const currentId = relationId(option, current);
  if (!ids.some((entry) => `${entry.operator}|${entry.negate ? 1 : 0}` === currentId)) {
    ids.push({ operator: current.operator, negate: Boolean(current.negate) });
  }
  return ids.map(({ operator, negate }) => ({ id: `${operator}|${negate ? 1 : 0}`, label: relationWords(operator, negate, type) }));
}

/** A rule's relation id; an `In` over a fixed list is the list's primary relation, since the ticks decide. */
function relationId(option: FactOption | undefined, rule: AutomationCondition): string {
  const operator = rule.operator === 'In' && hasValues(option) ? primaryOperator(option) : rule.operator;
  return `${operator}|${rule.negate ? 1 : 0}`;
}

/** The rule with another relation, keeping the value it can keep. */
export function withRelation(rule: AutomationCondition, option: FactOption | undefined, relation: string): AutomationCondition {
  const [operator, negated] = relation.split('|') as [ConditionOperator, string];
  const negate = negated === '1';
  const base: AutomationCondition = { fact: rule.fact, operator, ...(negate && { negate }) };
  if (operator === 'Exists') return base;
  const previous = rule.operator === 'In' ? (rule.values ?? []) : rule.value ? [rule.value] : [];
  if (operator === 'In') return { ...base, values: previous.length > 0 ? [...previous] : ruleOperand('In', option).values ?? [] };
  return { ...base, value: previous[0] ?? ruleOperand(operator, option).value ?? '' };
}

/** The rule with these values ticked: one stores the primary relation, two or more store `In`. */
export function withValues(rule: AutomationCondition, option: FactOption, ids: readonly string[]): AutomationCondition {
  const negate = rule.negate ? { negate: true } : {};
  if (ids.length === 1) return { fact: rule.fact, operator: primaryOperator(option), ...negate, value: ids[0]! };
  return { fact: rule.fact, operator: 'In', ...negate, values: [...ids] };
}

/**
 * The facts the sentence offers for a condition. A saved condition on a fact left out here still reads (its fact is added
 * to its own list), so nothing saved is hidden.
 */
function offeredFacts(workflow: HiringWorkflow, automation: Automation, index: number): { options: FactOption[]; refused: boolean } {
  let refused = false;
  const options = factsFor(workflow, automation.trigger.type).filter((option) => {
    // AI help never sorts, filters or counts, so the sentence never offers it; a saved one stays in the rules form.
    if (option.fact === 'verification.aiAssistance') return false;
    // A reminder for a passed time limit may not wait on timing: it would be skipped for a candidate with an accommodation,
    // who would then wait with nobody told (DefinitionValidator, guardrail 4).
    if (option.fact === 'application.hoursInStage' && automation.trigger.type === 'SlaBreached') return false;
    // A check, or a tag rule that would read a tag a check adds, makes this automation a check's.
    const probe = { ...automation, conditions: { match: automation.conditions?.match ?? ('All' as const), rules: [...(automation.conditions?.rules ?? []), newRule(option)] } };
    if (breaks(workflow, index, probe)) {
      refused = true;
      return false;
    }
    return true;
  });
  return { options, refused };
}

function factChoices(options: readonly FactOption[], workflow: HiringWorkflow, current?: FactOption): Choice[] {
  const all = current && !options.some((option) => option.fact === current.fact) ? [...options, current] : options;
  const groups = [factGroupAnswers, factGroupApplication, factGroupChecks];
  return groups.flatMap((group) =>
    all.filter((option) => factGroup(option.fact) === group).map((option) => ({ id: option.fact, label: factLabel(option, workflow), group })),
  );
}

function valueText(rule: AutomationCondition, option: FactOption | undefined): string {
  const word = (value: string) => option?.values?.find((candidate) => candidate.value === value)?.label ?? value;
  const values = rule.operator === 'In' ? (rule.values ?? []) : rule.value ? [rule.value] : [];
  return values.map(word).join(' or ');
}

/**
 * A condition in the few words a map node or a list holds: "Right to work is No", "Verification is Review or Verify",
 * "Tags includes referral". The sentence says the same with "their answer to" before the question.
 */
export function ruleTitle(rule: AutomationCondition, workflow: HiringWorkflow, trigger: TriggerType): string {
  const option = factsFor(workflow, trigger).find((candidate) => candidate.fact === rule.fact);
  const [operator, negated] = relationId(option, rule).split('|') as [ConditionOperator, string];
  const relation = relationWords(operator, negated === '1', option?.type ?? 'Text');
  const value =
    rule.operator === 'Exists' ? '' : hasValues(option) ? valueText(rule, option) : rule.operator === 'In' ? (rule.values ?? []).join(', ') : (rule.value ?? '');
  return `${option ? factLabel(option, workflow) : rule.fact} ${relation}${value ? ` ${value}` : ''}`;
}

const lowerFirst = (text: string): string => text.charAt(0).toLowerCase() + text.slice(1);

/**
 * An automation in one line, in the map's words: "When someone applies and Right to work is No, queue for rejection
 * review." The Automation list's row. It reads the same titles the map's nodes carry and offers no choices, so a list of
 * fifty automations costs fifty strings, not fifty sentences' worth of guardrail checks.
 */
export function automationLine(
  automation: Automation,
  workflow: HiringWorkflow,
  members?: Parameters<typeof describeAction>[2],
  templateNames?: Parameters<typeof describeAction>[3],
): string {
  const rules = automation.conditions?.rules ?? [];
  const titles = rules.map((rule) => ruleTitle(rule, workflow, automation.trigger.type));
  const condition =
    titles.length === 0 ? '' : automation.conditions?.match === 'Any' && titles.length > 1 ? ` and one of ${titles.join(' or ')}` : ` and ${titles.join(' and ')}`;
  const steps = automation.actions.map((action) => lowerFirst(describeAction(action, workflow, members, templateNames)));
  return `When ${triggerClause(automation.trigger, workflow)}${condition}, ${steps.length > 0 ? steps.join(' and ') : 'nothing happens yet'}.`;
}

/** The map's trigger title as the clause after "When": a node title such as "Moves to Phone call" has no subject. */
function triggerClause(trigger: Automation['trigger'], workflow: HiringWorkflow): string {
  const title = lowerFirst(triggerTitle(trigger, workflow));
  switch (trigger.type) {
    case 'StageEntered':
    case 'StageExited':
    case 'QuestionnaireCompleted':
      return `someone ${title}`;
    case 'SlaBreached':
      return trigger.stageId ? `the ${title}` : title;
    case 'Manual':
      return 'it is run by hand';
    default:
      return title;
  }
}

function conditionParts(automation: Automation, index: number, workflow: HiringWorkflow, ctx: SentenceContext): Part[] {
  void ctx;
  const path = `automations[${index}]`;
  const rules = automation.conditions?.rules ?? [];
  const match = automation.conditions?.match ?? 'All';
  const trigger = automation.trigger.type;
  const { options, refused } = offeredFacts(workflow, automation, index);
  const parts: Part[] = [];
  const setRules = (next: AutomationCondition[]): Automation => ({
    ...automation,
    conditions: next.length > 0 ? { match, rules: next } : null,
  });
  const replace = (at: number, rule: AutomationCondition) => setRules(rules.map((existing, i) => (i === at ? rule : existing)));

  rules.forEach((rule, at) => {
    const option = factsFor(workflow, trigger).find((candidate) => candidate.fact === rule.fact);
    const ruleName = `Condition ${at + 1}`;
    const errorPath = `${path}.conditions.rules[${at}]`;
    const remove = { label: 'Remove this condition', apply: () => setRules(rules.filter((_, i) => i !== at)) };

    if (at === 0) parts.push(text(match === 'Any' && rules.length > 1 ? 'and one of' : 'and'));
    else {
      parts.push(
        blank({
          kind: 'choice',
          id: 'match',
          name: 'Conditions join',
          text: match === 'Any' ? 'or' : 'and',
          errorPath: `${path}.conditions`,
          choices: [
            { id: 'All', label: 'and: all of them hold' },
            { id: 'Any', label: 'or: any one of them holds' },
          ],
          selected: match,
          apply: (id) => ({ ...automation, conditions: { match: id as 'All' | 'Any', rules } }),
        }),
      );
    }

    parts.push(
      blank({
        kind: 'choice',
        id: `rule-${at}-fact`,
        name: `${ruleName}: what to check`,
        text: option ? factPhrase(option, workflow) : rule.fact,
        errorPath,
        ...(at === 0 && { focusPath: `${path}.conditions` }),
        choices: factChoices(options, workflow, option),
        selected: rule.fact,
        note: refused ? leftOutLine : undefined,
        remove,
        apply: (id) => {
          const chosen = factsFor(workflow, trigger).find((candidate) => candidate.fact === id);
          return chosen ? replace(at, newRule(chosen)) : automation;
        },
      }),
    );

    const relation = relationChoices(option, rule);
    const chosenRelation = relationId(option, rule);
    parts.push(
      blank({
        kind: 'choice',
        id: `rule-${at}-relation`,
        name: `${ruleName}: how to compare`,
        text: relation.find((choice) => choice.id === chosenRelation)?.label ?? relationWords(rule.operator, Boolean(rule.negate), option?.type ?? 'Text'),
        errorPath,
        choices: relation,
        selected: chosenRelation,
        apply: (id) => replace(at, withRelation(rule, option, id)),
      }),
    );

    if (rule.operator === 'Exists') return;
    if (hasValues(option)) {
      const single = option.type === 'YesNo';
      const selected = rule.operator === 'In' ? (rule.values ?? []) : rule.value ? [rule.value] : [];
      const choices = option.values.map((value) => ({ id: value.value, label: value.label }));
      for (const id of selected) if (!choices.some((choice) => choice.id === id)) choices.push({ id, label: id });
      const common = { id: `rule-${at}-value`, name: `${ruleName}: value`, text: valueText(rule, option) || 'choose', errorPath, choices };
      parts.push(
        blank(
          single
            ? {
                ...common,
                kind: 'choice',
                selected: selected[0] ?? null,
                apply: (id) => replace(at, withValues(rule, option, [id])),
              }
            : {
                ...common,
                kind: 'multi',
                selected,
                apply: (ids) => (ids.length === 0 ? automation : replace(at, withValues(rule, option, ids))),
              },
        ),
      );
      return;
    }
    const numeric = option?.type === 'Number';
    const many = rule.operator === 'In';
    parts.push(
      blank({
        kind: 'text',
        id: `rule-${at}-value`,
        name: `${ruleName}: value`,
        text: many ? (rule.values ?? []).join(', ') : (rule.value ?? ''),
        placeholder: numeric ? 'a number' : 'a word',
        errorPath,
        value: many ? (rule.values ?? []).join(', ') : (rule.value ?? ''),
        numeric,
        hint: many ? 'Separated by commas.' : undefined,
        maxLength: 200,
        apply: (value) =>
          replace(
            at,
            many
              ? {
                  ...rule,
                  values: value
                    .split(',')
                    .map((item) => item.trim())
                    .filter(Boolean),
                }
              : { ...rule, value },
          ),
      }),
    );
  });

  // "And if" adds a condition by naming what to check.
  const addable = options.length > 0 ? options : [];
  if (addable.length > 0) {
    parts.push(
      blank({
        kind: 'choice',
        id: 'add-condition',
        name: rules.length === 0 ? 'Add a condition' : 'Add another condition',
        text: rules.length === 0 ? 'and if' : 'and if also',
        errorPath: `${path}.conditions`,
        adds: true,
        choices: factChoices(addable, workflow),
        selected: null,
        note: refused ? leftOutLine : undefined,
        apply: (id) => {
          const chosen = factsFor(workflow, trigger).find((candidate) => candidate.fact === id);
          return chosen ? setRules([...rules, newRule(chosen)]) : automation;
        },
      }),
    );
  }
  return parts;
}

// ---- Actions -------------------------------------------------------------------------------------------------------

/** An action kind in the list: "Send an email". */
export const actionKindChoiceLabels: Partial<Record<ActionType, string>> = {
  SendEmail: 'Send an email',
  SendQuestionnaire: 'Send a questionnaire',
  MoveToStage: 'Move to a stage',
  AddTag: 'Add a tag',
  AssignReviewer: 'Ask a teammate to review',
  NotifyTeam: 'Tell the team',
  CreateTask: 'Create a task',
  CallWebhook: 'Call a webhook',
  ScheduleInterview: 'Offer an interview',
  QueueForRejectionReview: 'Ask the team to review before turning them down',
  Reject: 'Turn them down',
};

/** An action kind as the chip in the sentence says it, before its own blank. */
export const actionKindWords: Partial<Record<ActionType, string>> = {
  SendEmail: 'send the email',
  SendQuestionnaire: 'send the questionnaire',
  MoveToStage: 'move them to',
  AddTag: 'add the tag',
  AssignReviewer: 'ask',
  NotifyTeam: 'tell the team',
  CreateTask: 'create the task',
  CallWebhook: 'call the webhook',
  ScheduleInterview: 'offer an interview',
  QueueForRejectionReview: 'ask the team to review before turning them down, because',
  Reject: 'turn them down, because',
};

/** The tag a probe uses to ask whether any tag at all would tangle another automation. */
const probeTag = '\u0001probe';

/**
 * The action kinds the sentence offers at step `step` (or a new step at the end). Never a rejection (a saved one stays
 * readable, adding one is Edit as rules), never RequestVerification (not available yet), and, for an automation a check
 * drives, only what the validator allows after a check. Each kind is offered only if choosing it creates no refused step
 * anywhere in the workflow.
 */
export function thenChoices(workflow: HiringWorkflow, index: number, step: number | null): ChoiceBlank['choices'] {
  const automation = workflow.automations[index]!;
  const current = step === null ? undefined : automation.actions[step]?.type;
  const followUps = followUpsOf(workflow);
  const candidates = actionsFor(automation.trigger.type, current).filter((type) => {
    if (type === current) return true;
    if (type === 'Reject' || type === 'RequestVerification') return false;
    return type !== 'SendQuestionnaire' || followUps.length > 0;
  });
  return candidates
    .filter((type) => {
      if (type === current) return true;
      const action = type === 'MoveToStage' ? moveCandidates(workflow, index, step).at(0) : startAction(workflow, index, step, type);
      if (!action) return false;
      const probe = type === 'AddTag' ? { ...action, tag: probeTag } : action;
      return !breaks(workflow, index, withAction(automation, step, probe));
    })
    .map((type) => ({ id: type, label: actionKindChoiceLabels[type] ?? type }));
}

/** A fresh step of `type`; a move starts on the first stage the guardrail allows, not just the first stage. */
function startAction(workflow: HiringWorkflow, index: number, step: number | null, type: ActionType): AutomationAction {
  if (type === 'MoveToStage') return moveCandidates(workflow, index, step)[0] ?? newAction(type, workflow.stages, followUpsOf(workflow));
  return newAction(type, workflow.stages, followUpsOf(workflow));
}

const withAction = (automation: Automation, step: number | null, action: AutomationAction): Automation => ({
  ...automation,
  actions: step === null || step >= automation.actions.length ? [...automation.actions, action] : automation.actions.map((existing, i) => (i === step ? action : existing)),
});

/** Move actions that create no refused step, one per stage a move may target. */
function moveCandidates(workflow: HiringWorkflow, index: number, step: number | null): AutomationAction[] {
  const automation = workflow.automations[index]!;
  const currentStage = step === null ? undefined : automation.actions[step]?.stageId;
  return workflow.stages
    .filter((stage) => stage.kind !== 'Rejected' && stage.kind !== 'Withdrawn')
    .map((stage): AutomationAction => ({ type: 'MoveToStage', stageId: stage.id }))
    .filter((action) => action.stageId === currentStage || !breaks(workflow, index, withAction(automation, step, action)));
}

const textFieldHints: Partial<Record<keyof AutomationAction, { placeholder: string; maxLength: number }>> = {
  // The validator's limits (DefinitionValidator.ValidateActionText): a tag 50, a note 1000, a reason 500.
  tag: { placeholder: 'name the tag', maxLength: 50 },
  note: { placeholder: 'add a message', maxLength: 1000 },
  url: { placeholder: 'a web address (https)', maxLength: 2000 },
  reason: { placeholder: 'give a reason', maxLength: 500 },
};

function actionParts(automation: Automation, index: number, workflow: HiringWorkflow, ctx: SentenceContext): Part[] {
  const path = `automations[${index}]`;
  const parts: Part[] = [];
  const followUps = followUpsOf(workflow);
  const driven = checkDrivenIds(workflow).has(index);
  const replaceAt = (step: number, action: AutomationAction): Automation => ({ ...automation, actions: automation.actions.map((existing, i) => (i === step ? action : existing)) });

  automation.actions.forEach((action, step) => {
    const errorPath = `${path}.actions[${step}]`;
    const name = `Action ${step + 1}`;
    const remove = { label: 'Remove this step', apply: () => ({ ...automation, actions: automation.actions.filter((_, i) => i !== step) }) };
    parts.push(text(step === 0 ? ',' : 'and', step === 0));
    parts.push(
      blank({
        kind: 'choice',
        id: `action-${step}-kind`,
        name,
        text: actionKindWords[action.type] ?? action.type,
        errorPath,
        focusPath: errorPath,
        choices: thenChoices(workflow, index, step),
        selected: action.type,
        note: driven ? checkGuardrailLine : undefined,
        remove,
        apply: (id) => replaceAt(step, startAction(workflow, index, step, id as ActionType)),
      }),
    );

    switch (action.type) {
      case 'SendEmail': {
        const choices = ctx.emailTemplates.map((template) => ({ id: template.id, label: template.name }));
        // An email the list does not hold is a built-in one (named until the list loads) or one that was deleted.
        if (action.emailTemplate && !choices.some((choice) => choice.id === action.emailTemplate)) {
          choices.push({ id: action.emailTemplate, label: builtInEmailTemplates.find((template) => template.id === action.emailTemplate)?.name ?? 'A deleted email' });
        }
        parts.push(
          blank({
            kind: 'choice',
            id: `action-${step}-arg`,
            name: `${name}: email`,
            text: choices.find((choice) => choice.id === action.emailTemplate)?.label ?? 'choose an email',
            errorPath,
            choices,
            selected: action.emailTemplate ?? null,
            apply: (id) => replaceAt(step, { type: 'SendEmail', emailTemplate: id }),
          }),
        );
        break;
      }
      case 'SendQuestionnaire': {
        const choices = followUps.map((followUp) => ({ id: followUp.id, label: followUp.name || followUp.id }));
        if (action.questionnaireId && !choices.some((choice) => choice.id === action.questionnaireId)) choices.push({ id: action.questionnaireId, label: action.questionnaireId });
        parts.push(
          blank({
            kind: 'choice',
            id: `action-${step}-arg`,
            name: `${name}: questionnaire`,
            text: choices.find((choice) => choice.id === action.questionnaireId)?.label ?? 'choose a questionnaire',
            errorPath,
            choices,
            selected: action.questionnaireId ?? null,
            apply: (id) => replaceAt(step, { type: 'SendQuestionnaire', questionnaireId: id }),
          }),
        );
        break;
      }
      case 'MoveToStage': {
        const targets = moveCandidates(workflow, index, step).map((entry) => entry.stageId!);
        const choices = targets.map((id) => ({ id, label: stageName(workflow, id) }));
        if (action.stageId && !choices.some((choice) => choice.id === action.stageId)) choices.push({ id: action.stageId, label: stageName(workflow, action.stageId) });
        const hidden = workflow.stages.some((stage) => stage.kind !== 'Rejected' && stage.kind !== 'Withdrawn' && !choices.some((choice) => choice.id === stage.id));
        parts.push(
          blank({
            kind: 'choice',
            id: `action-${step}-arg`,
            name: `${name}: stage`,
            text: action.stageId ? stageName(workflow, action.stageId) : 'choose a stage',
            errorPath,
            choices,
            selected: action.stageId ?? null,
            note: hidden && driven ? stagesRefusedLine : undefined,
            apply: (id) => replaceAt(step, { type: 'MoveToStage', stageId: id }),
          }),
        );
        break;
      }
      case 'AssignReviewer': {
        const choices: Choice[] = [{ id: '', label: 'The team' }, ...ctx.reviewers.map((member) => ({ id: member.userId, label: member.displayName }))];
        if (action.assigneeUserId && !choices.some((choice) => choice.id === action.assigneeUserId)) {
          choices.push({ id: action.assigneeUserId, label: ctx.teamKnown ? 'A former teammate' : 'A teammate' });
        }
        parts.push(
          blank({
            kind: 'choice',
            id: `action-${step}-arg`,
            name: `${name}: who reviews`,
            text: action.assigneeUserId
              ? (ctx.reviewers.find((member) => member.userId === action.assigneeUserId)?.displayName ?? (ctx.teamKnown ? 'a former teammate' : 'a teammate'))
              : 'the team',
            errorPath,
            choices,
            selected: action.assigneeUserId ?? '',
            apply: (id) => replaceAt(step, id ? { type: 'AssignReviewer', assigneeUserId: id } : { type: 'AssignReviewer' }),
          }),
          text('to review'),
        );
        break;
      }
      default: {
        const field = textFieldFor(action.type);
        if (field) {
          const hint = textFieldHints[field]!;
          const value = action[field] ?? '';
          parts.push(
            blank({
              kind: 'text',
              id: `action-${step}-arg`,
              name: `${name}: ${field === 'tag' ? 'tag name' : field}`,
              text: value,
              placeholder: hint.placeholder,
              errorPath,
              value,
              maxLength: hint.maxLength,
              apply: (next) => replaceAt(step, { ...action, [field]: next }),
            }),
          );
        }
      }
    }
  });

  const choices = thenChoices(workflow, index, null);
  if (choices.length > 0) {
    parts.push(
      blank({
        kind: 'choice',
        id: 'add-action',
        name: 'Add another step',
        text: 'then also',
        errorPath: `${path}.actions`,
        adds: true,
        choices,
        selected: null,
        note: driven ? checkGuardrailLine : undefined,
        apply: (id) => withAction(automation, null, startAction(workflow, index, null, id as ActionType)),
      }),
    );
  }
  return parts;
}

/** The one text field an action kind takes (Reject and the review queue also take a reason). */
export function textFieldFor(type: ActionType): 'tag' | 'note' | 'url' | 'reason' | null {
  switch (type) {
    case 'AddTag':
      return 'tag';
    case 'NotifyTeam':
    case 'CreateTask':
      return 'note';
    case 'CallWebhook':
      return 'url';
    case 'QueueForRejectionReview':
    case 'Reject':
      return 'reason';
    default:
      return null;
  }
}

// ---- The sentence --------------------------------------------------------------------------------------------------

/** The automation at `index` as parts: text and blanks in reading order, ending in the plus blanks that add a part. */
export function sentenceOf(workflow: HiringWorkflow, index: number, ctx: SentenceContext): Part[] {
  const automation = workflow.automations[index];
  if (!automation) return [];
  return [...triggerParts(automation, index, workflow), ...conditionParts(automation, index, workflow, ctx), ...actionParts(automation, index, workflow, ctx), text('.', true)];
}

/** Every blank of a sentence, in reading order. */
export const blanksOf = (parts: readonly Part[]): Blank[] => parts.flatMap((part) => (part.type === 'blank' ? [part.blank] : []));

/** The words of a sentence, for a test or a screen reader: text and each blank's button text, joined. */
export function readSentence(parts: readonly Part[]): string {
  return parts
    .map((part) => (part.type === 'text' ? part.text : part.blank.adds ? '' : part.blank.text || part.blank.placeholder || ''))
    .filter(Boolean)
    .join(' ')
    .replace(/ ([,.])/g, '$1');
}
