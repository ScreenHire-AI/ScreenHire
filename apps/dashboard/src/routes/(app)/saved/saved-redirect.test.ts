import { isRedirect } from '@sveltejs/kit';
import { describe, expect, it } from 'vitest';
import { load } from './+page.js';

const loadAt = (address: string) => {
  try {
    void load({ url: new URL(address) } as Parameters<typeof load>[0]);
  } catch (error) {
    return error;
  }

  return undefined;
};

describe('the old Saved candidates address', () => {
  it('redirects to the Saved list on Candidates, which Saved candidates became', () => {
    const thrown = loadAt('http://localhost/saved');

    expect(isRedirect(thrown)).toBe(true);
    expect(thrown).toMatchObject({ status: 307, location: '/candidates?list=saved' });
  });

  it('keeps the query of an old link', () => {
    expect(loadAt('http://localhost/saved?q=casey')).toMatchObject({ location: '/candidates?q=casey&list=saved' });
  });
});
