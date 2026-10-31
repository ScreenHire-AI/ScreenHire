import { answeredCount, questionPages, type QuestionSpec } from '@screenhire/ui/questions/model';
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

describe('answeredCount', () => {
  it('counts text, choices and files that have an answer', () => {
    const questions = [question('name'), question('shifts', 'MultiChoice'), question('resume', 'File'), question('blank')];

    expect(answeredCount(questions, { name: 'Casey', shifts: ['a'], blank: '  ' }, { resume: { uploadId: 'x' } })).toBe(3);
    expect(answeredCount(questions, { shifts: [] })).toBe(0);
  });
});
