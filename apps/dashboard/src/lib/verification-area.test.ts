import { describe, expect, it } from 'vitest';
import { interviewer, user, userAs } from '../test-support/fixtures.js';
import { canEditVerification, canOpenAiUse, canSeeUsage, movedTabs, tabOfPath, tabsFor, verificationTabs } from './verification-area.js';

describe('the Verification area tabs', () => {
  it('lists the four tabs in order, with the Overview at the area address', () => {
    expect(verificationTabs.map((tab) => [tab.label, tab.href])).toEqual([
      ['Overview', '/verification'],
      ['Activity log', '/verification/activity'],
      ['Checks', '/verification/checks'],
      ['AI use', '/verification/ai-use'],
    ]);
  });

  it('finds the tab an address belongs to, with or without a trailing slash or a deeper path', () => {
    expect(tabOfPath('/verification').value).toBe('overview');
    expect(tabOfPath('/verification/').value).toBe('overview');
    expect(tabOfPath('/verification/ai-use').value).toBe('ai-use');
    expect(tabOfPath('/verification/checks/').value).toBe('checks');
    expect(tabOfPath('/verification/unknown').value).toBe('overview');
  });

  it('marks the two tabs that show the saved settings', () => {
    expect(verificationTabs.filter((tab) => tab.settings).map((tab) => tab.value)).toEqual(['checks', 'ai-use']);
  });

  it('sends each merged tab to the tab that holds its settings now, and only to a tab that exists', () => {
    expect(movedTabs).toEqual({
      '/verification/policy': '/verification/checks',
      '/verification/notifications': '/verification/checks',
      '/verification/usage': '/verification/ai-use',
      '/verification/ai-model': '/verification/ai-use',
    });
    for (const target of Object.values(movedTabs)) expect(verificationTabs.some((tab) => tab.href === target)).toBe(true);
  });

  it('needs View AI usage and All jobs for usage, Edit settings to edit, and either for the AI use tab', () => {
    expect(canSeeUsage(user)).toBe(true);
    expect(canSeeUsage(userAs('Admin'))).toBe(true);
    expect(canSeeUsage(userAs('Recruiter'))).toBe(false);
    expect(canSeeUsage({ ...user, organization: { ...user.organization!, seesAllJobs: false } })).toBe(false);
    expect(canSeeUsage(null)).toBe(false);
    expect(canEditVerification(userAs('Admin'))).toBe(true);
    expect(canEditVerification(interviewer)).toBe(false);
    expect(canOpenAiUse(userAs('Recruiter'))).toBe(false);
    expect(canOpenAiUse({ ...userAs('Admin'), organization: { ...userAs('Admin').organization!, seesAllJobs: false } })).toBe(true);
  });

  it('gives a signed-out person only what needs no permission', () => {
    expect(tabsFor(null).map((tab) => tab.value)).toEqual(['overview', 'activity', 'checks']);
  });
});
