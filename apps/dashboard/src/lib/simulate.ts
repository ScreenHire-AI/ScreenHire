// The guided test of a workflow (workflow-engine.md section 4, design system "Guided test"): the words and the rules
// behind it, kept apart from the component so they can be tested on their own. The API's evaluator stays the judge of
// what would happen; this module decides which questions are worth asking, builds the ready-made candidates from what
// the workflow's automations read, and words the answer.
import type {
  Automation,
  AutomationAction,
  AutomationCondition,
  AutomationFacts,
  AutomationTrigger,
  ConditionOperator,
  HiringWorkflow,
  Question,
  SimulatedAutomation,
  TriggerType,
  WorkflowSimulationResult,
} from '@screenhire/api-client';
import {
  aiAssistanceLevels,
  allQuestions,
  authenticityLevels,
  describeAction,
  emailTemplateNames,
  factsFor,
  followUpName,
  followUpsOf,
  hasTimeLimit,
  operatorLabels,
  recommendationLabels,
  type FactOption,
} from '#lib/workflow.js';

/** The moments a person can pick, in the order they happen to a candidate, with the words the first question uses. */
const momentLabels: Partial<Record<TriggerType, string>> = {
  ApplicationSubmitted: 'A candidate applied',
  QuestionnaireCompleted: 'A candidate answered a follow-up questionnaire',
  VerificationAssessed: 'The verification checks finished',
  StageEntered: 'A candidate moved to a stage',
  TimeElapsed: 'Time passed in a stage',
  SlaBreached: 'A time limit passed',
  ReviewerDecision: 'A scorecard came in',
  InterviewScheduled: 'A candidate booked an interview',
  StageExited: 'A candidate left a stage',
  Manual: 'Someone ran an automation by hand',
};

const momentOrder = Object.keys(momentLabels) as TriggerType[];

/** Whether an automation is on. A workflow saved before the switch existed has none, which means on. */
const isOn = (automation: Automation): boolean => automation.enabled !== false;

export interface Moment {
  type: TriggerType;
  label: string;
  /** How many automations are on and react to it. */
  automations: number;
}

/**
 * The moments worth testing: those some automation that is on reacts to. Any other moment would only say that nothing
 * happens, so it is not offered.
 */
export function momentsOf(workflow: HiringWorkflow): Moment[] {
  return momentOrder
    .map((type) => ({ type, label: momentLabels[type]!, automations: workflow.automations.filter((automation) => isOn(automation) && automation.trigger.type === type).length }))
    .filter((moment) => moment.automations > 0);
}

export interface Option {
  id: string;
  name: string;
}

/** A second question a moment needs, with the answers it may take. */
export interface FollowUp {
  kind: 'stage' | 'questionnaire';
  /** The question, asked in the heading. */
  title: string;
  options: Option[];
}

const stagedTypes: readonly TriggerType[] = ['StageEntered', 'StageExited', 'TimeElapsed'];
/** These two may name a stage or leave it out, which means any stage that has a scorecard or a time limit. */
const optionallyStaged: readonly TriggerType[] = ['ReviewerDecision', 'SlaBreached'];

const stageName = (workflow: HiringWorkflow, id: string | null | undefined): string => workflow.stages.find((stage) => stage.id === id)?.name ?? id ?? '?';

/**
 * The choices a moment leaves open: which stage, or which follow-up questionnaire. Only what an automation that is on
 * reacts to is offered, so every answer has something to show. A moment with no choice, or one, returns no options to
 * ask about.
 */
export function followUpFor(workflow: HiringWorkflow, type: TriggerType): FollowUp | null {
  const listening = workflow.automations.filter((automation) => isOn(automation) && automation.trigger.type === type);
  if (stagedTypes.includes(type) || optionallyStaged.includes(type)) {
    const named = new Set(listening.flatMap((automation) => (automation.trigger.stageId ? [automation.trigger.stageId] : [])));
    // Every automation of these two that is on watches any stage: there is no stage to ask about.
    if (optionallyStaged.includes(type) && named.size === 0) return null;
    const anyStage = listening.some((automation) => !automation.trigger.stageId);
    const eligible = (stage: HiringWorkflow['stages'][number]) =>
      type === 'ReviewerDecision' ? Boolean(stage.scorecard) || Boolean(stage.scorecardTemplateId) : type === 'SlaBreached' ? hasTimeLimit(stage) : true;
    const stages = workflow.stages.filter((stage) => named.has(stage.id) || (anyStage && optionallyStaged.includes(type) && eligible(stage)));
    return {
      kind: 'stage',
      title: type === 'StageExited' ? 'Which stage did they leave?' : type === 'TimeElapsed' ? 'In which stage?' : type === 'ReviewerDecision' ? 'On which stage?' : 'Which stage?',
      options: stages.map((stage) => ({ id: stage.id, name: stage.name })),
    };
  }
  if (type === 'QuestionnaireCompleted') {
    const named = new Set(listening.flatMap((automation) => (automation.trigger.questionnaireId ? [automation.trigger.questionnaireId] : [])));
    const anyQuestionnaire = listening.some((automation) => !automation.trigger.questionnaireId);
    const questionnaires = followUpsOf(workflow).filter((followUp) => anyQuestionnaire || named.has(followUp.id));
    return { kind: 'questionnaire', title: 'Which questionnaire?', options: questionnaires.map((followUp) => ({ id: followUp.id, name: followUp.name || followUp.id })) };
  }
  return null;
}

/** Whether the person has to answer the follow-up: with fewer than two answers there is nothing to choose. */
export const needsAnswer = (followUp: FollowUp | null): followUp is FollowUp => followUp !== null && followUp.options.length > 1;

/**
 * The trigger to simulate. `answer` is the stage or questionnaire the person chose; a follow-up with one answer takes
 * it without asking, and one with none sends no stage, which the engine reads as any.
 */
export function triggerFor(workflow: HiringWorkflow, type: TriggerType, answer?: string | null): AutomationTrigger {
  const followUp = followUpFor(workflow, type);
  if (!followUp || followUp.options.length === 0) return { type };
  const chosen = followUp.options.find((option) => option.id === answer) ?? followUp.options[0]!;
  return followUp.kind === 'stage' ? { type, stageId: chosen.id } : { type, questionnaireId: chosen.id };
}

/** The automations of `workflow` that fire on `trigger`: the engine's own matching (AutomationEvaluator.Triggered). */
export function listenersOf(workflow: HiringWorkflow, trigger: AutomationTrigger): Automation[] {
  return workflow.automations.filter(
    (automation) =>
      isOn(automation) &&
      automation.trigger.type === trigger.type &&
      ((automation.trigger.stageId ?? null) === (trigger.stageId ?? null) || (optionallyStaged.includes(trigger.type) && !automation.trigger.stageId)) &&
      (trigger.type !== 'QuestionnaireCompleted' || !automation.trigger.questionnaireId || automation.trigger.questionnaireId === trigger.questionnaireId),
  );
}

/** The moment as a sentence, with its stage or questionnaire named: "A candidate moved to Phone screen". */
export function describeMoment(workflow: HiringWorkflow, trigger: AutomationTrigger): string {
  const stage = stageName(workflow, trigger.stageId);
  switch (trigger.type) {
    case 'StageEntered':
      return `A candidate moved to ${stage}`;
    case 'StageExited':
      return `A candidate left ${stage}`;
    case 'TimeElapsed':
      return `Time passed in ${stage}`;
    case 'SlaBreached':
      return trigger.stageId ? `The time limit in ${stage} passed` : 'A time limit passed';
    case 'ReviewerDecision':
      return trigger.stageId ? `A scorecard came in on ${stage}` : 'A scorecard came in';
    case 'QuestionnaireCompleted':
      return trigger.questionnaireId ? `A candidate answered "${followUpName(workflow, trigger.questionnaireId)}"` : (momentLabels.QuestionnaireCompleted ?? '');
    default:
      return momentLabels[trigger.type] ?? trigger.type;
  }
}

// ---- The candidate -------------------------------------------------------------------------------------------------

/** A sample candidate as the form holds it: every member is text or a list, so a field can be bound straight to it. */
export interface SampleCandidate {
  /** The ids chosen for each question: one for a yes or no and a single choice, several for a multi-choice, text as typed. */
  answers: Record<string, string[]>;
  /** Tags as typed, separated by commas. */
  tags: string;
  source: string;
  hoursInStage: string;
  authenticity: string;
  aiAssistance: string;
  recommendation: string;
  accommodation: boolean;
}

export const blankCandidate = (): SampleCandidate => ({
  answers: {},
  tags: '',
  source: '',
  hoursInStage: '',
  authenticity: '',
  aiAssistance: '',
  recommendation: '',
  accommodation: false,
});

/** What the automations that react to a moment read: the only things worth asking about a candidate. */
export interface Reads {
  /** The questions some condition tests, in the questionnaires' order. */
  questions: Question[];
  tags: boolean;
  source: boolean;
  hoursInStage: boolean;
  authenticity: boolean;
  aiAssistance: boolean;
  recommendation: boolean;
  /** Whether the engine's accommodation rule could change the result: a timing condition, or a time-passed trigger. */
  timing: boolean;
}

const rulesOf = (automation: Automation): AutomationCondition[] => automation.conditions?.rules ?? [];

export function readsOf(workflow: HiringWorkflow, listeners: readonly Automation[]): Reads {
  const facts = new Set(listeners.flatMap((automation) => rulesOf(automation).map((rule) => rule.fact)));
  return {
    questions: allQuestions(workflow).filter((question) => question.type !== 'File' && facts.has(`answers.${question.id}`)),
    tags: facts.has('application.tags'),
    source: facts.has('application.source'),
    hoursInStage: facts.has('application.hoursInStage'),
    authenticity: facts.has('verification.authenticity'),
    aiAssistance: facts.has('verification.aiAssistance'),
    recommendation: facts.has('reviewer.recommendation'),
    timing: facts.has('application.hoursInStage') || listeners.some((automation) => automation.trigger.type === 'TimeElapsed'),
  };
}

/** Whether there is anything to customize: a candidate with nothing the automations read has nothing to change. */
export const hasFields = (reads: Reads): boolean =>
  reads.questions.length > 0 || reads.tags || reads.source || reads.hoursInStage || reads.authenticity || reads.aiAssistance || reads.recommendation || reads.timing;

/** A yes-or-no knockout the automations read: the answer that passes it and the answer that fails it. */
function knockoutOf(question: Question): { pass: string; fail: string } | null {
  if (question.type !== 'YesNo' || !question.knockout) return null;
  return question.knockout.disqualifyingAnswer ? { pass: 'no', fail: 'yes' } : { pass: 'yes', fail: 'no' };
}

// The verification levels, as the engine names them (verification-engine.md): the calm one and the strongest.
const calmAuthenticity = 'NoConcerns';
const strongestAuthenticity = 'VerificationRecommended';
const calmAiAssistance = 'NotIndicated';
const strongestAiAssistance = 'StrongIndicators';

export interface Preset {
  id: string;
  name: string;
  /** One line on what is different about this candidate. */
  description: string;
  candidate: SampleCandidate;
}

const joinWords = (words: string[]): string => (words.length <= 1 ? (words[0] ?? '') : `${words.slice(0, -1).join(', ')} and ${words.at(-1)}`);

/**
 * The ready-made candidates for a moment, built from what its automations read. The first meets every requirement: it
 * passes every knockout, shows no verification signals and has a reviewer who said yes. The others differ from it in
 * one way: they fail a knockout, show verification signals, ask for an accommodation or have a reviewer against them.
 * Each exists only where an automation reads what it changes, so none is a copy of another.
 */
export function presetsFor(workflow: HiringWorkflow, trigger: AutomationTrigger, listeners: readonly Automation[]): Preset[] {
  const reads = readsOf(workflow, listeners);
  const knockouts = reads.questions.flatMap((question) => {
    const knockout = knockoutOf(question);
    return knockout ? [{ question, ...knockout }] : [];
  });
  const stage = workflow.stages.find((candidate) => candidate.id === trigger.stageId);
  // A passed time limit means the candidate waited at least that long; otherwise a day, which a person can change.
  const hours = reads.hoursInStage ? String(stage && hasTimeLimit(stage) && trigger.type === 'SlaBreached' ? stage.slaDays! * 24 : Math.max(24, ...listeners.map((automation) => automation.trigger.afterHours ?? 0))) : '';

  const base = (): SampleCandidate => ({
    ...blankCandidate(),
    answers: Object.fromEntries(knockouts.map((knockout) => [knockout.question.id, [knockout.pass]])),
    source: reads.source ? 'CareersSite' : '',
    hoursInStage: hours,
    authenticity: reads.authenticity ? calmAuthenticity : '',
    aiAssistance: reads.aiAssistance ? calmAiAssistance : '',
    recommendation: reads.recommendation ? 'Yes' : '',
  });

  const meets = base();
  const requirements = knockouts.length > 0 ? 'Passes every knockout question' : 'Nothing this workflow checks counts against them';
  const presets: Preset[] = [{ id: 'meets', name: 'Meets every requirement', description: requirements, candidate: meets }];

  for (const knockout of knockouts.slice(0, 2)) {
    const candidate = base();
    candidate.answers[knockout.question.id] = [knockout.fail];
    presets.push({
      id: `fails:${knockout.question.id}`,
      name: knockouts.length === 1 ? 'Fails a knockout' : `Fails "${knockout.question.label}"`,
      description: `Answers ${knockout.fail === 'yes' ? 'Yes' : 'No'} to "${knockout.question.label}"`,
      candidate,
    });
  }

  if (reads.authenticity || reads.aiAssistance) {
    const candidate = base();
    const words: string[] = [];
    if (reads.authenticity) {
      candidate.authenticity = strongestAuthenticity;
      words.push(levelLabel('authenticity', strongestAuthenticity));
    }
    if (reads.aiAssistance) {
      candidate.aiAssistance = strongestAiAssistance;
      words.push(levelLabel('aiAssistance', strongestAiAssistance));
    }
    // Levels route a candidate to a person. They are shown as words, never as a number, and never as a reason to reject.
    presets.push({ id: 'signals', name: 'Has verification signals', description: joinWords(words), candidate });
  }

  if (reads.recommendation) {
    const candidate = base();
    candidate.recommendation = 'No';
    presets.push({ id: 'against', name: 'Has a reviewer against them', description: `The scorecard recommends ${recommendationLabels.No}`, candidate });
  }

  if (reads.timing) {
    const candidate = base();
    candidate.accommodation = true;
    presets.push({ id: 'accommodation', name: 'Asked for an accommodation', description: 'Timing is not used for them', candidate });
  }
  return presets;
}

/** A level as the words the verification screens use. */
export function levelLabel(kind: 'authenticity' | 'aiAssistance', value: string): string {
  const option = (kind === 'authenticity' ? authenticityLevels : aiAssistanceLevels).find((level) => level.value === value);
  return option?.label ?? value;
}

const list = (text: string): string[] =>
  text
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean);

/** The facts the API takes for a sample candidate. */
export function factsOf(candidate: SampleCandidate): AutomationFacts {
  // A number field can hand back a number, so the text is read from whatever it holds.
  const hours = String(candidate.hoursInStage ?? '').trim();
  return {
    answers: Object.entries(candidate.answers)
      .map(([questionId, values]) => ({ questionId, values: values.map((value) => value.trim()).filter(Boolean) }))
      .filter((answer) => answer.values.length > 0),
    tags: list(candidate.tags),
    source: candidate.source || null,
    hoursInStage: hours && Number.isFinite(Number(hours)) ? Number(hours) : null,
    verificationAuthenticity: candidate.authenticity || null,
    verificationAiAssistance: candidate.aiAssistance || null,
    reviewerRecommendation: candidate.recommendation || null,
  };
}

// ---- The answer ----------------------------------------------------------------------------------------------------

/** One automation in the timeline: what it did, or why it did not run, in a line. */
export interface Step {
  id: string;
  name: string;
  ran: boolean;
  /** What ScreenHire would do, in order. Empty when it did not run. */
  actions: string[];
  /** Why it did not run, in one line. */
  why?: string;
}

const negated: Partial<Record<ConditionOperator, string>> = {
  EqualTo: 'is not',
  In: 'is not one of',
  Contains: 'does not contain',
  Exists: 'is not given',
};

/** A condition in plain words: "Do you have the right to work here? is Yes". */
export function describeRule(rule: AutomationCondition, workflow: HiringWorkflow, trigger: TriggerType): string {
  const fact: FactOption | undefined = factsFor(workflow, trigger).find((option) => option.fact === rule.fact);
  const question = rule.fact.startsWith('answers.') ? allQuestions(workflow).find((candidate) => candidate.id === rule.fact.slice('answers.'.length)) : undefined;
  const subject = question ? question.label || question.id : (fact?.label ?? rule.fact);
  const operator = rule.negate ? (negated[rule.operator] ?? `not ${operatorLabels[rule.operator]}`) : operatorLabels[rule.operator];
  const word = (value: string) => fact?.values?.find((option) => option.value === value)?.label ?? value;
  const operand = rule.operator === 'Exists' ? '' : rule.operator === 'In' ? ` ${joinWords((rule.values ?? []).map(word))}` : ` ${word(rule.value ?? '')}`;
  return `${subject} ${operator}${operand}`.trim();
}

/** Why an automation that listened did not run, from the workflow's own rules, in one line. */
function whyNot(automation: Automation, simulated: SimulatedAutomation, workflow: HiringWorkflow, trigger: TriggerType): string {
  if (simulated.outcome === 'Skipped') return 'This candidate asked for an accommodation, so it uses no timing and did not run.';
  if (simulated.outcome === 'Blocked') return simulated.reason ?? 'It was stopped.';
  const rules = rulesOf(automation);
  const failed = simulated.rules.findIndex((result) => !result.held);
  if (rules.length === 0 || failed < 0) return 'Its conditions did not hold.';
  if (automation.conditions?.match === 'Any' && rules.length > 1) return 'None of its conditions held.';
  return `It needs "${describeRule(rules[failed]!, workflow, trigger)}", and that did not hold.`;
}

/** Why an automation of the same kind never listened: it is off, or it waits for another stage or questionnaire. */
function whyNotListening(automation: Automation, workflow: HiringWorkflow): string {
  if (!isOn(automation)) return 'It is switched off.';
  if (automation.trigger.stageId) return `It waits for ${stageName(workflow, automation.trigger.stageId)}.`;
  if (automation.trigger.questionnaireId) return `It waits for "${followUpName(workflow, automation.trigger.questionnaireId)}".`;
  return 'It waits for something else.';
}

/**
 * The answer as a timeline, in the workflow's order. Every automation of the moment's kind is in it: those that ran with
 * what they would do, in order, and those that did not with one line on why.
 */
export function timelineOf(
  workflow: HiringWorkflow,
  trigger: AutomationTrigger,
  result: WorkflowSimulationResult,
  templateNames: ReadonlyMap<string, string> = emailTemplateNames(undefined),
): Step[] {
  const fired = new Map(result.automations.map((simulated) => [simulated.automationId, simulated]));
  return workflow.automations
    .filter((automation) => automation.trigger.type === trigger.type)
    .map((automation): Step => {
      const simulated = fired.get(automation.id);
      if (!simulated) return { id: automation.id, name: automation.name, ran: false, actions: [], why: whyNotListening(automation, workflow) };
      if (simulated.outcome === 'Matched') {
        return { id: automation.id, name: automation.name, ran: true, actions: simulated.actions.map((action: AutomationAction) => describeAction(action, workflow, undefined, templateNames)) };
      }
      return { id: automation.id, name: automation.name, ran: false, actions: [], why: whyNot(automation, simulated, workflow, trigger.type) };
    })
    // What would happen comes first, in the workflow's order; what would not, with its reason, follows (the sort is stable).
    .sort((a, b) => Number(b.ran) - Number(a.ran));
}
