// The Test tab's sample candidates (design system section 11, Test drive): four candidates built from the template itself,
// each tried at the moment of the workflow they matter to, and what happens to each in a few plain steps. The API's
// evaluator stays the judge (`POST /v1/workflows/simulate`); this module picks the candidates and words the answer. It
// reuses the guided test's presets (`presetsFor`), so a sample is the same candidate the guided test would offer.
import type {
  AutomationAction,
  AutomationTrigger,
  HiringWorkflow,
  SimulateWorkflowRequest,
  WorkflowSimulationResult,
} from '@screenhire/api-client';
import { actionWords } from '#lib/journey-text.js';
import { blankCandidate, factsOf, listenersOf, momentsOf, presetsFor, triggerFor, type Preset } from '#lib/simulate.js';
import type { TriggerType } from '@screenhire/api-client';

export interface Sample {
  id: string;
  /** "Meets every requirement". */
  name: string;
  /** One short line on who this is. */
  caption: string;
  /** The moment it is tried at, in words: "When they apply". */
  moment: string;
  trigger: AutomationTrigger;
  request: SimulateWorkflowRequest;
}

const momentWords: Partial<Record<TriggerType, string>> = {
  ApplicationSubmitted: 'When they apply',
  VerificationAssessed: 'When the checks finish',
  QuestionnaireCompleted: 'When they answer a form',
  StageEntered: 'When they reach a stage',
  StageExited: 'When they leave a stage',
  TimeElapsed: 'After time in a stage',
  SlaBreached: 'When a time limit passes',
  ReviewerDecision: 'When a scorecard comes in',
  InterviewScheduled: 'When an interview is booked',
  Manual: 'When someone runs it',
};

const requestOf = (workflow: HiringWorkflow, trigger: AutomationTrigger, preset: Preset): SimulateWorkflowRequest => ({
  workflow,
  trigger,
  facts: factsOf(preset.candidate),
  accommodationRequested: preset.candidate.accommodation,
});

/**
 * The samples for a workflow, in the order a person reads them: who meets everything, who fails a knockout, whose checks
 * suggest a review, who asked for an accommodation. A sample exists only where the workflow has something that reads what
 * makes the candidate different, so none repeats another. A workflow with no automation that is on has none.
 */
export function samplesOf(workflow: HiringWorkflow): Sample[] {
  const moments = momentsOf(workflow);
  if (moments.length === 0) return [];
  const samples: Sample[] = [];
  const momentOf = (type: TriggerType) => {
    const trigger = triggerFor(workflow, type);
    return { trigger, presets: presetsFor(workflow, trigger, listenersOf(workflow, trigger)) };
  };
  const add = (id: string, name: string, caption: string, type: TriggerType, preset: Preset | undefined) => {
    if (!preset) return;
    const trigger = triggerFor(workflow, type);
    samples.push({ id, name, caption, moment: momentWords[type] ?? 'At one moment', trigger, request: requestOf(workflow, trigger, preset) });
  };

  const main = moments.find((moment) => moment.type === 'ApplicationSubmitted')?.type ?? moments[0]!.type;
  const here = momentOf(main);
  add('meets', 'Meets every requirement', '', main, here.presets.find((preset) => preset.id === 'meets'));
  const knockout = here.presets.find((preset) => preset.id.startsWith('fails:'));
  add('fails', knockout?.name ?? 'Fails a requirement', knockout?.description ?? '', main, knockout);

  const checks = moments.find((moment) => moment.type === 'VerificationAssessed');
  if (checks) {
    const signals = momentOf('VerificationAssessed').presets.find((preset) => preset.id === 'signals');
    add('signals', 'Checks suggest a review', 'Verification is Review', 'VerificationAssessed', signals);
  }

  // Everyone can ask for an accommodation, so the sample always exists once there is a moment to try it at.
  const accommodation = here.presets.find((preset) => preset.id === 'accommodation') ?? {
    id: 'accommodation',
    name: 'Asked for an accommodation',
    description: '',
    candidate: { ...(here.presets[0]?.candidate ?? blankCandidate()), accommodation: true },
  };
  add('accommodation', 'Asked for an accommodation', '', main, accommodation);
  return samples;
}

/** One step as the test says it: what ScreenHire does, never who it asks. */
function stepWords(action: AutomationAction, workflow: HiringWorkflow, templateNames: ReadonlyMap<string, string>): string {
  if (action.type === 'QueueForRejectionReview') return "Goes to the team's review list. Nobody is turned down yet";
  return actionWords(action, workflow, templateNames);
}

export interface Outcome {
  /** What happens, in order. */
  steps: string[];
  /** A muted line after the steps: what did not happen, or that nothing did. */
  note: string | null;
}

/** What happened to a sample, from the evaluator's answer. */
export function outcomeOf(
  workflow: HiringWorkflow,
  result: WorkflowSimulationResult,
  templateNames: ReadonlyMap<string, string>,
  accommodation = false,
): Outcome {
  const steps = result.automations
    .filter((automation) => automation.outcome === 'Matched')
    .flatMap((automation) => automation.actions.map((action) => stepWords(action, workflow, templateNames)));
  const skipped = result.automations.some((automation) => automation.outcome === 'Skipped');
  const note = skipped ? 'Timing steps are skipped for them' : accommodation ? 'No steps skipped' : steps.length === 0 ? 'Nothing runs, a person moves them on' : 'Then a person moves them on';
  return { steps, note };
}
