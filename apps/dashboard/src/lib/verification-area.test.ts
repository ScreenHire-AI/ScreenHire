import { describe, expect, it } from 'vitest';
import { interviewer, user, userAs } from '../test-support/fixtures.js';
import { canEditVerification, canSeeUsage, movedTabs, tabOfPath, tabsFor, verificationTabs } from './verification-area.js';

describe('the Verification area tabs', () => {
  it('lists the three tabs in order, with the Overview at the area address', () => {
    expect(verificationTabs.map((tab) => [tab.label, tab.href])).toEqual([
      ['Overview', '/verification'],
      ['Activity log', '/verification/activity'],
      ['Checks', '/verification/checks'],
    ]);
  });

  it('finds the tab an address belongs to, with or without a trailing slash or a deeper path', () => {
    expect(tabOfPath('/verification').value).toBe('overview');
    expect(tabOfPath('/verification/').value).toBe('overview');
    expect(tabOfPath('/verification/activity').value).toBe('activity');
    expect(tabOfPath('/verification/checks/').value).toBe('checks');
    expect(tabOfPath('/verification/unknown').value).toBe('overview');
  });

  it('marks the one tab that shows the saved settings', () => {
    expect(verificationTabs.filter((tab) => tab.settings).map((tab) => tab.value)).toEqual(['checks']);
  });

  it("sends each moved tab to the page that holds what it held, for the person's role", () => {
    const ownJobsAdmin = { ...userAs('Admin'), organization: { ...userAs('Admin').organization!, seesAllJobs: false } };
    const where = (from: keyof typeof movedTabs) => [user, ownJobsAdmin, userAs('Recruiter')].map((signedIn) => movedTabs[from](signedIn));
    expect(where('/verification/policy')).toEqual(['/verification/checks', '/verification/checks', '/verification/checks']);
    expect(where('/verification/notifications')).toEqual(['/settings/notification-defaults', '/settings/notification-defaults', '/account/notifications']);
    expect(where('/verification/ai-use')).toEqual(['/settings/billing#ai-use', '/settings/ai-model', '/verification']);
    expect(where('/verification/usage')).toEqual(['/settings/billing#ai-use', '/settings/ai-model', '/verification']);
    expect(where('/verification/ai-model')).toEqual(['/settings/ai-model', '/settings/ai-model', '/verification']);
    // Someone who sees usage but may not change the key goes to the usage.
    const reader = { ...interviewer, organization: { ...interviewer.organization!, permissions: [...interviewer.organization!.permissions, 'ViewAiUsage' as const], seesAllJobs: true } };
    expect(movedTabs['/verification/ai-model'](reader)).toBe('/settings/billing#ai-use');
  });

  it('needs View AI usage and All jobs for usage, and Edit settings to edit', () => {
    expect(canSeeUsage(user)).toBe(true);
    expect(canSeeUsage(userAs('Admin'))).toBe(true);
    expect(canSeeUsage(userAs('Recruiter'))).toBe(false);
    expect(canSeeUsage({ ...user, organization: { ...user.organization!, seesAllJobs: false } })).toBe(false);
    expect(canSeeUsage(null)).toBe(false);
    expect(canEditVerification(userAs('Admin'))).toBe(true);
    expect(canEditVerification(interviewer)).toBe(false);
  });

  it('gives a signed-out person only what needs no permission', () => {
    expect(tabsFor(null).map((tab) => tab.value)).toEqual(['overview', 'activity', 'checks']);
  });
});
