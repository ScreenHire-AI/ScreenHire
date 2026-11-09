import type { CurrentUser } from '@screenhire/api-client';
import { isRedirect } from '@sveltejs/kit';
import { describe, expect, it } from 'vitest';
import { interviewer, user, userAs } from '../../../test-support/fixtures.js';
import { load as loadAiModel } from './ai-model/+page.js';
import { load as loadNotifications } from './notifications/+page.js';
import { load as loadUsage } from './usage/+page.js';

// A tab a role cannot use is left out of the strip, so its address sends them to the Overview instead of a refusal (A3).
async function locationOf(load: unknown, signedIn: CurrentUser): Promise<string | null> {
  try {
    await (load as (event: unknown) => Promise<unknown>)({ parent: async () => ({ user: signedIn }) });
  } catch (thrown) {
    if (isRedirect(thrown)) return thrown.location;
    throw thrown;
  }
  return null;
}

const withoutAllJobs = (signedIn: CurrentUser): CurrentUser => ({ ...signedIn, organization: { ...signedIn.organization!, seesAllJobs: false } });

describe('the addresses of tabs a role may not use', () => {
  it.each([
    ['Usage', loadUsage],
    ['AI model', loadAiModel],
    ['Notifications', loadNotifications],
  ] as const)('opens %s for an Owner and an Admin', async (_name, load) => {
    expect(await locationOf(load, user)).toBeNull();
    expect(await locationOf(load, userAs('Admin'))).toBeNull();
  });

  it.each([
    ['Usage', loadUsage],
    ['AI model', loadAiModel],
    ['Notifications', loadNotifications],
  ] as const)('sends a Recruiter, a Viewer and a custom role without the permission from %s to the Overview', async (_name, load) => {
    for (const signedIn of [userAs('Recruiter'), userAs('Viewer'), interviewer]) {
      expect(await locationOf(load, signedIn)).toBe('/verification');
    }
  });

  it('sends an Admin who sees only their jobs from Usage, which covers every job, but not from the model tab', async () => {
    const admin = withoutAllJobs(userAs('Admin'));

    expect(await locationOf(loadUsage, admin)).toBe('/verification');
    expect(await locationOf(loadAiModel, admin)).toBeNull();
  });
});
