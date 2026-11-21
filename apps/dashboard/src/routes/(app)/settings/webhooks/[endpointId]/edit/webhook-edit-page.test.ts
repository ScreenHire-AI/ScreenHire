import { isRedirect } from '@sveltejs/kit';
import { describe, expect, it } from 'vitest';
import { load } from './+page.js';

// Editing an endpoint is a sheet over its page (design system A16): the old edit address opens the page with it open.
describe('the old Edit endpoint address', () => {
  it('sends a visitor to the page of that endpoint with the Edit sheet open', () => {
    let location: string | undefined;
    try {
      (load as (event: { params: { endpointId: string } }) => unknown)({ params: { endpointId: 'e1' } });
    } catch (thrown) {
      if (!isRedirect(thrown)) throw thrown;
      location = thrown.location;
    }
    expect(location).toBe('/settings/webhooks/e1?edit=endpoint');
  });
});
