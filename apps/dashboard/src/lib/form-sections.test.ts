import { describe, expect, it } from 'vitest';
import { addSection, fromRows, removeSection, rowsOf, syncSections, type SectionDraft } from './form-sections.js';

type Item = { id: string };
const key = (item: Item) => item.id;
const items = ['a', 'b', 'c', 'd'].map((id) => ({ id }));
const sections: SectionDraft[] = [
  { id: 'one', name: 'One', questionIds: ['a', 'b'] },
  { id: 'two', name: 'Two', questionIds: ['c', 'd'] },
];

describe('form sections', () => {
  it('lists each section before its questions, or just the questions without sections', () => {
    expect(rowsOf(items, sections, key).map((row) => (row.kind === 'section' ? `#${row.section.id}` : row.item.id))).toEqual([
      '#one',
      'a',
      'b',
      '#two',
      'c',
      'd',
    ]);
    expect(rowsOf(items, null, key).every((row) => row.kind === 'question')).toBe(true);
  });

  it('reads the sections back from a new row order, the flat list following them', () => {
    const rows = rowsOf(items, sections, key);
    // The second section's row moved up a place: b now starts it.
    const moved = [rows[0]!, rows[1]!, rows[3]!, rows[2]!, rows[4]!, rows[5]!];

    expect(fromRows(moved, key).sections?.map((section) => section.questionIds)).toEqual([['a'], ['b', 'c', 'd']]);
  });

  it('puts a question dragged above the first section row into the first section', () => {
    const rows = rowsOf(items, sections, key);
    const moved = [rows[1]!, rows[0]!, ...rows.slice(2)];

    expect(fromRows(moved, key)).toEqual({ items, sections });
  });

  it('keeps new questions beside the one before them and drops deleted ones', () => {
    const next = [{ id: 'a' }, { id: 'c' }, { id: 'c_2' }, { id: 'd' }, { id: 'e' }];

    const synced = syncSections(next, sections, key);

    expect(synced.sections.map((section) => section.questionIds)).toEqual([['a'], ['c', 'c_2', 'd', 'e']]);
    expect(synced.items.map(key)).toEqual(['a', 'c', 'c_2', 'd', 'e']);
  });

  it('adds sections at the end, the first holding every question', () => {
    expect(addSection(null, ['a', 'b']).map((section) => [section.id, section.questionIds])).toEqual([
      ['section_1', ['a', 'b']],
      ['section_2', []],
    ]);
    expect(addSection(sections, []).at(-1)).toEqual({ id: 'section_3', name: 'Section 3', questionIds: [] });
  });

  it('gives a deleted section its questions to the one before, or after for the first, and none back to automatic pages', () => {
    expect(removeSection(sections, 1)).toEqual([{ id: 'one', name: 'One', questionIds: ['a', 'b', 'c', 'd'] }]);
    expect(removeSection(sections, 0)).toEqual([{ id: 'two', name: 'Two', questionIds: ['a', 'b', 'c', 'd'] }]);
    expect(removeSection([sections[0]!], 0)).toBeNull();
  });
});
