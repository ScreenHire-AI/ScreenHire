import type { CurrentUser } from '@screenhire/api-client';
import { isRedirect } from '@sveltejs/kit';
import { describe, expect, it } from 'vitest';
import { interviewer, user, userAs } from '../../../test-support/fixtures.js';
import { load as loadAiModel } from './ai-model/+page.js';
import { load as loadAiUse } from './ai-use/+page.js';
import { load as loadNotifications } from './notifications/+page.js';
import { load as loadPolicy } from './policy/+page.js';
import { load as loadUsage } from './usage/+page.js';

// A tab a role cannot use is left out of the strip, so its address sends them to the Overview instead of a refusal (A3).
async function locationOf(load: unknown, event: Record<string, unknown>): Promise<string | null> {
  try {
    await (load as (event: unknown) => Promise<unknown>)(event);
  } catch (thrown) {
    if (isRedirect(thrown)) return thrown.location;
    throw thrown;
  }
  return null;
}

const signedInAs = (signedIn: CurrentUser) => ({ parent: async () => ({ user: signedIn }) });
const at = (path: string) => ({ url: new URL(`http://dashboard.test${path}`) });

describe('the addresses of the tabs that merged', () => {
  it.each([
    ['/verification/policy', loadPolicy, '/verification/checks'],
    ['/verification/notifications', loadNotifications, '/verification/checks'],
    ['/verification/usage', loadUsage, '/verification/ai-use'],
    ['/verification/ai-model', loadAiModel, '/verification/ai-use'],
  ] as const)('sends %s to the tab that holds its settings now, with the query string', async (path, load, target) => {
    expect(await locationOf(load, at(path))).toBe(target);
    expect(await locationOf(load, at(`${path}?from=email`))).toBe(`${target}?from=email`);
  });
});

describe('the AI use tab', () => {
  it('opens for an Owner, an Admin, and an Admin who sees only their jobs, for the key and the alert', async () => {
    expect(await locationOf(loadAiUse, signedInAs(user))).toBeNull();
    expect(await locationOf(loadAiUse, signedInAs(userAs('Admin')))).toBeNull();
    const admin = userAs('Admin');
    expect(await locationOf(loadAiUse, signedInAs({ ...admin, organization: { ...admin.organization!, seesAllJobs: false } }))).toBeNull();
  });

  it('sends a Recruiter, a Viewer and a custom role without the permission to the Overview', async () => {
    for (const signedIn of [userAs('Recruiter'), userAs('Viewer'), interviewer]) {
      expect(await locationOf(loadAiUse, signedInAs(signedIn))).toBe('/verification');
    }
  });
});
