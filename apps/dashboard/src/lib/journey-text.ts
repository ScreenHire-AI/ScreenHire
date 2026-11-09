// The Overview tab's text (design system section 11, Journey): a workflow read back as what happens to an applicant. Pure
// functions over the workflow, so the words are tested without a page. Generated text never names a person: a reviewer
// is "the team", whoever is saved in the step.
import type { Automation, AutomationAction, AutomationCondition, HiringWorkflow, WorkflowStage } from '@screenhire/api-client';
import { emailTemplateNames, followUpName, followUpsOf, isTerminal, laneOf, reminderId } from '#lib/workflow.js';

export type JourneyColumn = {
  stage: WorkflowStage;
  /** Position among the open stages, from 1. */
  number: number;
  /** "5-day limit", or null when the stage has none. */
  limit: string | null;
  /** What happens here: each line is one automation, in the order the workflow lists them. */
  automations: string[];
  /** How many questions the apply form asks, on the first column only. */
  questions: number | null;
};

export type Journey = {
  columns: JourneyColumn[];
  /** Automations that belong to no stage: the checks finishing, someone running one by hand. */
  anytime: string[];
  /** The stages that end the candidacy, each as one phrase: "Rejected, sends the "Not this time" email". */
  ends: string[];
};

const upperFirst = (text: string): string => text.replace(/^./, (first) => first.toUpperCase());
const lowerFirst = (text: string): string => text.replace(/^./, (first) => first.toLowerCase());

/** One step as the journey says it: a verb, third person. */
export function actionWords(action: AutomationAction, workflow: HiringWorkflow, templateNames: ReadonlyMap<string, string>): string {
  switch (action.type) {
    case 'SendEmail': {
      const name = action.emailTemplate ? templateNames.get(action.emailTemplate) : undefined;
      return name ? `Sends the "${name}" email` : 'Sends an email';
    }
    case 'SendQuestionnaire': {
      const form = followUpsOf(workflow).find((followUp) => followUp.id === action.questionnaireId);
      const count = form?.questions.length ?? 0;
      return `Sends the "${followUpName(workflow, action.questionnaireId)}" form${count > 0 ? `, ${count} ${count === 1 ? 'question' : 'questions'}` : ''}`;
    }
    case 'MoveToStage':
      return `Moves them to ${workflow.stages.find((stage) => stage.id === action.stageId)?.name ?? 'another stage'}`;
    case 'AddTag':
      return `Adds the tag "${action.tag ?? ''}"`;
    case 'AssignReviewer':
      return 'Asks the team to review';
    case 'NotifyTeam':
      return 'Tells the team';
    case 'CreateTask':
      return 'Adds a task for the team';
    case 'CallWebhook':
      return 'Tells another system';
    case 'ScheduleInterview':
      return 'Offers an interview';
    case 'RequestVerification':
      return 'Asks for a check';
    case 'QueueForRejectionReview':
      return 'Queues them for the team to look at first';
    case 'Reject':
      return 'Declines them';
    default:
      return 'Does something';
  }
}

// The one word the screens use for a verification level (redesign pick 1): Clear, Review or Verify.
const levelWords: Record<string, string> = {
  NoConcerns: 'Clear',
  NotIndicated: 'Clear',
  ReviewSuggested: 'Review',
  SomeIndicators: 'Review',
  VerificationRecommended: 'Verify',
  StrongIndicators: 'Verify',
};

/** A condition in words, for the few the journey can say plainly; anything else is "the conditions match". */
function conditionWords(rule: AutomationCondition, workflow: HiringWorkflow): string | null {
  const question = /^answers\.(.+)$/.exec(rule.fact);
  if (question) {
    const label = [...workflow.questionnaire.questions, ...followUpsOf(workflow).flatMap((followUp) => followUp.questions)].find(
      (candidate) => candidate.id === question[1],
    )?.label;
    if (label && rule.operator === 'EqualTo' && typeof rule.value === 'string') return `the answer to "${label}" is ${rule.value}`;
    return label ? `the answer to "${label}" matches` : null;
  }
  if (rule.fact.startsWith('verification.')) {
    const values = (rule.values ?? (rule.value ? [rule.value] : [])).map((value) => levelWords[String(value)] ?? String(value));
    return values.length > 0 ? `verification is ${values.join(' or ')}` : 'verification matches';
  }
  return null;
}

/** An automation as one line: when it applies (if it is not just the stage), then what it does. */
export function automationWords(
  automation: Automation,
  workflow: HiringWorkflow,
  templateNames: ReadonlyMap<string, string> = emailTemplateNames(undefined),
): string {
  const actions = automation.actions.map((action) => actionWords(action, workflow, templateNames));
  const first = actions[0] ?? 'Does something';
  const rest = actions.slice(1).map(lowerFirst);
  const steps = [first, ...rest].join(', then ');
  const { trigger } = automation;
  const rule = automation.conditions?.rules?.[0];
  const condition = rule ? (conditionWords(rule, workflow) ?? 'the conditions match') : null;
  const more = (automation.conditions?.rules?.length ?? 0) > 1 ? ' and the other conditions match' : '';

  if (trigger.type === 'SlaBreached') return `${steps} when the time limit passes`;
  const when =
    trigger.type === 'VerificationAssessed'
      ? 'When the checks finish'
      : trigger.type === 'ReviewerDecision'
        ? 'When a reviewer records a decision'
        : trigger.type === 'StageExited'
          ? 'When they leave'
          : trigger.type === 'TimeElapsed'
            ? `${trigger.afterHours ?? '?'} hours after they arrive`
            : trigger.type === 'QuestionnaireCompleted'
              ? trigger.questionnaireId
                ? `When they answer "${followUpName(workflow, trigger.questionnaireId)}"`
                : 'When they answer a form'
              : trigger.type === 'InterviewScheduled'
                ? 'When an interview is booked'
                : trigger.type === 'Manual'
                  ? 'When someone runs it'
                  : null;
  const prefix = [when, condition ? `${when ? 'if' : 'If'} ${condition}${more}` : null].filter(Boolean).join(', ');
  return prefix ? `${upperFirst(prefix)}, ${lowerFirst(steps)}` : steps;
}

/** The stage an automation belongs to: its own, or the first stage for an arriving application. Others have none. */
export function homeOf(workflow: HiringWorkflow, automation: Automation): string | null {
  return laneOf(automation) ?? (automation.trigger.type === 'ApplicationSubmitted' ? (workflow.stages[0]?.id ?? null) : null);
}

/** The enabled automations that run at a stage, as the journey says them, with their place in the workflow's list. */
export function automationsAt(
  workflow: HiringWorkflow,
  stageId: string,
  templateNames: ReadonlyMap<string, string> = emailTemplateNames(undefined),
  reminders = true,
): { index: number; text: string }[] {
  return workflow.automations.flatMap((automation, index) =>
    automation.enabled !== false && homeOf(workflow, automation) === stageId && (reminders || automation.id !== reminderId(stageId))
      ? [{ index, text: automationWords(automation, workflow, templateNames) }]
      : [],
  );
}

/** The journey: a column per open stage, the automations that belong to no stage, then the end stages. */
export function journeyOf(
  workflow: HiringWorkflow,
  templateNames: ReadonlyMap<string, string> = emailTemplateNames(undefined),
): Journey {
  const open = workflow.stages.filter((stage) => !isTerminal(stage.kind));
  const ends = workflow.stages.filter((stage) => isTerminal(stage.kind));
  const first = workflow.stages[0]?.id;

  const lines = new Map<string, string[]>();
  const anytime: string[] = [];
  const known = new Set(workflow.stages.map((stage) => stage.id));
  for (const automation of workflow.automations) {
    if (automation.enabled === false) continue;
    const home = homeOf(workflow, automation);
    const text = automationWords(automation, workflow, templateNames);
    // An automation whose stage is gone (a problem the validator names) still shows, under Any time.
    if (home === null || !known.has(home)) anytime.push(text);
    else lines.set(home, [...(lines.get(home) ?? []), text]);
  }

  const questions = workflow.questionnaire.questions.length;
  return {
    columns: open.map((stage, index) => ({
      stage,
      number: index + 1,
      limit: stage.slaDays ? `${stage.slaDays}-day limit` : null,
      automations: lines.get(stage.id) ?? [],
      questions: stage.id === first && questions > 0 ? questions : null,
    })),
    anytime,
    ends: ends.map((stage) => {
      const said = lines.get(stage.id) ?? [];
      return said.length > 0 ? `${stage.name}, ${said.map(lowerFirst).join(', ')}` : stage.name;
    }),
  };
}
