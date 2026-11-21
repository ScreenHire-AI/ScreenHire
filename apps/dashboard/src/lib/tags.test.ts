import { describe, expect, it } from 'vitest';
import { matchesTag, suggestTags, tagsOf } from './tags.js';

describe('tags', () => {
  it('lists each tag once, ignoring case, in alphabetical order', () => {
    expect(tagsOf([{ tags: ['referral', 'Rehire'] }, { tags: ['Referral', 'agency'] }, { tags: [] }])).toEqual(['agency', 'referral', 'Rehire']);
  });

  it('matches a tag ignoring case, and everything when no tag is chosen', () => {
    expect(matchesTag({ tags: ['Referral'] }, 'referral')).toBe(true);
    expect(matchesTag({ tags: ['Referral'] }, 'Agency')).toBe(false);
    expect(matchesTag({ tags: [] }, null)).toBe(true);
  });

  describe('suggestions while typing', () => {
    const known = [
      { name: 'Senior engineer', applications: 9 },
      { name: 'Senior', applications: 4 },
      { name: 'Remote', applications: 7 },
      { name: 'Sr', applications: 1 },
      { name: 'Chosen', applications: 12 },
    ];

    it('matches ignoring case, most used first, and offers Create for a new name last', () => {
      expect(suggestTags(known, 'SE', [])).toEqual({ matches: ['Chosen', 'Senior engineer', 'Senior'], create: 'SE' });
    });

    it('puts the tag the text names in full first and offers no Create, so another case reuses it', () => {
      expect(suggestTags(known, ' senior ', [])).toEqual({ matches: ['Senior', 'Senior engineer'], create: null });
    });

    it('leaves out the tags the record has, and offers nothing new for one of them', () => {
      expect(suggestTags(known, 'sen', ['senior'])).toEqual({ matches: ['Chosen', 'Senior engineer'], create: 'sen' });
      expect(suggestTags(known, 'SENIOR', ['Senior'])).toEqual({ matches: ['Senior engineer'], create: null });
    });

    it('offers the most used tags before anything is typed, up to the limit', () => {
      expect(suggestTags(known, '', ['Remote'], 2)).toEqual({ matches: ['Chosen', 'Senior engineer'], create: null });
    });
  });
});
