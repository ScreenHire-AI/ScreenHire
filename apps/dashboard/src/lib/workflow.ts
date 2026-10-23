// The workflow builder's view of a hiring workflow (docs/architecture/workflow-engine.md §4): words for each kind,
// what the builder offers, and immutable edits. The API's DefinitionValidator stays the judge of what is valid; this
// module only keeps the builder from offering what v1 refuses outright, and mirrors the facts schema (FactSchema.cs)
// so conditions offer the operators a fact allows.
import type {
  ActionType,
  AiUsePolicy,
  Automation,
  AutomationAction,
  AutomationCondition,
  ConditionOperator,
  DefinitionError,
  HiringWorkflow,
  Question,
  QuestionType,
  StageKind,
  TeamMember,
  TriggerType,
  WorkflowStage,
} from '@screenhire/api-client';

export const stageKindLabels: Record<StageKind, string> = {
  Applied: 'Applied',
  Screening: 'Screening',
  Assessment: 'Assessment',
  Verification: 'Verification',
  Interview: 'Interview',
  Offer: 'Offer',
  Hired: 'Hired',
  Rejected: 'Rejected',
  Withdrawn: 'Withdrawn',
  Custom: 'Custom',
};

export const stageKinds = Object.keys(stageKindLabels) as StageKind[];

export const isTerminal = (kind: StageKind): boolean => kind === 'Hired' || kind === 'Rejected' || kind === 'Withdrawn';

/** Triggers v1 runs. VerificationCompleted, CandidateReplied and InterviewScheduled are refused at save, so not offered. */
export const triggerLabels: Partial<Record<TriggerType, string>> = {
  ApplicationSubmitted: 'An application arrives',
  StageEntered: 'A candidate enters a stage',
  StageExited: 'A candidate leaves a stage',
  TimeElapsed: 'Time passes in a stage',
  QuestionnaireCompleted: 'The questionnaire is completed',
  IntegrityAssessed: 'The integrity checks finish',
  ReviewerDecision: 'A reviewer records a decision',
  Manual: 'Someone runs it by hand',
};

export const triggerTypes = Object.keys(triggerLabels) as TriggerType[];

export const triggerNeedsStage = (type: TriggerType): boolean => type === 'StageEntered' || type === 'StageExited' || type === 'TimeElapsed';

/** Actions v1 runs. SendQuestionnaire, RequestVerification and ScheduleInterview are refused at save, so not offered. */
export const actionLabels: Partial<Record<ActionType, string>> = {
  SendEmail: 'Send an email',
  MoveToStage: 'Move to a stage',
  AddTag: 'Add a tag',
  AssignReviewer: 'Assign a reviewer',
  NotifyTeam: 'Notify the team',
  CreateTask: 'Create a task',
  CallWebhook: 'Call a webhook',
  QueueForRejectionReview: 'Queue for rejection review',
  Reject: 'Reject',
};

export const actionTypes = Object.keys(actionLabels) as ActionType[];

/** The one field each action needs, as DefinitionValidator requires it. Reject may also name an email template. */
export type ActionField = 'emailTemplate' | 'stageId' | 'tag' | 'note' | 'url' | 'reason';

export const actionField: Partial<Record<ActionType, ActionField>> = {
  SendEmail: 'emailTemplate',
  MoveToStage: 'stageId',
  AddTag: 'tag',
  NotifyTeam: 'note',
  CreateTask: 'note',
  CallWebhook: 'url',
  QueueForRejectionReview: 'reason',
  Reject: 'reason',
};

export const actionFieldLabels: Record<ActionField, string> = {
  emailTemplate: 'Email',
  stageId: 'Stage',
  tag: 'Tag',
  note: 'Note',
  url: 'URL (https)',
  reason: 'Reason',
};

/** The emails an automation may send (EmailTemplateIds.cs). The rejection email is sent only by Reject. */
export const emailTemplates = [
  { id: 'application-received', label: 'Application received' },
  { id: 'stage-change', label: 'Moved to the next step' },
];

/** A fresh action of `type`, carrying only that type's field: the validator refuses fields a type does not take. */
export const newAction = (type: ActionType, stages: readonly WorkflowStage[] = []): AutomationAction => {
  const field = actionField[type];
  if (!field) return { type };
  if (field === 'emailTemplate') return { type, emailTemplate: emailTemplates[0]!.id };
  if (field === 'stageId') return { type, stageId: stages.find((stage) => !isTerminal(stage.kind))?.id ?? stages[0]?.id ?? '' };
  return { type, [field]: '' };
};

/** The AI-use policy as candidates are told it before they start. */
export const aiUseLabels: Record<AiUsePolicy, string> = {
  Allowed: 'Allowed: candidates may use AI tools and need not say so',
  AllowedWithDisclosure: 'Allowed with disclosure: candidates say if they used AI tools',
  NotAllowed: 'Not allowed: candidates are asked to write without AI tools',
};

export const questionTypeLabels: Record<QuestionType, string> = {
  ShortText: 'Short answer',
  LongText: 'Long answer',
  YesNo: 'Yes or no',
  SingleChoice: 'One choice',
  MultiChoice: 'Several choices',
  Number: 'Number',
  Url: 'Link',
  File: 'File upload',
};

export const questionTypes = Object.keys(questionTypeLabels) as QuestionType[];

export const operatorLabels: Record<ConditionOperator, string> = {
  EqualTo: 'is',
  In: 'is one of',
  Contains: 'contains',
  GreaterThan: 'is more than',
  GreaterThanOrEqual: 'is at least',
  LessThan: 'is less than',
  LessThanOrEqual: 'is at most',
  Exists: 'is given',
};

export type FactType = 'Text' | 'TextList' | 'Number' | 'YesNo' | 'Choice' | 'MultiChoice' | 'File' | 'Level';

/** One fact a condition can test, with the values it may take when they are a fixed list. */
export interface FactOption {
  fact: string;
  label: string;
  type: FactType;
  /** An integrity or verification signal: it may only route, never reject (ADR-0004). */
  signal: boolean;
  values?: { value: string; label: string }[];
}

const operatorsByType: Record<FactType, ConditionOperator[]> = {
  Text: ['EqualTo', 'In', 'Contains', 'Exists'],
  TextList: ['Contains', 'In', 'Exists'],
  MultiChoice: ['Contains', 'In', 'Exists'],
  Number: ['EqualTo', 'GreaterThan', 'GreaterThanOrEqual', 'LessThan', 'LessThanOrEqual', 'Exists'],
  YesNo: ['EqualTo', 'Exists'],
  Choice: ['EqualTo', 'In', 'Exists'],
  Level: ['EqualTo', 'In', 'Exists'],
  File: ['Exists'],
};

export const operatorsFor = (type: FactType): ConditionOperator[] => operatorsByType[type];

/** The integrity engine's level names (integrity-engine.md); levels compare by equality only. */
export const authenticityLevels = [
  { value: 'NoConcerns', label: 'No concerns' },
  { value: 'ReviewSuggested', label: 'Review suggested' },
  { value: 'VerificationRecommended', label: 'Verification recommended' },
];

export const aiAssistanceLevels = [
  { value: 'NotIndicated', label: 'Not indicated' },
  { value: 'SomeIndicators', label: 'Some indicators' },
  { value: 'StrongIndicators', label: 'Strong indicators' },
];

const answerType: Record<QuestionType, FactType> = {
  ShortText: 'Text',
  LongText: 'Text',
  Url: 'Text',
  SingleChoice: 'Choice',
  MultiChoice: 'MultiChoice',
  YesNo: 'YesNo',
  Number: 'Number',
  File: 'File',
};

const yesNo = [
  { value: 'yes', label: 'Yes' },
  { value: 'no', label: 'No' },
];

/** Every fact a condition in this workflow can test: one per question, then the application's and the signals. */
export function factsFor(workflow: HiringWorkflow): FactOption[] {
  const answers = workflow.questionnaire.questions.map(
    (question): FactOption => ({
      fact: `answers.${question.id}`,
      label: `Answer: ${question.label || question.id}`,
      type: answerType[question.type],
      signal: false,
      values:
        question.type === 'YesNo'
          ? yesNo
          : question.type === 'SingleChoice' || question.type === 'MultiChoice'
            ? (question.options ?? []).map((option) => ({ value: option.id, label: option.label }))
            : undefined,
    }),
  );
  return [
    ...answers,
    { fact: 'application.tags', label: 'Tags', type: 'TextList', signal: false },
    { fact: 'application.source', label: 'Source', type: 'Text', signal: false },
    { fact: 'application.hoursInStage', label: 'Hours in stage', type: 'Number', signal: false },
    { fact: 'integrity.authenticity', label: 'Integrity: authenticity', type: 'Level', signal: true, values: authenticityLevels },
    { fact: 'integrity.aiAssistance', label: 'Integrity: AI assistance', type: 'Level', signal: true, values: aiAssistanceLevels },
  ];
}

/** A fresh rule on `fact`, with the first operator it allows and its first value. */
export function newRule(fact: FactOption): AutomationCondition {
  const operator = operatorsFor(fact.type)[0]!;
  return { fact: fact.fact, operator, ...ruleOperand(operator, fact) };
}

/** The operand members `operator` takes: none, `value` or `values`. The validator refuses the others. */
export function ruleOperand(operator: ConditionOperator, fact: FactOption | undefined): Pick<AutomationCondition, 'value' | 'values'> {
  if (operator === 'Exists') return {};
  const first = fact?.values?.[0]?.value ?? '';
  return operator === 'In' ? { values: first ? [first] : [] } : { value: first };
}

/** A stable id from a label: lowercase letters, digits and underscores, unique among `taken`. */
export function idFrom(label: string, taken: Iterable<string>, fallback: string): string {
  const base =
    label
      .normalize('NFKD')
      .replace(/[̀-ͯ]/g, '')
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '_')
      .replace(/^_+|_+$/g, '')
      .slice(0, 40) || fallback;
  const used = new Set(taken);
  if (!used.has(base)) return base;
  for (let n = 2; ; n++) if (!used.has(`${base}_${n}`)) return `${base}_${n}`;
}

export function newStage(workflow: HiringWorkflow): WorkflowStage {
  return { id: idFrom('stage', workflow.stages.map((stage) => stage.id), 'stage'), name: 'New stage', kind: 'Custom' };
}

export function newQuestion(workflow: HiringWorkflow, type: QuestionType = 'ShortText'): Question {
  const id = idFrom('question', workflow.questionnaire.questions.map((question) => question.id), 'question');
  return withQuestionType({ id, type: 'ShortText', label: 'New question', required: false }, type);
}

/** The question as `type`, keeping what carries over and dropping members the type does not take. */
export function withQuestionType(question: Question, type: QuestionType): Question {
  const next: Question = { id: question.id, type, label: question.label, required: question.required ?? false, helpText: question.helpText ?? null };
  if (type === 'SingleChoice' || type === 'MultiChoice') {
    next.options = question.options?.length
      ? question.options
      : [
          { id: 'option_1', label: 'Option 1' },
          { id: 'option_2', label: 'Option 2' },
        ];
  }
  if (type === 'YesNo' && question.type === 'YesNo') next.knockout = question.knockout ?? null;
  if (type === 'File') next.acceptedFileTypes = question.acceptedFileTypes?.length ? question.acceptedFileTypes : ['.pdf', '.docx'];
  return next;
}

/** A new automation for a lane: a stage's entry trigger, or "an application arrives" for the whole workflow. */
export function newAutomation(workflow: HiringWorkflow, stageId: string | null): Automation {
  const id = idFrom('automation', workflow.automations.map((automation) => automation.id), 'automation');
  return {
    id,
    name: 'New automation',
    trigger: stageId ? { type: 'StageEntered', stageId } : { type: 'ApplicationSubmitted' },
    conditions: null,
    actions: [newAction('AddTag')],
    enabled: true,
    allowAutoReject: false,
  };
}

/** The stage an automation belongs to in the pipeline view, or null for the whole workflow. */
export const laneOf = (automation: Automation): string | null =>
  triggerNeedsStage(automation.trigger.type) ? (automation.trigger.stageId ?? null) : null;

/** `list` with the item at `index` moved by `delta`, or unchanged at either end. The non-drag way to reorder. */
export function moveItem<T>(list: readonly T[], index: number, delta: -1 | 1): T[] {
  const target = index + delta;
  if (target < 0 || target >= list.length) return [...list];
  const next = [...list];
  [next[index], next[target]] = [next[target]!, next[index]!];
  return next;
}

/** `list` with `item` at `index`. */
export const replaceAt = <T>(list: readonly T[], index: number, item: T): T[] => list.map((existing, i) => (i === index ? item : existing));

/** `list` without the item at `index`. */
export const removeAt = <T>(list: readonly T[], index: number): T[] => list.filter((_, i) => i !== index);

/**
 * A validator path as the builder uses it: relative to the workflow. Save responses prefix paths with the request
 * member (`definition.workflow.`, `workflow.`); the validate endpoint does not.
 */
export const workflowPath = (path: string): string => path.replace(/^(definition\.)?workflow\.?/, '');

/** The errors at `path` or inside it (`automations[0]` covers `automations[0].actions[1].reason`). */
export const errorsUnder = (errors: readonly DefinitionError[], path: string): DefinitionError[] =>
  errors.filter((error) => error.path === path || error.path.startsWith(`${path}.`) || error.path.startsWith(`${path}[`));

/** The messages exactly at `path`. */
export const errorsAt = (errors: readonly DefinitionError[], path: string): string[] =>
  errors.filter((error) => error.path === path).map((error) => error.message);

/** Validation errors from a save's 400 problem details (`errors: { path: [messages] }`), relative to the workflow. */
export function problemErrors(body: unknown): DefinitionError[] {
  const errors = body && typeof body === 'object' && 'errors' in body ? (body as { errors: unknown }).errors : null;
  if (!errors || typeof errors !== 'object') return [];
  return Object.entries(errors as Record<string, unknown>).flatMap(([path, messages]) =>
    (Array.isArray(messages) ? messages : []).map((message) => ({ path: workflowPath(path), message: String(message) })),
  );
}

/** Words for where an error is, from its path: "Automation "Tag on arrival", action 1". */
export function describePath(workflow: HiringWorkflow, path: string): string {
  const automation = /^automations\[(\d+)\](?:\.actions\[(\d+)\])?(?:\.conditions\.rules\[(\d+)\])?/.exec(path);
  if (automation) {
    const name = workflow.automations[Number(automation[1])]?.name ?? `Automation ${Number(automation[1]) + 1}`;
    const part = automation[2] !== undefined ? `, action ${Number(automation[2]) + 1}` : automation[3] !== undefined ? `, condition ${Number(automation[3]) + 1}` : '';
    return `Automation "${name}"${part}`;
  }
  const question = /^questionnaire\.questions\[(\d+)\]/.exec(path);
  if (question) return `Question "${workflow.questionnaire.questions[Number(question[1])]?.label ?? Number(question[1]) + 1}"`;
  const stage = /^stages\[(\d+)\]/.exec(path);
  if (stage) return `Stage "${workflow.stages[Number(stage[1])]?.name ?? Number(stage[1]) + 1}"`;
  if (path.startsWith('stages')) return 'Stages';
  if (path.startsWith('questionnaire')) return 'Questionnaire';
  return 'Workflow';
}

/** A team member as an AssignReviewer action names them. */
type Reviewer = Pick<TeamMember, 'userId' | 'displayName'>;

/**
 * One sentence for an automation, as the pipeline view lists it: "When a candidate enters Screening: add a tag."
 * `members` names an AssignReviewer action's reviewer.
 */
export function summarize(automation: Automation, workflow: HiringWorkflow, members?: readonly Reviewer[]): string {
  const stage = (id: string | null | undefined) => workflow.stages.find((candidate) => candidate.id === id)?.name ?? id ?? '?';
  const { trigger } = automation;
  const when =
    trigger.type === 'StageEntered'
      ? `When a candidate enters ${stage(trigger.stageId)}`
      : trigger.type === 'StageExited'
        ? `When a candidate leaves ${stage(trigger.stageId)}`
        : trigger.type === 'TimeElapsed'
          ? `${trigger.afterHours ?? '?'} hours after entering ${stage(trigger.stageId)}`
          : (triggerLabels[trigger.type] ?? trigger.type).replace(/^./, (first) => `When ${first.toLowerCase()}`);
  const rules = automation.conditions?.rules.length ?? 0;
  const condition = rules > 0 ? `, if ${rules} ${rules === 1 ? 'condition holds' : automation.conditions?.match === 'Any' ? 'conditions: any holds' : 'conditions hold'}` : '';
  // Only the first letter drops to lower case, so stage, template and people's names keep theirs.
  const actions = automation.actions.map((action) => describeAction(action, workflow, members).replace(/^./, (first) => first.toLowerCase())).join(', then ');
  return `${when}${condition}: ${actions}.`;
}

export function describeAction(action: AutomationAction, workflow: HiringWorkflow, members?: readonly Reviewer[]): string {
  const label = actionLabels[action.type] ?? action.type;
  if (action.type === 'AssignReviewer') {
    if (!action.assigneeUserId) return 'Ask the team to review';
    return `Ask ${members?.find((member) => member.userId === action.assigneeUserId)?.displayName ?? 'a teammate'} to review`;
  }
  if (action.type === 'MoveToStage') return `Move to ${workflow.stages.find((stage) => stage.id === action.stageId)?.name ?? action.stageId}`;
  if (action.type === 'AddTag') return `Add the tag "${action.tag ?? ''}"`;
  if (action.type === 'SendEmail') return `Send the "${emailTemplates.find((template) => template.id === action.emailTemplate)?.label ?? action.emailTemplate}" email`;
  return label;
}
