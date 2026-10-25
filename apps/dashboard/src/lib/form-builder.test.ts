import { describe, expect, it } from 'vitest';
import { applyFormKinds, duplicateId, followUpKinds, insertAt, kindIn } from './form-builder.js';

describe('answer kinds', () => {
  it('offers every question type on the apply form, General first, and no File in a follow-up', () => {
    expect(applyFormKinds.map((group) => [group.label, group.kinds.map((kind) => kind.label)])).toEqual([
      ['General', ['Yes or no', 'Choice', 'Multi-choice', 'Short text', 'Long text']],
      ['More', ['Number', 'Link', 'File']],
    ]);
    expect(followUpKinds.flatMap((group) => group.kinds.map((kind) => kind.value))).toEqual(['YesNo', 'SingleChoice', 'MultiChoice', 'ShortText', 'LongText', 'Number', 'Url']);
    expect(kindIn(applyFormKinds, 'SingleChoice')).toMatchObject({ options: 'circle' });
    expect(kindIn(applyFormKinds, 'MultiChoice')).toMatchObject({ options: 'square' });
    expect(kindIn(followUpKinds, 'File')).toBeUndefined();
  });
});

describe('insertAt', () => {
  it('puts the item at the index, after the one before it', () => {
    expect(insertAt(['a', 'b', 'c'], 1, 'x')).toEqual(['a', 'x', 'b', 'c']);
    expect(insertAt(['a'], 1, 'x')).toEqual(['a', 'x']);
  });
});

describe('duplicateId', () => {
  it('names a copy after its original, unique among the ids taken', () => {
    expect(duplicateId('notice', ['notice'])).toBe('notice_copy');
    expect(duplicateId('notice', ['notice', 'notice_copy'])).toBe('notice_copy_2');
    // A copy of a copy is another copy of the original, not notice_copy_copy.
    expect(duplicateId('notice_copy', ['notice', 'notice_copy'])).toBe('notice_copy_2');
  });

  it('stays within the 40 characters a question id may have', () => {
    const long = 'a'.repeat(40);
    expect(duplicateId(long, [long]).length).toBeLessThanOrEqual(40);
    expect(duplicateId(long, [long, `${'a'.repeat(30)}_copy`]).length).toBeLessThanOrEqual(40);
  });
});
