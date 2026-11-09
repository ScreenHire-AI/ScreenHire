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
  CriterionRating,
  DefinitionError,
  FollowUpQuestionnaire,
  HiringWorkflow,
  Question,
  QuestionType,
  Recommendation,
  ScorecardQuestionType,
  StageKind,
  TeamMember,
  TriggerType,
  WorkflowStage,
} from '@screenhire/api-client';
import { zoneName } from '#lib/report-range.js';

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

/** Triggers v1 runs. VerificationCompleted and CandidateReplied are refused at save, so not offered. */
export const triggerLabels: Partial<Record<TriggerType, string>> = {
  ApplicationSubmitted: 'An application arrives',
  StageEntered: 'A candidate enters a stage',
  StageExited: 'A candidate leaves a stage',
  TimeElapsed: 'Time passes in a stage',
  QuestionnaireCompleted: 'A follow-up questionnaire is answered',
  VerificationAssessed: 'The verification checks finish',
  ReviewerDecision: 'A reviewer records a decision',
  SlaBreached: "A stage's time limit passes",
  InterviewScheduled: 'An interview is booked',
  Manual: 'Someone runs it by hand',
};

export const triggerTypes = Object.keys(triggerLabels) as TriggerType[];

export const triggerNeedsStage = (type: TriggerType): boolean => type === 'StageEntered' || type === 'StageExited' || type === 'TimeElapsed';

/**
 * A reviewer decision may name the stage whose scorecards it fires on, and a passed time limit the stage whose limit it
 * watches; without one, either fires on any stage's.
 */
export const triggerMayHaveStage = (type: TriggerType): boolean => type === 'ReviewerDecision' || type === 'SlaBreached';

/** Whether a stage's time limit counts: it has one, and the stage does not end the application (WorkflowStage.HasSla). */
export const hasTimeLimit = (stage: WorkflowStage): boolean => Boolean(stage.slaDays) && !isTerminal(stage.kind);

/**
 * What a passed time limit may do (DefinitionValidator's SlaBreachActions). It measures the team's delay, never the
 * candidate, so it may only remind the team.
 */
export const timeLimitActions: readonly ActionType[] = ['NotifyTeam', 'AssignReviewer', 'CreateTask', 'AddTag'];

/**
 * The actions the builder offers for an automation on `trigger`: all of `actionTypes`, or only the reminders for a
 * passed time limit. A saved action outside that list stays offered as `current`, so the form shows what is saved and
 * the validator's message beside it.
 */
export function actionsFor(trigger: TriggerType, current?: ActionType): ActionType[] {
  const offered = trigger === 'SlaBreached' ? actionTypes.filter((type) => timeLimitActions.includes(type)) : [...actionTypes];
  return current && !offered.includes(current) ? [...offered, current] : offered;
}

/**
 * A completed questionnaire names the follow-up questionnaire that fires it. Without one it fires on any follow-up,
 * which is how workflows saved before follow-ups existed still read; the apply form never fires it.
 */
export const triggerMayHaveQuestionnaire = (type: TriggerType): boolean => type === 'QuestionnaireCompleted';

/** The workflow's follow-up questionnaires, an empty list for a workflow saved before they existed. */
export const followUpsOf = (workflow: HiringWorkflow): FollowUpQuestionnaire[] => workflow.followUpQuestionnaires ?? [];

/** Every question of the workflow: the apply form's, then each follow-up's. Ids are unique across all of them. */
export const allQuestions = (workflow: HiringWorkflow): Question[] => [
  ...workflow.questionnaire.questions,
  ...followUpsOf(workflow).flatMap((followUp) => followUp.questions),
];

/** A follow-up questionnaire's name, or its id when it has none yet. */
export const followUpName = (workflow: HiringWorkflow, id: string | null | undefined): string =>
  followUpsOf(workflow).find((followUp) => followUp.id === id)?.name || id || '?';

/**
 * The labelled scale of v0.2 scorecards (CriterionRating), kept so the scorecards submitted then read as they were.
 * Those labels were never numbers, so they never count toward the candidate's rating.
 */
export type Rating = NonNullable<CriterionRating>;

export const ratingLabels: Record<Rating, string> = {
  NotDemonstrated: 'Not demonstrated',
  PartlyDemonstrated: 'Partly demonstrated',
  Demonstrated: 'Demonstrated',
  StronglyDemonstrated: 'Strongly demonstrated',
};

/** A reviewer's overall recommendation; automations test it as `reviewer.recommendation`. */
export const recommendationLabels: Record<NonNullable<Recommendation>, string> = {
  StrongNo: 'Strong no',
  No: 'No',
  Yes: 'Yes',
  StrongYes: 'Strong yes',
};

export const recommendations = Object.keys(recommendationLabels) as NonNullable<Recommendation>[];

/** Actions v1 runs. RequestVerification is refused at save, so not offered. */
export const actionLabels: Partial<Record<ActionType, string>> = {
  SendEmail: 'Send an email',
  SendQuestionnaire: 'Send a questionnaire',
  MoveToStage: 'Move to a stage',
  AddTag: 'Add a tag',
  AssignReviewer: 'Assign a reviewer',
  NotifyTeam: 'Notify the team',
  CreateTask: 'Create a task',
  CallWebhook: 'Call a webhook',
  ScheduleInterview: 'Schedule an interview',
  QueueForRejectionReview: 'Queue for rejection review',
  Reject: 'Reject',
};

export const actionTypes = Object.keys(actionLabels) as ActionType[];

/**
 * The one field each action needs, as DefinitionValidator requires it. Reject may also name an email template.
 * ScheduleInterview and AssignReviewer are not here: the first takes four fields (`interviewFieldLabels`), the second an
 * optional reviewer, and the form draws both itself.
 */
export type ActionField = 'emailTemplate' | 'stageId' | 'tag' | 'note' | 'url' | 'reason' | 'questionnaireId';

export const actionField: Partial<Record<ActionType, ActionField>> = {
  SendEmail: 'emailTemplate',
  SendQuestionnaire: 'questionnaireId',
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
  tag: 'Tag name',
  note: 'Note',
  url: 'URL (https)',
  reason: 'Reason',
  questionnaireId: 'Questionnaire',
};

/** The four fields of a ScheduleInterview action, as the form words them. */
export const interviewFieldLabels = {
  interviewerUserIds: 'Interviewers',
  interviewMinutes: 'Length',
  interviewWindowDays: 'Days to choose',
  interviewTimeZone: 'Time zone',
} as const;

/** The longest window an interview action gives the candidate, in days: DefinitionValidator's limit. */
export const maxInterviewWindowDays = 60;

/**
 * The built-in emails a SendEmail automation may send (EmailTemplateIds.cs), with their names until the organization's
 * list loads (`GET /v1/email-templates`), which adds its own templates. The rejection email is sent only by Reject,
 * and the follow-up questions email only by SendQuestionnaire.
 */
export const builtInEmailTemplates = [
  { id: 'application-received', name: 'Application received' },
  { id: 'stage-change', name: 'Moved to the next step' },
];

/** Template names by id, for `describeAction`: the organization's list, or the built-in names until it loads. */
export function emailTemplateNames(templates: readonly { id: string; name: string }[] | undefined): ReadonlyMap<string, string> {
  return new Map([...builtInEmailTemplates, ...(templates ?? [])].map((template) => [template.id, template.name]));
}

/**
 * A fresh action of `type`, carrying only that type's field: the validator refuses fields a type does not take.
 * SendQuestionnaire starts with the first follow-up questionnaire in `followUps`.
 */
export const newAction = (
  type: ActionType,
  stages: readonly WorkflowStage[] = [],
  followUps: readonly FollowUpQuestionnaire[] = [],
): AutomationAction => {
  // The organization has no time zone of its own, so a new interview action starts with the editor's.
  if (type === 'ScheduleInterview') return { type, interviewerUserIds: [], interviewMinutes: 30, interviewWindowDays: 14, interviewTimeZone: zoneName() };
  const field = actionField[type];
  if (!field) return { type };
  if (field === 'emailTemplate') return { type, emailTemplate: builtInEmailTemplates[0]!.id };
  if (field === 'stageId') return { type, stageId: stages.find((stage) => !isTerminal(stage.kind))?.id ?? stages[0]?.id ?? '' };
  if (field === 'questionnaireId') return { type, questionnaireId: followUps[0]?.id ?? '' };
  return { type, [field]: '' };
};

/** The AI-use policy as candidates are told it before they start. */
export const aiUseLabels: Record<AiUsePolicy, string> = {
  Allowed: 'Allowed: candidates may use AI tools and need not say so',
  AllowedWithDisclosure: 'Allowed with disclosure: candidates say if they used AI tools',
  NotAllowed: 'Not allowed: candidates are asked to write without AI tools',
};

/** The policy's name alone: "Allowed with disclosure". */
export const aiUseName = (policy: AiUsePolicy): string => aiUseLabels[policy].split(': ')[0]!;

export const questionTypeLabels: Record<QuestionType, string> = {
  ShortText: 'Short text',
  LongText: 'Long text',
  YesNo: 'Yes or no',
  SingleChoice: 'Choice',
  MultiChoice: 'Multi-choice',
  Number: 'Number',
  Url: 'Link',
  File: 'File',
};

/** A scorecard question's answer kind as the answer menu names it; the Rating kinds sit under its Rating heading. */
export const scorecardQuestionTypeLabels: Record<ScorecardQuestionType, string> = {
  YesNo: 'Yes or no',
  SingleChoice: 'Choice',
  MultiChoice: 'Multi-choice',
  ShortText: 'Short text',
  LongText: 'Long text',
  Rating5: '1 to 5',
  Rating10: '1 to 10',
  RatingYesNo: 'Yes or no',
};

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

export type FactType = 'Text' | 'TextList' | 'Number' | 'YesNo' | 'Choice' | 'MultiChoice' | 'File' | 'Level' | 'Recommendation';

/** One fact a condition can test, with the values it may take when they are a fixed list. */
export interface FactOption {
  fact: string;
  label: string;
  type: FactType;
  /** A verification signal: it may only route, never reject (ADR-0004). */
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
  Recommendation: ['EqualTo', 'In', 'Exists'],
  File: ['Exists'],
};

export const operatorsFor = (type: FactType): ConditionOperator[] => operatorsByType[type];

/**
 * The verification engine's level names (verification-engine.md), in the one-word states the dashboard uses (redesign
 * 2026-11, approved rule 1): Clear, Review, Verify. The stored values do not change. Levels compare by equality only.
 */
export const authenticityLevels = [
  { value: 'NoConcerns', label: 'Clear' },
  { value: 'ReviewSuggested', label: 'Review' },
  { value: 'VerificationRecommended', label: 'Verify' },
];

export const aiAssistanceLevels = [
  { value: 'NotIndicated', label: 'No signs of AI help' },
  { value: 'SomeIndicators', label: 'Some signs of AI help' },
  { value: 'StrongIndicators', label: 'Strong signs of AI help' },
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

/** Where an application came from (ApplicationSource), as `application.source` holds it. */
export const sources = [
  { value: 'CareersSite', label: 'Careers site' },
  { value: 'Recruiter', label: 'Added by a recruiter' },
];

/**
 * Every fact a condition in this workflow can test: one per question of the apply form and of each follow-up
 * questionnaire (a follow-up's answers hold only for triggers after it was answered), then the application's and the
 * signals, and for a reviewer-decision automation the reviewer's recommendation, which only that trigger has.
 */
export function factsFor(workflow: HiringWorkflow, trigger?: TriggerType): FactOption[] {
  const questions = [
    ...workflow.questionnaire.questions.map((question) => ({ question, from: null as string | null })),
    ...followUpsOf(workflow).flatMap((followUp) => followUp.questions.map((question) => ({ question, from: followUp.name || followUp.id }))),
  ];
  const answers = questions.map(
    ({ question, from }): FactOption => ({
      fact: `answers.${question.id}`,
      label: from ? `Answer (${from}): ${question.label || question.id}` : `Answer: ${question.label || question.id}`,
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
    { fact: 'application.source', label: 'Source', type: 'Text', signal: false, values: sources },
    { fact: 'application.hoursInStage', label: 'Hours in stage', type: 'Number', signal: false },
    { fact: 'verification.authenticity', label: 'Verification', type: 'Level', signal: true, values: authenticityLevels },
    { fact: 'verification.aiAssistance', label: 'Verification: AI assistance', type: 'Level', signal: true, values: aiAssistanceLevels },
    ...(trigger === 'ReviewerDecision'
      ? [
          {
            fact: 'reviewer.recommendation',
            label: "Reviewer's recommendation",
            type: 'Recommendation' as const,
            signal: false,
            values: recommendations.map((value) => ({ value, label: recommendationLabels[value] })),
          },
        ]
      : []),
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

/**
 * The kind a stage gets from its name, so a person who types "Trial shift" never meets the word "Assessment". The first
 * match wins, and a name that says nothing is Custom. Only a stage's creation and its name, until its kind is set by
 * hand under More options, use it. Applied is never guessed: the first stage is Applied and no other stage can be.
 */
export function kindFromName(name: string): StageKind {
  const text = name.toLowerCase();
  const has = (...words: string[]) => words.some((word) => text.includes(word));
  if (has('withdraw')) return 'Withdrawn';
  if (has('reject', 'not selected', 'declined', 'not hired', 'turned down')) return 'Rejected';
  if (has('hired', 'started', 'onboard')) return 'Hired';
  if (has('offer')) return 'Offer';
  if (has('interview')) return 'Interview';
  if (has('verif', 'background', 'reference')) return 'Verification';
  if (has('trial', 'test', 'task', 'assessment', 'skills', 'shift', 'group session', 'exercise')) return 'Assessment';
  if (has('phone', 'call', 'screen', 'review')) return 'Screening';
  return 'Custom';
}

export const newStageName = 'New stage';

/** A new stage named `name`, its kind set from the name (`kindFromName`). */
export function newStage(workflow: HiringWorkflow, name: string = newStageName): WorkflowStage {
  return {
    id: idFrom('stage', workflow.stages.map((stage) => stage.id), 'stage'),
    name,
    kind: name === newStageName ? 'Custom' : kindFromName(name),
  };
}

/** The stages with `stage` inserted so it sits at `index`; a stage never goes before the first one, which is Applied. */
export function insertStage(stages: readonly WorkflowStage[], index: number, stage: WorkflowStage): WorkflowStage[] {
  const at = Math.max(1, Math.min(index, stages.length));
  return [...stages.slice(0, at), stage, ...stages.slice(at)];
}

/** The workflow without the stage at `index`, and without the reminder the stage sheet kept for it. */
export function withoutStage(workflow: HiringWorkflow, index: number): Pick<HiringWorkflow, 'stages' | 'automations'> {
  const stage = workflow.stages[index];
  return {
    stages: workflow.stages.filter((_, i) => i !== index),
    automations: stage ? workflow.automations.filter((automation) => automation.id !== reminderId(stage.id)) : workflow.automations,
  };
}

/** Where a stage inserted after the one at `index` goes: just after it, but before the stages that end the candidacy. */
export function insertionIndex(stages: readonly WorkflowStage[], index: number): number {
  const stage = stages[index];
  if (!stage || isTerminal(stage.kind)) {
    const firstEnd = stages.findIndex((candidate) => isTerminal(candidate.kind));
    return firstEnd < 0 ? stages.length : firstEnd;
  }
  return index + 1;
}

/** The plain time limits a stage offers; anything else is "Other". */
export const timeLimitChoices: readonly { days: number | null; label: string }[] = [
  { days: null, label: 'None' },
  { days: 2, label: '2 days' },
  { days: 3, label: '3 days' },
  { days: 5, label: '5 days' },
  { days: 7, label: '1 week' },
];

/** "2 days", "1 week", "10 days": a time limit in the words people use. */
export const timeLimitLabel = (days: number): string =>
  timeLimitChoices.find((choice) => choice.days === days)?.label ?? `${days} ${days === 1 ? 'day' : 'days'}`;

/**
 * The id of the reminder the stage sheet keeps for a stage's time limit: an automation that tells the team when someone
 * has waited longer than the limit. One per stage, so the sheet can add, change and remove it without touching the
 * automations a person wrote (docs/architecture/workflow-engine.md section 4).
 */
export const reminderId = (stageId: string): string => `reminder-${stageId}`;

const isSignalFact = (fact: string): boolean => fact.startsWith('verification.');

/**
 * Whether a stage can have a reminder. A stage a verification check moves candidates into only allows the check's own
 * kind of automation, and telling the team is not one of them (the validator's guardrail), so it has no reminder.
 */
export function canRemindIn(workflow: HiringWorkflow, stageId: string): boolean {
  return !workflow.automations.some(
    (automation) =>
      (automation.trigger.type === 'VerificationAssessed' ||
        automation.trigger.type === 'VerificationCompleted' ||
        automation.conditions?.rules.some((rule) => isSignalFact(rule.fact))) &&
      automation.actions.some((action) => action.type === 'MoveToStage' && action.stageId === stageId),
  );
}

/** Whether something already tells the team when the stage's time limit passes: the sheet's reminder or a person's automation. */
export function hasReminder(workflow: HiringWorkflow, stageId: string): boolean {
  return workflow.automations.some(
    (automation) =>
      automation.enabled !== false &&
      automation.trigger.type === 'SlaBreached' &&
      (automation.trigger.stageId === stageId || !automation.trigger.stageId) &&
      automation.actions.some((action) => action.type === 'NotifyTeam'),
  );
}

/**
 * The workflow with the stage's reminder in step with its time limit: added when the stage has a limit and nothing
 * already reminds the team, renamed with the stage, and removed (only the sheet's own) when the limit goes.
 */
export function syncReminder(workflow: HiringWorkflow, stageId: string): HiringWorkflow {
  const stage = workflow.stages.find((candidate) => candidate.id === stageId);
  const own = workflow.automations.find((automation) => automation.id === reminderId(stageId));
  if (!stage || !hasTimeLimit(stage) || !canRemindIn(workflow, stageId)) {
    return own ? { ...workflow, automations: workflow.automations.filter((automation) => automation !== own) } : workflow;
  }
  const name = `Remind the team when ${stage.name} waits too long`;
  if (own) {
    return own.name === name
      ? workflow
      : { ...workflow, automations: workflow.automations.map((automation) => (automation === own ? { ...own, name } : automation)) };
  }
  if (hasReminder(workflow, stageId)) return workflow;
  const reminder: Automation = {
    id: reminderId(stageId),
    name,
    trigger: { type: 'SlaBreached', stageId },
    conditions: null,
    actions: [{ type: 'NotifyTeam', note: `Someone has waited in ${stage.name} longer than the time limit.` }],
    enabled: true,
    allowAutoReject: false,
  };
  return { ...workflow, automations: [...workflow.automations, reminder] };
}

/** A new question with an id unique across every questionnaire of the workflow, so `answers.{id}` names one question. */
export function newQuestion(workflow: HiringWorkflow, type: QuestionType = 'ShortText'): Question {
  const id = idFrom('question', allQuestions(workflow).map((question) => question.id), 'question');
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

/** Whether an automation's condition reads the answer to the question `questionId` (the fact `answers.{id}`). */
export const answerIsRead = (workflow: HiringWorkflow, questionId: string): boolean =>
  workflow.automations.some((automation) => automation.conditions?.rules.some((rule) => rule.fact === `answers.${questionId}`));

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
  triggerNeedsStage(automation.trigger.type) || triggerMayHaveStage(automation.trigger.type) ? (automation.trigger.stageId ?? null) : null;

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

/**
 * Words for where an error is, from its path: `The step "Add a tag" in "Tag on arrival"`. The validator's messages name
 * the thing themselves, so this is for the places that need only the location: a Fix button's label, a screen reader's
 * context, and a path no message can name.
 */
export function describePath(workflow: HiringWorkflow, path: string): string {
  const automation = /^automations\[(\d+)\](?:\.actions\[(\d+)\])?(?:\.conditions\.rules\[(\d+)\])?/.exec(path);
  if (automation) {
    const found = workflow.automations[Number(automation[1])];
    const name = found?.name || `automation ${Number(automation[1]) + 1}`;
    if (automation[2] !== undefined) {
      const step = found?.actions[Number(automation[2])];
      const label = step ? (actionLabels[step.type] ?? step.type) : `Step ${Number(automation[2]) + 1}`;
      return `The step "${label}" in "${name}"`;
    }
    if (automation[3] !== undefined) return `Condition ${Number(automation[3]) + 1} in "${name}"`;
    return `The automation "${name}"`;
  }
  const followUp = /^followUpQuestionnaires\[(\d+)\](?:\.questions\[(\d+)\])?/.exec(path);
  if (followUp) {
    const questionnaire = followUpsOf(workflow)[Number(followUp[1])];
    const name = questionnaire?.name || `follow-up ${Number(followUp[1]) + 1}`;
    if (followUp[2] === undefined) return `The form "${name}"`;
    return `The question "${questionnaire?.questions[Number(followUp[2])]?.label ?? Number(followUp[2]) + 1}" in "${name}"`;
  }
  const question = /^questionnaire\.questions\[(\d+)\]/.exec(path);
  if (question) return `The question "${workflow.questionnaire.questions[Number(question[1])]?.label ?? Number(question[1]) + 1}"`;
  const stage = /^stages\[(\d+)\]/.exec(path);
  if (stage) return `The stage "${workflow.stages[Number(stage[1])]?.name ?? Number(stage[1]) + 1}"`;
  if (path.startsWith('stages')) return 'The stages';
  if (path.startsWith('questionnaire')) return 'The questions';
  return 'The workflow';
}

/** A team member as an AssignReviewer action names them. */
type Reviewer = Pick<TeamMember, 'userId' | 'displayName'>;

/**
 * One sentence for an automation, as the pipeline view lists it: "When a candidate enters Screening: add a tag."
 * `members` names an AssignReviewer action's reviewer; `templateNames` names a SendEmail action's template.
 */
export function summarize(
  automation: Automation,
  workflow: HiringWorkflow,
  members?: readonly Reviewer[],
  templateNames: ReadonlyMap<string, string> = emailTemplateNames(undefined),
): string {
  const stage = (id: string | null | undefined) => workflow.stages.find((candidate) => candidate.id === id)?.name ?? id ?? '?';
  const { trigger } = automation;
  const when =
    trigger.type === 'StageEntered'
      ? `When a candidate enters ${stage(trigger.stageId)}`
      : trigger.type === 'StageExited'
        ? `When a candidate leaves ${stage(trigger.stageId)}`
        : trigger.type === 'TimeElapsed'
          ? `${trigger.afterHours ?? '?'} hours after entering ${stage(trigger.stageId)}`
          : trigger.type === 'ReviewerDecision' && trigger.stageId
            ? `When a reviewer records a decision on ${stage(trigger.stageId)}`
          : trigger.type === 'SlaBreached' && trigger.stageId
            ? `When the time limit in ${stage(trigger.stageId)} passes`
          : trigger.type === 'QuestionnaireCompleted' && trigger.questionnaireId
            ? `When the candidate answers "${followUpName(workflow, trigger.questionnaireId)}"`
          : (triggerLabels[trigger.type] ?? trigger.type).replace(/^./, (first) => `When ${first.toLowerCase()}`);
  const rules = automation.conditions?.rules.length ?? 0;
  const condition = rules > 0 ? `, if ${rules} ${rules === 1 ? 'condition holds' : automation.conditions?.match === 'Any' ? 'conditions: any holds' : 'conditions hold'}` : '';
  // Only the first letter drops to lower case, so stage, template and people's names keep theirs.
  const actions = automation.actions
    .map((action) => describeAction(action, workflow, members, templateNames).replace(/^./, (first) => first.toLowerCase()))
    .join(', then ');
  return `${when}${condition}: ${actions}.`;
}

export function describeAction(
  action: AutomationAction,
  workflow: HiringWorkflow,
  members?: readonly Reviewer[],
  templateNames: ReadonlyMap<string, string> = emailTemplateNames(undefined),
): string {
  const label = actionLabels[action.type] ?? action.type;
  if (action.type === 'AssignReviewer') {
    if (!action.assigneeUserId) return 'Ask the team to review';
    return `Ask ${members?.find((member) => member.userId === action.assigneeUserId)?.displayName ?? 'a teammate'} to review`;
  }
  if (action.type === 'ScheduleInterview') {
    const names = (action.interviewerUserIds ?? []).map((id) => members?.find((member) => member.userId === id)?.displayName ?? 'a teammate');
    return names.length > 0 ? `Schedule an interview with ${names.join(', ')}` : label;
  }
  if (action.type === 'MoveToStage') return `Move to ${workflow.stages.find((stage) => stage.id === action.stageId)?.name ?? action.stageId}`;
  // A tag with no name yet reads as unfinished, never as an empty pair of quotes.
  if (action.type === 'AddTag') return action.tag?.trim() ? `Add the tag "${action.tag}"` : 'Add a tag (no name yet)';
  if (action.type === 'SendQuestionnaire') return `Send the "${followUpName(workflow, action.questionnaireId)}" questionnaire`;
  if (action.type === 'SendEmail') {
    const name = action.emailTemplate ? templateNames.get(action.emailTemplate) : undefined;
    return name ? `Send the "${name}" email` : 'Send an email';
  }
  return label;
}
