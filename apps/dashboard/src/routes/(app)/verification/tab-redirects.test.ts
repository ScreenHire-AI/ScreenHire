import type { CurrentUser } from '@screenhire/api-client';
import { isRedirect } from '@sveltejs/kit';
import { describe, expect, it } from 'vitest';
import { interviewer, user, userAs } from '../../../test-support/fixtures.js';
import { load as loadAiModel } from './ai-model/+page.js';
import { load as loadAiUse } from './ai-use/+page.js';
import { load as loadNotifications } from './notifications/+page.js';
import { load as loadPolicy } from './policy/+page.js';
import { load as loadUsage } from './usage/+page.js';

// Bell notifications, alert emails, Slack messages and bookmarks still link to tabs that moved. Each address sends the person
// to the page that now holds what it held, for their role, with the query string, and the target's hash after it.
async function locationOf(load: unknown, event: Record<string, unknown>): Promise<string | null> {
  try {
    await (load as (event: unknown) => Promise<unknown>)(event);
  } catch (thrown) {
    if (isRedirect(thrown)) return thrown.location;
    throw thrown;
  }
  return null;
}

const at = (path: string, signedIn: CurrentUser) => ({ url: new URL(`http://dashboard.test${path}`), parent: async () => ({ user: signedIn }) });
const ownJobsAdmin = { ...userAs('Admin'), organization: { ...userAs('Admin').organization!, seesAllJobs: false } };

describe('the addresses of the tabs that moved', () => {
  it.each([
    ['/verification/ai-use', loadAiUse, '/settings/billing', '#ai-use'],
    ['/verification/usage', loadUsage, '/settings/billing', '#ai-use'],
    ['/verification/ai-model', loadAiModel, '/settings/ai-model', ''],
    ['/verification/notifications', loadNotifications, '/settings/notification-defaults', ''],
    ['/verification/policy', loadPolicy, '/verification/checks', ''],
  ] as const)('sends an Owner from %s to its new home, keeping the query string before the hash', async (path, load, target, hash) => {
    expect(await locationOf(load, at(path, user))).toBe(`${target}${hash}`);
    expect(await locationOf(load, at(`${path}?from=email`, user))).toBe(`${target}?from=email${hash}`);
  });

  it('sends an Admin who sees only their jobs, who cannot see usage, to the AI model from the AI use addresses', async () => {
    for (const [path, load] of [
      ['/verification/ai-use', loadAiUse],
      ['/verification/usage', loadUsage],
      ['/verification/ai-model', loadAiModel],
    ] as const) {
      expect(await locationOf(load, at(path, ownJobsAdmin))).toBe('/settings/ai-model');
    }
  });

  it('sends a role with neither usage nor settings to the Overview, and to their own notifications', async () => {
    for (const signedIn of [userAs('Recruiter'), userAs('Viewer'), interviewer]) {
      expect(await locationOf(loadAiUse, at('/verification/ai-use', signedIn))).toBe('/verification');
      expect(await locationOf(loadAiModel, at('/verification/ai-model', signedIn))).toBe('/verification');
      expect(await locationOf(loadNotifications, at('/verification/notifications', signedIn))).toBe('/account/notifications');
    }
  });
});
