// The Pipeline tab's short facts (design system section 11, Stage cards and Stage flow): each stage read back as a few
// lines, one fact a line, so a recruiter sees what happens at a stage without reading sentences. Pure functions over
// the workflow, so the words are tested without a page. Generated text never names a person: a reviewer is "the team".
import type { Automation, AutomationAction, HiringWorkflow, WorkflowStage } from '@screenhire/api-client';
import { ruleShort } from '#lib/automation-sentence.js';
import { emailTemplateNames, followUpName, isTerminal, placeOf, reminderId, timeLimitLabel } from '#lib/workflow.js';

export type StageFactKind = 'limit' | 'meeting' | 'email' | 'automation' | 'questions';

export type StageFact = {
  kind: StageFactKind;
  text: string;
  /** The automation the line stands for, so it can link to it on the Automations tab. */
  automationId?: string;
};

/** "Respond within 3 days": how the time limit reads wherever it shows. */
export const respondWithin = (days: number): string => `Respond within ${timeLimitLabel(days)}`;

/** A step in the few words a card line holds, lower case, as the second half of "Right to work: No, team looks first". */
export function actionShort(action: AutomationAction, workflow: HiringWorkflow, templateNames: ReadonlyMap<string, string>): string {
  switch (action.type) {
    case 'SendEmail': {
      const name = action.emailTemplate ? templateNames.get(action.emailTemplate) : undefined;
      return name ? `sends "${name}"` : 'sends an email';
    }
    case 'SendQuestionnaire':
      return `sends "${followUpName(workflow, action.questionnaireId)}"`;
    case 'MoveToStage':
      return `moves to ${workflow.stages.find((stage) => stage.id === action.stageId)?.name ?? 'another stage'}`;
    case 'AddTag':
      return action.tag ? `tag ${action.tag}` : 'adds a tag';
    case 'AssignReviewer':
      return 'team reviews';
    case 'NotifyTeam':
      return 'tell the team';
    case 'CreateTask':
      return 'task for the team';
    case 'CallWebhook':
      return 'tells another system';
    case 'ScheduleInterview':
      return 'offers an interview';
    case 'RequestVerification':
      return 'asks for a check';
    case 'QueueForRejectionReview':
      return 'team looks first';
    case 'Reject':
      return 'declines';
    default:
      return 'does something';
  }
}

const upperFirst = (text: string): string => text.replace(/^./, (first) => first.toUpperCase());

/** What starts an automation, when it is not simply arriving at its stage: "When they leave", "After 3 days". */
function triggerPrefix(automation: Automation, workflow: HiringWorkflow): string | null {
  const { trigger } = automation;
  switch (trigger.type) {
    case 'StageExited':
      return 'When they leave';
    case 'TimeElapsed': {
      const hours = trigger.afterHours ?? 0;
      return hours > 0 && hours % 24 === 0 ? `After ${hours / 24} ${hours === 24 ? 'day' : 'days'}` : `After ${hours} ${hours === 1 ? 'hour' : 'hours'}`;
    }
    case 'SlaBreached':
      return 'When overdue';
    case 'ReviewerDecision':
      return 'When a reviewer decides';
    case 'VerificationAssessed':
    case 'VerificationCompleted':
      return 'When the checks finish';
    case 'QuestionnaireCompleted':
      return trigger.questionnaireId ? `When they answer "${followUpName(workflow, trigger.questionnaireId)}"` : 'When they answer a form';
    case 'InterviewScheduled':
      return 'When an interview is booked';
    case 'Manual':
      return 'When run by hand';
    default:
      return null;
  }
}

/**
 * An automation as a card's short label: its condition, a comma and its steps ("Right to work: No, team looks first"),
 * after what starts it when that is not arriving at the stage ("When they leave: tag left"). `withTrigger` false leaves the
 * start out, for a list that already says it ("When overdue: tell the team").
 */
export function automationLabel(
  automation: Automation,
  workflow: HiringWorkflow,
  templateNames: ReadonlyMap<string, string> = emailTemplateNames(undefined),
  withTrigger = true,
): string {
  const rules = automation.conditions?.rules ?? [];
  const joiner = automation.conditions?.match === 'Any' ? ' or ' : ', ';
  const condition = rules.map((rule) => ruleShort(rule, workflow, automation.trigger.type)).join(joiner);
  const steps = automation.actions.map((action) => actionShort(action, workflow, templateNames)).join(', ') || 'nothing yet';
  const body = condition ? `${condition}, ${steps}` : steps;
  // A check's condition already says the checks finished ("Verification: Review or Verify"), so it needs no prefix.
  const checked = (automation.trigger.type === 'VerificationAssessed' || automation.trigger.type === 'VerificationCompleted') && rules.length > 0;
  const prefix = withTrigger && !checked ? triggerPrefix(automation, workflow) : null;
  return prefix ? `${prefix}: ${upperFirst(body)}` : upperFirst(body);
}

const isOn = (automation: Automation) => automation.enabled !== false;
const onlyEmails = (automation: Automation) => automation.actions.length > 0 && automation.actions.every((action) => action.type === 'SendEmail');

/** The automations that are on and placed at the stage, in the workflow's order. */
const placedAt = (workflow: HiringWorkflow, place: string): Automation[] =>
  workflow.automations.filter((automation) => isOn(automation) && placeOf(workflow, automation) === place);

/**
 * A stage's facts, one a line, in one order: the time limit, the meeting when its name is not the stage's, each email it
 * sends, each other automation as a short label, then (on the first stage) how many questions the apply form asks. The
 * stage's own reminder is the time limit's line, so it is not listed again (W7); an automation that only sends emails is
 * its email lines.
 */
export function stageFacts(
  workflow: HiringWorkflow,
  stage: WorkflowStage,
  templateNames: ReadonlyMap<string, string> = emailTemplateNames(undefined),
): StageFact[] {
  const facts: StageFact[] = [];
  const open = !isTerminal(stage.kind);
  if (open && stage.slaDays) facts.push({ kind: 'limit', text: respondWithin(stage.slaDays) });
  const meeting = stage.meetingName?.trim();
  if (meeting && meeting.toLocaleLowerCase() !== stage.name.trim().toLocaleLowerCase()) facts.push({ kind: 'meeting', text: `Meeting: ${meeting}` });

  const here = placedAt(workflow, stage.id).filter((automation) => automation.id !== reminderId(stage.id));
  for (const automation of here) {
    if (onlyEmails(automation) && (automation.conditions?.rules.length ?? 0) === 0 && triggerPrefix(automation, workflow) === null) {
      for (const action of automation.actions) facts.push({ kind: 'email', text: upperFirst(actionShort(action, workflow, templateNames)), automationId: automation.id });
    } else {
      facts.push({ kind: 'automation', text: automationLabel(automation, workflow, templateNames), automationId: automation.id });
    }
  }

  const questions = workflow.questionnaire.questions.length;
  if (stage.id === workflow.stages[0]?.id && questions > 0) facts.push({ kind: 'questions', text: `Asks ${questions} ${questions === 1 ? 'question' : 'questions'}` });
  return facts;
}

/** The automations that belong to no stage (a manual run, any stage's time limit), each as its short label. */
export function anyStageFacts(workflow: HiringWorkflow, templateNames: ReadonlyMap<string, string> = emailTemplateNames(undefined)): StageFact[] {
  return placedAt(workflow, 'any').map((automation) => ({ kind: 'automation', text: automationLabel(automation, workflow, templateNames), automationId: automation.id }));
}

/** When a stage's time limit passes, what the team is asked to do: the short labels of its SlaBreached automations. */
export function whenOverdue(workflow: HiringWorkflow, stageId: string, templateNames: ReadonlyMap<string, string> = emailTemplateNames(undefined)): StageFact[] {
  return workflow.automations
    .filter((automation) => isOn(automation) && automation.trigger.type === 'SlaBreached' && (automation.trigger.stageId ?? stageId) === stageId)
    .map((automation) => ({ kind: 'automation', text: automationLabel(automation, workflow, templateNames, false), automationId: automation.id }));
}
