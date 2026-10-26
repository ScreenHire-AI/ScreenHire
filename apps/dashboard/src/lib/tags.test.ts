import { describe, expect, it } from 'vitest';
import { matchesTag, tagsOf } from './tags.js';

describe('tags', () => {
  it('lists each tag once, ignoring case, in alphabetical order', () => {
    expect(tagsOf([{ tags: ['referral', 'Rehire'] }, { tags: ['Referral', 'agency'] }, { tags: [] }])).toEqual(['agency', 'referral', 'Rehire']);
  });

  it('matches a tag ignoring case, and everything when no tag is chosen', () => {
    expect(matchesTag({ tags: ['Referral'] }, 'referral')).toBe(true);
    expect(matchesTag({ tags: ['Referral'] }, 'Agency')).toBe(false);
    expect(matchesTag({ tags: [] }, null)).toBe(true);
  });
});
