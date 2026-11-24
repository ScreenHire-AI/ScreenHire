import { checkFor, DEFAULT_QUESTIONS_PER_PAGE, progressLabel, progressPercent, questionPages, type QuestionSpec } from '@screenhire/ui/questions/model';
import { describe, expect, it } from 'vitest';

// The shared paging rules (packages/ui) the careers pages and the dashboard's preview both follow.
const question = (id: string, type: QuestionSpec['type'] = 'ShortText'): QuestionSpec => ({ id, type, label: id });
const ids = (count: number) => Array.from({ length: count }, (_, i) => question(`q${i + 1}`));

describe('questionPages', () => {
  it('splits a questionnaire without sections into pages of the page size', () => {
    expect(questionPages(ids(17), null, 8).map((page) => page.questionIds.length)).toEqual([8, 8, 1]);
    expect(questionPages(ids(3), [], 8)).toEqual([{ name: null, questionIds: ['q1', 'q2', 'q3'] }]);
    expect(questionPages([], null, 8)).toEqual([]);
  });

  it('shows each named section on its own page, leaving out skipped questions and empty pages', () => {
    const questions = [question('resume', 'File'), ...ids(3)];
    const sections = [
      { id: 'a', name: 'About you', questionIds: ['resume'] },
      { id: 'b', name: 'Experience', questionIds: ['q1', 'q2'] },
      { id: 'c', name: 'Availability', questionIds: ['q3'] },
    ];

    expect(questionPages(questions, sections, 1, ['resume'])).toEqual([
      { name: 'Experience', questionIds: ['q1', 'q2'] },
      { name: 'Availability', questionIds: ['q3'] },
    ]);
  });

  it('still asks a question no section names, on a last page', () => {
    expect(questionPages(ids(3), [{ id: 'a', name: 'First', questionIds: ['q1'] }], 8)).toEqual([
      { name: 'First', questionIds: ['q1'] },
      { name: null, questionIds: ['q2', 'q3'] },
    ]);
  });

  it('treats a nonsense page size as one question a page', () => {
    expect(questionPages(ids(2), null, 0)).toHaveLength(2);
  });
});

describe('the default page size', () => {
  it('asks four questions a page when the organization set nothing else', () => {
    expect(DEFAULT_QUESTIONS_PER_PAGE).toBe(4);
    expect(questionPages(ids(9), null, DEFAULT_QUESTIONS_PER_PAGE).map((page) => page.questionIds.length)).toEqual([4, 4, 1]);
  });
});

describe('progressPercent', () => {
  it('counts the step on screen as reached and the sent form as the last step, so the last step stays short of 100', () => {
    expect([1, 2, 3, 4].map((step) => progressPercent(step, 4))).toEqual([20, 40, 60, 80]);
    expect([1, 2].map((step) => progressPercent(step, 2))).toEqual([33, 67]);
    expect(progressPercent(1, 1)).toBe(50);
  });

  it('keeps a step outside the form inside it', () => {
    expect(progressPercent(0, 4)).toBe(20);
    expect(progressPercent(9, 4)).toBe(80);
    expect(progressPercent(1, 0)).toBe(50);
  });
});

describe('progressLabel', () => {
  it('says the percentage on every step but the review, the last page of a follow-up included', () => {
    expect([1, 2, 3].map((step) => progressLabel(step, 4))).toEqual(['20% done', '40% done', '60% done']);
    expect(progressLabel(2, 2)).toBe('67% done');
    expect(progressLabel(4, 4, true)).toBe('Ready to send');
  });
});

describe('checkFor', () => {
  it('says what an email or a web address needs, and that a required field needs an answer', () => {
    expect(checkFor('email', false)).toBe('Enter an email address like name@example.com.');
    expect(checkFor('url', true)).toBe('Enter a web address like https://example.com.');
    expect(checkFor('text', true)).toBe('Add an answer.');
    expect(checkFor('text', false)).toBeNull();
    expect(checkFor('tel', false)).toBeNull();
  });
});
