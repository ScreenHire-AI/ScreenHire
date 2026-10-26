import type { ScorecardAnswerEntry } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { answerText, ratingWords, scaleOf, starFills } from '#lib/scorecards.js';

const answer = (entry: Partial<ScorecardAnswerEntry> & Pick<ScorecardAnswerEntry, 'type'>): ScorecardAnswerEntry => ({ questionId: 'q', label: 'Q', ...entry });

describe('scorecards in words', () => {
  it('says each kind of answer as the list shows it', () => {
    expect(answerText(answer({ type: 'Rating5', rating: 4 }))).toBe('4 of 5');
    expect(answerText(answer({ type: 'Rating10', rating: 7 }))).toBe('7 of 10');
    expect(answerText(answer({ type: 'RatingYesNo', value: 'no' }))).toBe('No');
    expect(answerText(answer({ type: 'YesNo', value: 'yes' }))).toBe('Yes');
    const options = [
      { id: 'a', label: 'System design' },
      { id: 'b', label: 'Testing' },
    ];
    expect(answerText(answer({ type: 'SingleChoice', value: 'b', options }))).toBe('Testing');
    expect(answerText(answer({ type: 'MultiChoice', values: ['a', 'b'], options }))).toBe('System design, Testing');
    expect(answerText(answer({ type: 'ShortText', value: 'Solid.' }))).toBe('Solid.');
    expect(answerText(answer({ type: 'LongText' }))).toBeNull();
  });

  it('reads a rating aloud, or says why there is none', () => {
    expect(ratingWords({ state: 'Rated', value: 4.1, scorecards: 3 })).toBe('Rated 4.1 out of 5 from 3 scorecards');
    expect(ratingWords({ state: 'Rated', value: 3, scorecards: 1 })).toBe('Rated 3.0 out of 5 from 1 scorecard');
    expect(ratingWords({ state: 'NotRated', scorecards: 0 })).toBe('Not rated yet');
    expect(ratingWords({ state: 'AfterYourScorecard', scorecards: 0 })).toBe('Rating after your scorecard');
  });

  it('fills stars to the tenth', () => {
    expect(starFills(4.1)).toEqual([1, 1, 1, 1, 0.1]);
    expect(starFills(3.7)).toEqual([1, 1, 1, 0.7, 0]);
    expect(starFills(1)).toEqual([1, 0, 0, 0, 0]);
  });

  it('numbers the scales lowest first', () => {
    expect(scaleOf('Rating5')).toEqual([1, 2, 3, 4, 5]);
    expect(scaleOf('Rating10')).toHaveLength(10);
    expect(scaleOf('RatingYesNo')).toEqual([]);
  });
});
