import { describe, expect, it } from 'vitest';
import { interviewer, user, userAs } from '../test-support/fixtures.js';
import { canEditVerification, canSeeUsage, tabOfPath, tabsFor, verificationTabs } from './verification-area.js';

describe('the Verification area tabs', () => {
  it('lists the seven tabs in order, with the Overview at the area address', () => {
    expect(verificationTabs.map((tab) => [tab.label, tab.href])).toEqual([
      ['Overview', '/verification'],
      ['Activity log', '/verification/activity'],
      ['Usage', '/verification/usage'],
      ['Checks', '/verification/checks'],
      ['Policy', '/verification/policy'],
      ['AI model', '/verification/ai-model'],
      ['Notifications', '/verification/notifications'],
    ]);
  });

  it('finds the tab an address belongs to, with or without a trailing slash or a deeper path', () => {
    expect(tabOfPath('/verification').value).toBe('overview');
    expect(tabOfPath('/verification/').value).toBe('overview');
    expect(tabOfPath('/verification/usage').value).toBe('usage');
    expect(tabOfPath('/verification/ai-model/').value).toBe('ai-model');
    expect(tabOfPath('/verification/unknown').value).toBe('overview');
  });

  it('marks the four tabs that show the saved settings', () => {
    expect(verificationTabs.filter((tab) => tab.settings).map((tab) => tab.value)).toEqual(['checks', 'policy', 'ai-model', 'notifications']);
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
    expect(tabsFor(null).map((tab) => tab.value)).toEqual(['overview', 'activity', 'checks', 'policy']);
  });
});
