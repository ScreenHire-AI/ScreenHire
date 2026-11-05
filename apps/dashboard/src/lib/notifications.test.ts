import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { builtInPermissions, userAs } from '../test-support/fixtures.js';
import {
  dataRequestNotification,
  dataRequestNotificationId,
  maxNotifications,
  mentionNotification,
  mentionNotificationId,
  Notifications,
  replyNotification,
  replyNotificationId,
  teamNotification,
  toastLifetime,
} from './notifications.svelte.js';
import type { Toast } from './teammate-changes.js';

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

describe('reply notifications', () => {
  it('words a reply to your email, linking to Messages, and never carries the reply itself', () => {
    const card = replyNotification('reply-1', application, true);

    expect(card).toEqual({
      id: replyNotificationId('reply-1'),
      kind: 'reply',
      title: 'Casey Morgan, Senior .NET Engineer',
      detail: 'Replied to your email.',
      href: '/applications/app-1?tab=messages',
    });
  });

  it("says an automation's email was answered when nobody in particular sent it", () => {
    expect(replyNotification('reply-1', application, false).detail).toBe('Replied to an automated email.');
  });

  it('keys the card by the reply, so the same event shows one card', () => {
    const tray = new Notifications();
    tray.add(replyNotification('reply-1', application, true));
    tray.add(replyNotification('reply-1', application, true));

    expect(tray.items.map((item) => item.id)).toEqual(['reply:reply-1']);
  });
});

describe('data request notifications', () => {
  it('words a request for a copy and one for erasure, linking to the Data requests page, and names no one', () => {
    expect(dataRequestNotification(userAs('Owner'), 'req-1', 'Copy')).toEqual({
      id: dataRequestNotificationId('req-1'),
      kind: 'dataRequest',
      title: 'New data request',
      detail: 'A candidate asked for a copy of their data.',
      href: '/settings/data-requests',
    });
    expect(dataRequestNotification(userAs('Admin'), 'req-2', 'Erasure')?.detail).toBe('A candidate asked to be erased.');
  });

  it('shows no card to a role that cannot open the page, or to someone who sees only their own jobs', () => {
    const admin = userAs('Admin');
    const scoped = { ...admin, organization: { ...admin.organization!, seesAllJobs: false } };

    expect(dataRequestNotification(userAs('Recruiter'), 'req-1', 'Copy')).toBeUndefined();
    expect(dataRequestNotification(userAs('Viewer'), 'req-1', 'Copy')).toBeUndefined();
    expect(dataRequestNotification(scoped, 'req-1', 'Copy')).toBeUndefined();
    expect(dataRequestNotification(null, 'req-1', 'Copy')).toBeUndefined();
    expect(builtInPermissions.Admin).toContain('EraseCandidates');
  });

  it('keys the card by the request, so a redelivered event shows one card', () => {
    const tray = new Notifications();
    tray.add(dataRequestNotification(userAs('Owner'), 'req-1', 'Copy')!);
    tray.add(dataRequestNotification(userAs('Owner'), 'req-1', 'Copy')!);

    expect(tray.items.map((item) => item.id)).toEqual(['data-request:req-1']);
  });
});

describe('toasts in the tray', () => {
  const toast = (id: string): Toast => ({ id, kind: 'Moves', sentence: ['Jordan Ellis moved ', 'Taylor Brook', ' to Interview'], href: '/applications/a', context: '' });
  const card = (id: string) => ({ id, title: '', detail: '', href: '' });
  const ids = (tray: Notifications) => tray.items.map((item) => item.id);

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('closes a toast after its lifetime, and never a card', () => {
    const tray = new Notifications({ covered: () => false });
    tray.add(card('card'));
    tray.toast(toast('toast'));

    vi.advanceTimersByTime(toastLifetime - 500);
    expect(ids(tray)).toEqual(['toast', 'card']);
    vi.advanceTimersByTime(500);
    expect(ids(tray)).toEqual(['card']);
    tray.dispose();
  });

  it('shares the limit of three with the cards: a new one pushes out the oldest toast before any card', () => {
    const tray = new Notifications({ covered: () => false });
    tray.add(card('card-1'));
    tray.toast(toast('toast-1'));
    tray.add(card('card-2'));
    tray.toast(toast('toast-2'));

    expect(ids(tray)).toEqual(['toast-2', 'card-2', 'card-1']);
    tray.add(card('card-3'));
    expect(ids(tray)).toEqual(['card-3', 'card-2', 'card-1']);
    tray.toast(toast('toast-3'));
    expect(ids(tray)).toEqual(['card-3', 'card-2', 'card-1']);
    expect(maxNotifications).toBe(3);
    tray.dispose();
  });

  it('holds the countdown while the pointer or the focus is in the tray, or something covers it', () => {
    let covered = false;
    const tray = new Notifications({ covered: () => covered });
    tray.toast(toast('toast'));

    tray.hold('pointer', true);
    vi.advanceTimersByTime(toastLifetime * 2);
    tray.hold('pointer', false);
    tray.hold('focus', true);
    vi.advanceTimersByTime(toastLifetime * 2);
    tray.hold('focus', false);
    covered = true;
    vi.advanceTimersByTime(toastLifetime * 2);
    expect(ids(tray)).toEqual(['toast']);

    covered = false;
    vi.advanceTimersByTime(toastLifetime);
    expect(ids(tray)).toEqual([]);
  });

  it('keeps toasts until dismissed when the person turned Close automatically off', () => {
    const tray = new Notifications({ covered: () => false, closeToasts: () => false });
    tray.toast(toast('toast'));
    vi.advanceTimersByTime(toastLifetime * 3);
    expect(ids(tray)).toEqual(['toast']);
    tray.dismiss('toast');
    expect(ids(tray)).toEqual([]);
  });
});
