import type { ApplicationRating, ScorecardAnswerEntry, ScorecardQuestionType } from '@screenhire/api-client';

// Scorecards in words and numbers (design system sections 11 and 21, workflow-engine.md section 8). The API works out
// the candidate's rating and each scorecard's part of it; this module only says them. The rating averages people's
// rating answers only, never verification output, AI signals or the stage, and no automation reads it (ADR-0013).

/** The answer kinds the candidate's rating averages. */
export const ratingKinds: readonly ScorecardQuestionType[] = ['Rating5', 'Rating10', 'RatingYesNo'];

export const isRatingKind = (type: ScorecardQuestionType) => ratingKinds.includes(type);

/** The numbers of a number scale, lowest first, or none for a kind that is not one. */
export function scaleOf(type: ScorecardQuestionType): number[] {
  if (type === 'Rating5') return [1, 2, 3, 4, 5];
  if (type === 'Rating10') return Array.from({ length: 10 }, (_, i) => i + 1);
  return [];
}

/** An answer in words as the summary list shows it, or null when it was left unanswered. */
export function answerText(answer: ScorecardAnswerEntry): string | null {
  if (answer.rating != null) return `${answer.rating} of ${answer.type === 'Rating10' ? 10 : 5}`;
  if (answer.values?.length) return answer.values.map((id) => optionLabel(answer, id)).join(', ');
  if (answer.value == null) return null;
  if (answer.type === 'YesNo' || answer.type === 'RatingYesNo') return answer.value === 'yes' ? 'Yes' : 'No';
  if (answer.type === 'SingleChoice') return optionLabel(answer, answer.value);
  return answer.value;
}

const optionLabel = (answer: ScorecardAnswerEntry, id: string) => answer.options?.find((option) => option.id === id)?.label ?? id;

/** A rating as it is shown: one decimal, always ("3.0"). */
export const formatRating = (value: number) => value.toFixed(1);

/** "3 scorecards", "1 scorecard". */
export const scorecardCount = (count: number) => `${count} ${count === 1 ? 'scorecard' : 'scorecards'}`;

/** What a screen reader hears for a rating, and the words when there is no number to show. */
export function ratingWords(rating: ApplicationRating | null | undefined): string {
  if (rating?.state === 'AfterYourScorecard') return 'Rating after your scorecard';
  if (rating?.state !== 'Rated' || rating.value == null) return 'Not rated yet';
  return `Rated ${formatRating(rating.value)} out of 5 from ${scorecardCount(rating.scorecards ?? 0)}`;
}

/** How much of each of the five stars is filled, from 0 to 1, to the tenth: 4.1 fills a tenth of the fifth star. */
export const starFills = (value: number): number[] => [0, 1, 2, 3, 4].map((star) => Math.min(1, Math.max(0, Math.round((value - star) * 10) / 10)));
