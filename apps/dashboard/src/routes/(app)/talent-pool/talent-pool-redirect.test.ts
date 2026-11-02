import { isRedirect } from '@sveltejs/kit';
import { describe, expect, it } from 'vitest';
import { load } from './+page.js';

describe('the old talent pool address', () => {
  it('redirects to Saved candidates, which the talent pool became', () => {
    let thrown: unknown;
    try {
      void load({} as Parameters<typeof load>[0]);
    } catch (error) {
      thrown = error;
    }

    expect(isRedirect(thrown)).toBe(true);
    expect(thrown).toMatchObject({ status: 307, location: '/saved' });
  });
});
