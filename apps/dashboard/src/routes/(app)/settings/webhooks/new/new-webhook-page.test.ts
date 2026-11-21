import { isRedirect } from '@sveltejs/kit';
import { describe, expect, it } from 'vitest';
import { load } from './+page.js';

// Add endpoint is a sheet over the Webhooks list (design system A16): the old page's address opens the list with it open.
describe('the old Add endpoint address', () => {
  it('sends a visitor to the Webhooks list with the Add endpoint sheet open', () => {
    let location: string | undefined;
    try {
      (load as () => unknown)();
    } catch (thrown) {
      if (!isRedirect(thrown)) throw thrown;
      location = thrown.location;
    }
    expect(location).toBe('/settings/webhooks?add=endpoint');
  });
});
