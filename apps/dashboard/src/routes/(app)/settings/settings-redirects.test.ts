import { isRedirect } from '@sveltejs/kit';
import { describe, expect, it } from 'vitest';
import { load as loadIntegrations } from './integrations/+page.js';
import { load as loadIntegrity } from './integrity/+page.js';
import { load as loadVerification } from './verification/+page.js';

// Settings was rearranged (design system section 11): every address that moved lands on its new one.
function locationOf(run: () => unknown): string {
  try {
    run();
  } catch (thrown) {
    if (isRedirect(thrown)) return thrown.location;
    throw thrown;
  }
  throw new Error('The load did not redirect.');
}

describe('old settings addresses', () => {
  it('sends the Integrations directory to the Settings home', () => {
    expect(locationOf(() => (loadIntegrations as () => unknown)())).toBe('/settings');
  });

  it('sends Verification settings, and the older Integrity address, to the Verification area', () => {
    expect(locationOf(() => (loadVerification as () => unknown)())).toBe('/verification');
    expect(locationOf(() => (loadIntegrity as () => unknown)())).toBe('/verification');
  });
});
