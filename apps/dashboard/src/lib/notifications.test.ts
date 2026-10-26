import { describe, expect, it } from 'vitest';
import { maxNotifications, mentionNotification, mentionNotificationId, Notifications, teamNotification } from './notifications.svelte.js';

const application = { id: 'app-1', candidate: { fullName: 'Casey Morgan' }, jobTitle: 'Senior .NET Engineer' };

describe('notifications', () => {
  it("words a NotifyTeam run with the action's note, linking to the application", () => {
    const run = {
      automationName: 'Flag referrals',
      actions: [{ index: 0, action: { type: 'NotifyTeam' as const, note: ' A referral arrived. ' }, status: 'Completed' as const, detail: null, finishedAt: null }],
    };
    expect(teamNotification('run-1', run, application)).toEqual({
      id: 'run-1',
      title: 'Casey Morgan, Senior .NET Engineer',
      detail: 'A referral arrived.',
      href: '/applications/app-1',
    });
  });

  it("falls back to the automation's name, or a plain sentence when the run cannot be read", () => {
    expect(teamNotification('run-1', { automationName: 'Flag referrals', actions: [] }, application).detail).toBe(
      'The automation "Flag referrals" notified the team.',
    );
    expect(teamNotification('run-1', undefined, application).detail).toBe('An automation notified the team.');
  });

  it('shows each run once, newest first, and keeps only the latest few until dismissed', () => {
    const tray = new Notifications();
    for (let index = 0; index < maxNotifications + 1; index++) tray.add({ id: `run-${index}`, title: '', detail: '', href: '' });
    tray.add({ id: 'run-3', title: '', detail: '', href: '' });

    expect(tray.items.map((item) => item.id)).toEqual(['run-3', 'run-2', 'run-1']);
    tray.dismiss('run-2');
    expect(tray.items.map((item) => item.id)).toEqual(['run-3', 'run-1']);
  });

  it("keys a mention's card by its note, so deleting the note can take the card away", () => {
    const tray = new Notifications();
    tray.add(mentionNotification('note-1', application, 'Jordan Ellis'));
    tray.add(teamNotification('run-1', undefined, application));

    tray.dismiss(mentionNotificationId('note-1'));

    expect(tray.items.map((item) => item.id)).toEqual(['run-1']);
  });
});
