import type { JobTemplateSwitchPreview, StageMappingRequest } from '@screenhire/api-client';

/** Where each current stage's candidates go: the current stage's id to a stage of the new pipeline. */
export type StageChoices = Record<string, string>;

/** The server's preselection (kind first, then name), as the stage step starts. */
export function suggestedChoices(preview: JobTemplateSwitchPreview): StageChoices {
  return Object.fromEntries(preview.occupied.map((stage) => [stage.stageId, stage.suggestedStageId]));
}

/** The choices as the switch sends them, one per stage that holds candidates. */
export function stageMappings(preview: JobTemplateSwitchPreview, choices: StageChoices): StageMappingRequest[] {
  return preview.occupied.map((stage) => ({ fromStageId: stage.stageId, toStageId: choices[stage.stageId] ?? stage.suggestedStageId }));
}

const candidates = (count: number) => (count === 1 ? '1 candidate' : `${count} candidates`);

/** What happens to the candidates, one line per current stage, for the review step. */
export function moveLines(preview: JobTemplateSwitchPreview, choices: StageChoices): string[] {
  const names = new Map(preview.stages.map((stage) => [stage.id, stage.name]));
  return preview.occupied.map((stage) => {
    const to = names.get(choices[stage.stageId] ?? stage.suggestedStageId) ?? '';
    const one = stage.candidates === 1;
    return to === stage.name
      ? `${candidates(stage.candidates)} in ${stage.name} ${one ? 'stays' : 'stay'} in ${to}`
      : `${candidates(stage.candidates)} in ${stage.name} ${one ? 'moves' : 'move'} to ${to}`;
  });
}

/** How many candidates the switch touches, for the page's line once it is done. */
export function candidateCount(preview: JobTemplateSwitchPreview): number {
  return preview.occupied.reduce((total, stage) => total + stage.candidates, 0);
}
