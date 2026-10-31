import type { ScorecardEntry, ScorecardQuestion, ScorecardTemplateSummary, WorkflowStage } from '@screenhire/api-client';

/**
 * A scorecard a reviewer can fill in (workflow-engine.md section 8): a stage's own, the job's copy of the questions, for
 * a stage the candidate has reached; or a library scorecard, given at the stage the candidate is in now.
 */
export interface ScorecardChoice {
  /** The select's value: `stage:{id}` or `library:{key}`. */
  value: string;
  /** The option's words. */
  label: string;
  group: 'stages' | 'builtIn' | 'own';
  /** The scorecard's name, for the success line. */
  name: string;
  /** A stage choice's stage; it is submitted by stage. */
  stageId?: string;
  /** The library scorecard it is, when known: a stage's names one unless it is an old copy. */
  key: string | null;
  /** A stage choice carries the job's copy; a library one is read when chosen. */
  questions?: readonly ScorecardQuestion[];
}

export interface ScorecardChoiceSources {
  stages: readonly WorkflowStage[];
  /** The stages the application has been in, the current one included. */
  reached: ReadonlySet<string>;
  currentStageId: string | undefined;
  library: readonly ScorecardTemplateSummary[];
  /** The reader's own scorecards on the application. */
  mine: readonly ScorecardEntry[];
  /** A library scorecard to offer even when archived: one a link asks for (a teammate's request made before it was archived). */
  alsoOffer?: string | null;
}

/**
 * Every scorecard the reader can still give, in the order the sheet lists them: the job's stage scorecards (the current
 * stage first), then the built-ins and the team's own. Left out: what they gave already (one per stage and scorecard),
 * archived scorecards, and a library scorecard that is the current stage's own, which the stage's choice covers.
 */
export function scorecardChoices(sources: ScorecardChoiceSources): ScorecardChoice[] {
  const { stages, reached, currentStageId, library, mine, alsoOffer } = sources;
  // A stage's own scorecard is given once a scorecard of that kind is in for the stage; one given before scorecards
  // kept their kind was the stage's own.
  const stageGiven = (stage: WorkflowStage) =>
    mine.some((card) => card.stageId === stage.id && (!card.scorecardTemplateId || card.scorecardTemplateId === stage.scorecardTemplateId));
  const libraryGiven = (key: string) => mine.some((card) => card.stageId === currentStageId && card.scorecardTemplateId === key);
  const current = stages.find((stage) => stage.id === currentStageId);

  const ofStages = stages
    .filter((stage) => stage.scorecard && reached.has(stage.id))
    .filter((stage) => !stageGiven(stage))
    .toSorted((a, b) => Number(b.id === currentStageId) - Number(a.id === currentStageId))
    .map<ScorecardChoice>((stage) => {
      const name = stage.scorecard!.name;
      return {
        value: `stage:${stage.id}`,
        label: name === stage.name ? stage.name : `${stage.name}: ${name}`,
        group: 'stages',
        name,
        stageId: stage.id,
        key: stage.scorecardTemplateId ?? null,
        questions: stage.scorecard!.questions,
      };
    });

  const ofLibrary = library
    .filter((scorecard) => !scorecard.archived || scorecard.id === alsoOffer)
    .filter((scorecard) => !(current?.scorecard && current.scorecardTemplateId === scorecard.id))
    .filter((scorecard) => !libraryGiven(scorecard.id))
    .map<ScorecardChoice>((scorecard) => ({
      value: `library:${scorecard.id}`,
      label: scorecard.name,
      group: scorecard.builtIn ? 'builtIn' : 'own',
      name: scorecard.name,
      key: scorecard.id,
    }));

  return [...ofStages, ...ofLibrary.filter((choice) => choice.group === 'builtIn'), ...ofLibrary.filter((choice) => choice.group === 'own')];
}

/** The choice a link's scorecard names: a stage that took that scorecard (the current one first), else the library's. */
export const choiceFor = (choices: readonly ScorecardChoice[], key: string): ScorecardChoice | undefined =>
  choices.find((choice) => choice.group === 'stages' && choice.key === key) ?? choices.find((choice) => choice.group !== 'stages' && choice.key === key);

/** What Fill in starts on: the current stage's own scorecard while the reader has not given it, else nothing chosen. */
export const defaultChoice = (choices: readonly ScorecardChoice[], currentStageId: string | undefined): ScorecardChoice | undefined =>
  choices.find((choice) => choice.stageId !== undefined && choice.stageId === currentStageId);

/** A scorecard named in a sentence: "Interview scorecard", or the name alone when it already ends in "scorecard". */
export const scorecardWords = (name: string): string => (/scorecard$/i.test(name.trim()) ? name.trim() : `${name.trim()} scorecard`);
