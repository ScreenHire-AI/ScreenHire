import type { ScorecardEntry, ScorecardTemplateSummary, WorkflowStage } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { choiceFor, defaultChoice, scorecardChoices, scorecardWords, type ScorecardChoiceSources } from './scorecard-choices.js';

const card = (name: string) => ({ name, questions: [{ id: 'q', type: 'Rating5' as const, label: 'Skills', required: false }] });
const stages: WorkflowStage[] = [
  { id: 'applied', name: 'Applied', kind: 'Applied' },
  { id: 'screening', name: 'Screening', kind: 'Screening', scorecard: card('Screening'), scorecardTemplateId: 'screening' },
  { id: 'interview', name: 'Interview', kind: 'Interview', scorecard: card('Engineering interview'), scorecardTemplateId: 'engineering-interview' },
  { id: 'offer', name: 'Offer', kind: 'Offer', scorecard: card('Offer check'), scorecardTemplateId: null },
] as WorkflowStage[];
const summary = (id: string, name: string, builtIn: boolean, archived = false) => ({ id, name, builtIn, archived }) as ScorecardTemplateSummary;
const library = [
  summary('screening', 'Screening', true),
  summary('engineering-interview', 'Engineering interview', true),
  summary('phone-screen', 'Phone screen', true),
  summary('own-1', 'Culture add', false),
  summary('own-2', 'Old screen', false, true),
];
const mine = (stageId: string, scorecardTemplateId: string | null) => ({ stageId, scorecardTemplateId }) as ScorecardEntry;
const sources = (overrides: Partial<ScorecardChoiceSources> = {}): ScorecardChoiceSources => ({
  stages,
  reached: new Set(['applied', 'screening', 'interview']),
  currentStageId: 'interview',
  library,
  mine: [],
  ...overrides,
});
const values = (choices: { value: string }[]) => choices.map((choice) => choice.value);

describe('scorecard choices', () => {
  it("offers the reached stages' own scorecards, the current stage first, then the built-ins and the team's own", () => {
    const choices = scorecardChoices(sources());

    expect(values(choices)).toEqual(['stage:interview', 'stage:screening', 'library:screening', 'library:phone-screen', 'library:own-1']);
    expect(choices[0]).toMatchObject({ label: 'Interview: Engineering interview', name: 'Engineering interview', stageId: 'interview', key: 'engineering-interview' });
    expect(choices[1]).toMatchObject({ label: 'Screening', group: 'stages' });
  });

  it("leaves out what the reader gave: a stage's own once given (an old scorecard with no kind counts), a library scorecard at the current stage", () => {
    const choices = scorecardChoices(sources({ mine: [mine('screening', null), mine('interview', 'phone-screen')] }));

    expect(values(choices)).toEqual(['stage:interview', 'library:screening', 'library:own-1']);
  });

  it('offers an archived scorecard only when a link names it', () => {
    expect(values(scorecardChoices(sources({ alsoOffer: 'own-2' })))).toContain('library:own-2');
  });

  it("resolves a link's scorecard to the stage that took it before the library's, and starts on the current stage", () => {
    const choices = scorecardChoices(sources());

    expect(choiceFor(choices, 'engineering-interview')?.value).toBe('stage:interview');
    expect(choiceFor(choices, 'phone-screen')?.value).toBe('library:phone-screen');
    expect(choiceFor(choices, 'nothing')).toBeUndefined();
    expect(defaultChoice(choices, 'interview')?.value).toBe('stage:interview');
    expect(defaultChoice(scorecardChoices(sources({ currentStageId: 'applied' })), 'applied')).toBeUndefined();
  });

  it('names a scorecard in a sentence without saying scorecard twice', () => {
    expect(scorecardWords('Interview')).toBe('Interview scorecard');
    expect(scorecardWords('Culture scorecard')).toBe('Culture scorecard');
  });
});
