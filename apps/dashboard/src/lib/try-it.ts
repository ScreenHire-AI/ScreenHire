// Try it (design system section 11, Try it; workflow-engine.md section 4): one made-up candidate walked through the
// pipeline a stage at a time. This module holds the rules, kept apart from the components so they can be tested on
// their own: who can apply, which moments happen when they reach a stage, and what each step the evaluator ran looks
// like on screen (an email, a task, a line saying the person decides). The API's evaluator stays the judge of what
// runs (`POST /v1/workflows/simulate`); nothing here is saved or sent.
import type {
  AutomationAction,
  AutomationFacts,
  AutomationTrigger,
  HiringWorkflow,
  Question,
  SimulateWorkflowRequest,
  SimulatedAutomation,
  WorkflowStage,
} from '@screenhire/api-client';
import type { AnswerValue } from '@screenhire/ui/questions';
import { actionWords } from '#lib/action-words.js';
import { trackedStages, type Tracker } from '#lib/pipeline-tracker.js';
import { allQuestions } from '#lib/workflow.js';

/** A question in quotes, ending the sentence: its own question mark ends it, so no full stop follows the quote. */
const quoted = (label: string): string => `"${label}"${/[?!.]$/.test(label.trim()) ? '' : '.'}`;

/** The candidate's first name, the API's sample candidate (Jordan Lee), so the emails and the buttons say the same name. */
export const candidateName = 'Jordan';

export type ScenarioId = 'fit' | 'cannot' | 'check' | 'form';

export interface Scenario {
  id: ScenarioId;
  title: string;
  description: string;
}

/** A yes-or-no must-have question: the answer that passes it and the one that does not. */
interface MustHave {
  question: Question;
  pass: string;
  fail: string;
}

function mustHavesOf(workflow: HiringWorkflow): MustHave[] {
  return allQuestions(workflow).flatMap((question) =>
    question.type === 'YesNo' && question.knockout
      ? [{ question, ...(question.knockout.disqualifyingAnswer ? { pass: 'no', fail: 'yes' } : { pass: 'yes', fail: 'no' }) }]
      : [],
  );
}

/**
 * Who can apply, as four big cards. "Can't work here" needs a must-have yes-or-no question to say no to; without one it
 * would be the same candidate as "A good fit", so it is left out.
 */
export function scenariosOf(workflow: HiringWorkflow): Scenario[] {
  const scenarios: Scenario[] = [{ id: 'fit', title: 'A good fit', description: 'Says yes to everything you need.' }];
  const mustHave = mustHavesOf(workflow)[0];
  if (mustHave) scenarios.push({ id: 'cannot', title: "Can't work here", description: `Answers ${mustHave.fail === 'yes' ? 'Yes' : 'No'} to ${quoted(mustHave.question.label)}` });
  scenarios.push(
    { id: 'check', title: 'Something to check', description: 'The checks flag something. You decide what happens.' },
    { id: 'form', title: 'Fill in the form yourself', description: 'Answer the questions as a candidate would.' },
  );
  return scenarios;
}

/** The candidate as the evaluator reads them: their answers and whether the checks flagged something. */
export interface Candidate {
  answers: Record<string, string[]>;
  flagged: boolean;
  /** The must-have question they failed and what they said to it, for the line that says they wait for a person. */
  failed?: { label: string; said: string };
}

/** The candidate a card stands for. "Fill in the form yourself" passes its answers instead (`candidateFromForm`). */
export function candidateFor(workflow: HiringWorkflow, scenario: Exclude<ScenarioId, 'form'>): Candidate {
  const mustHaves = mustHavesOf(workflow);
  const answers = Object.fromEntries(mustHaves.map((mustHave) => [mustHave.question.id, [mustHave.pass]]));
  if (scenario === 'cannot' && mustHaves[0]) {
    answers[mustHaves[0].question.id] = [mustHaves[0].fail];
    return { answers, flagged: false, failed: { label: mustHaves[0].question.label, said: mustHaves[0].fail } };
  }
  return { answers, flagged: scenario === 'check' };
}

/** The answers a person gave in the form preview, as the evaluator reads them. A file question has nothing to read. */
export function candidateFromForm(workflow: HiringWorkflow, given: Record<string, AnswerValue | undefined>): Candidate {
  const answers: Record<string, string[]> = {};
  for (const [id, value] of Object.entries(given)) {
    const values = (Array.isArray(value) ? value : value === undefined ? [] : [value]).map((item) => item.trim()).filter(Boolean);
    if (values.length > 0) answers[id] = values;
  }
  const failed = mustHavesOf(workflow).find((mustHave) => answers[mustHave.question.id]?.includes(mustHave.fail));
  return { answers, flagged: false, failed: failed && { label: failed.question.label, said: failed.fail } };
}

// The verification levels as the engine names them (verification-engine.md): the calm ones, and the ones that ask a person
// to look. They stay inside the facts; the screen never names a level (non-negotiable 1).
export function factsOf(candidate: Candidate, tags: readonly string[], hoursInStage: number | null = null): AutomationFacts {
  return {
    answers: Object.entries(candidate.answers).map(([questionId, values]) => ({ questionId, values })),
    tags: [...tags],
    source: 'CareersSite',
    hoursInStage,
    verificationAuthenticity: candidate.flagged ? 'VerificationRecommended' : 'NoConcerns',
    verificationAiAssistance: candidate.flagged ? 'StrongIndicators' : 'NotIndicated',
    reviewerRecommendation: null,
  };
}

/** The stages the candidate walks: the tracked ones, so Rejected and Withdrawn, the ways out, are never a next stage. */
export const walkStages = (workflow: HiringWorkflow): WorkflowStage[] => trackedStages(workflow.stages);

/** The stage after `stageId` on the walk, or undefined on the last one. */
export function nextStage(workflow: HiringWorkflow, stageId: string): WorkflowStage | undefined {
  const stages = walkStages(workflow);
  const index = stages.findIndex((stage) => stage.id === stageId);
  return index < 0 ? undefined : stages[index + 1];
}

/**
 * What happens when the candidate reaches a stage, in the order the engine sees it. Applying is the first stage's: the
 * application arrives, the checks finish, and they enter the stage (ApplicationSubmitted.cs raises both). Any later stage is
 * entered.
 */
export function arrivalTriggers(workflow: HiringWorkflow, stageId: string): AutomationTrigger[] {
  const entered: AutomationTrigger = { type: 'StageEntered', stageId };
  return walkStages(workflow)[0]?.id === stageId ? [{ type: 'ApplicationSubmitted' }, { type: 'VerificationAssessed' }, entered] : [entered];
}

/** The request for one moment. `chain` names the automations that led here, so the engine's loop guard sees it. */
export function requestFor(
  workflow: HiringWorkflow,
  trigger: AutomationTrigger,
  candidate: Candidate,
  tags: readonly string[],
  chain: readonly string[] = [],
  hoursInStage: number | null = null,
): SimulateWorkflowRequest {
  return { workflow, trigger, facts: factsOf(candidate, tags, hoursInStage), accommodationRequested: false, chain: [...chain] };
}

/** How many times one click follows automations that move the candidate on by themselves. Two that move back and forth stop here. */
export const maxAutomaticMoves = 3;

/** The most emails one "Send me these emails" sends: the API's limit (TryItEmailsRequestValidator.MaxEmails). */
export const maxEmailsPerSend = 5;

/** One action the evaluator ran, with the automation that ran it. */
export interface Ran {
  automationId: string;
  action: AutomationAction;
}

export const ranOf = (results: readonly SimulatedAutomation[]): Ran[] =>
  results.filter((result) => result.outcome === 'Matched').flatMap((result) => result.actions.map((action) => ({ automationId: result.automationId, action })));

/** The email template an action sends, by the template id the worker uses for it (ExecuteAutomationActionHandler.cs). */
export function emailOf(action: AutomationAction): string | null {
  switch (action.type) {
    case 'SendEmail':
      return action.emailTemplate ?? null;
    case 'SendQuestionnaire':
      return 'questionnaire-request';
    case 'ScheduleInterview':
      return 'interview-invitation';
    case 'Reject':
      return 'application-rejected';
    default:
      return null;
  }
}

/** One thing on a stage's screen: an email as an inbox shows it, a task chip, or a line that says what happened. */
export type Thing =
  | { kind: 'email'; templateId: string }
  | { kind: 'task'; text: string; action: AutomationAction['type'] }
  | { kind: 'decide'; text: string }
  | { kind: 'moved'; text: string }
  | { kind: 'ended'; text: string };

/** The actions that are shown as a chip: work for the team, or something ScreenHire does on its own. */
const chipActions: readonly AutomationAction['type'][] = ['CreateTask', 'AssignReviewer', 'NotifyTeam', 'RequestVerification', 'AddTag', 'CallWebhook'];

/**
 * The things a stage shows, from the actions that ran there, in order, each email once. Moving on by itself and being
 * turned down are lines of their own; a review list and flagged checks become the line that says a person decides.
 * `waited` is the number of days let pass, for that line's words.
 */
export function thingsOf(
  workflow: HiringWorkflow,
  ran: readonly Ran[],
  candidate: Candidate,
  options: {
    first: boolean;
    templateNames: ReadonlyMap<string, string>;
    /** The stages an automation moved them on to by itself, in order. */
    movedTo?: string[];
    /** It kept moving them on past `maxAutomaticMoves`. */
    stopped?: boolean;
    /** An automation moved them to a way out (Rejected, Withdrawn), which ends the walk. */
    left?: string;
    waited?: number;
    /** The last stage: there is no stage to move them on to. */
    last?: boolean;
  },
): Thing[] {
  const things: Thing[] = [];
  const emails = new Set<string>();
  // Moving on by itself is one line however many stages it took, so it never pushes the stage's own things out of sight.
  const moved = options.movedTo ?? [];
  if (moved.length > 0) {
    const stop = options.stopped ? ' It keeps moving them on, so Try it stops here.' : '';
    things.push({ kind: 'moved', text: `${candidateName} moved on to ${moved.join(', then ')} by itself.${stop}` });
  }
  let review = false;
  let ended = false;
  for (const { action } of ran) {
    const template = emailOf(action);
    if (template && !emails.has(template)) {
      emails.add(template);
      things.push({ kind: 'email', templateId: template });
    }
    if (action.type === 'QueueForRejectionReview') review = true;
    if (action.type === 'Reject') ended = true;
    if (chipActions.includes(action.type)) things.push({ kind: 'task', text: actionWords(action, workflow, options.templateNames), action: action.type });
  }
  if (ended) {
    things.push({ kind: 'ended', text: `${candidateName} is turned down here, and gets the email above.` });
    return things;
  }
  if (options.left) {
    things.push({ kind: 'ended', text: `${candidateName} moved to ${options.left} by itself, which ends the process for them.` });
    return things;
  }
  if (review) {
    const because = candidate.failed ? `${candidateName} said ${candidate.failed.said} to ${quoted(candidate.failed.label)} ` : '';
    things.push({ kind: 'decide', text: `${because}They wait on your review list, and nobody is turned down until you decide.` });
  } else if (options.first && candidate.flagged) {
    things.push({ kind: 'decide', text: `The checks flagged something about ${candidateName}. A person looks and decides what happens.` });
  } else {
    const decide = options.last ? 'This is the last stage of your process.' : `You decide when ${candidateName} moves on.`;
    const nothing = options.waited ? `Nothing happens by itself after ${days(options.waited)}. ` : 'Nothing happens by itself here. ';
    things.push({ kind: 'decide', text: ran.length === 0 ? `${nothing}${decide}` : decide });
  }
  return things;
}

/** The most things a stage shows at once (the owner's walk-through: two or three). The rest are one click away. */
export const shownThings = 3;

/**
 * The things shown first, and how many wait behind "more". The line that says a person decides, or that the walk ended,
 * always shows: it is the stage's decision (W12).
 */
export function splitThings(things: readonly Thing[]): { shown: Thing[]; hidden: Thing[] } {
  const last = things.at(-1);
  const closing = last && (last.kind === 'decide' || last.kind === 'ended') ? last : undefined;
  const rest = closing ? things.slice(0, -1) : [...things];
  const room = closing ? shownThings - 1 : shownThings;
  return { shown: [...rest.slice(0, room), ...(closing ? [closing] : [])], hidden: rest.slice(room) };
}

export const days = (count: number): string => `${count} ${count === 1 ? 'day' : 'days'}`;

/**
 * How long "Let some days pass" waits in a stage, and the moments that fire then: the time limit's days with its passed
 * limit, else the first time-passed automation of the stage, in whole days. Null when nothing in the stage waits.
 */
export function waitOf(workflow: HiringWorkflow, stage: WorkflowStage): { days: number; triggers: AutomationTrigger[]; overdue: boolean } | null {
  const timed = workflow.automations.filter(
    (automation) => automation.enabled !== false && automation.trigger.type === 'TimeElapsed' && automation.trigger.stageId === stage.id && (automation.trigger.afterHours ?? 0) > 0,
  );
  const limit = stage.slaDays && stage.slaDays > 0 ? stage.slaDays : null;
  const count = limit ?? (timed.length > 0 ? Math.max(1, Math.ceil(Math.min(...timed.map((automation) => automation.trigger.afterHours!)) / 24)) : null);
  if (count === null) return null;
  const triggers: AutomationTrigger[] = [];
  if (limit) triggers.push({ type: 'SlaBreached', stageId: stage.id });
  if (timed.some((automation) => automation.trigger.afterHours! <= count * 24)) triggers.push({ type: 'TimeElapsed', stageId: stage.id });
  return { days: count, triggers, overdue: limit !== null };
}

/** The stage rail for the candidate in `stageId`: the stages before it passed, it current, the rest still to come. */
export function trackerAt(workflow: HiringWorkflow, stageId: string): Tracker {
  const stages = walkStages(workflow);
  const position = stages.findIndex((stage) => stage.id === stageId);
  return {
    steps: stages.map((stage, index) => ({ id: stage.id, name: stage.name, state: index < position ? 'passed' : index === position ? 'current' : 'upcoming' })),
    position: position + 1,
    total: stages.length,
  };
}
