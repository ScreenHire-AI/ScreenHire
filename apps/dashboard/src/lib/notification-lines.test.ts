import { describe, expect, it } from 'vitest';
import { notificationLine } from './notification-lines.js';

describe('notification lines', () => {
  it('words the sending domain alert from the domain in its payload', () => {
    expect(notificationLine('sending-domain-failing', { domain: 'example.com' })).toBe('example.com stopped verifying');
  });

  it('still reads as a sentence when the payload lacks the fact', () => {
    expect(notificationLine('sending-domain-failing', {})).toBe('Your email domain stopped verifying');
  });

  it('shows a kind it does not know as a generic line, never the raw kind', () => {
    expect(notificationLine('something-new', { anything: 'x' })).toBe('Something needs a look');
  });

  describe('the free trial', () => {
    const endsAt = '2026-12-10T12:00:00.000Z';
    const at = (iso: string) => new Date(iso);
    const ending = (now: string) => notificationLine('trial-ending', { endsAt, plan: 'Growth' }, at(now));

    it('counts the days left from the end, so the one notification stays right as the days pass', () => {
      expect(ending('2026-12-03T12:00:00.000Z')).toBe('Your free trial ends in 7 days. Choose a plan to keep opening jobs');
      expect(ending('2026-12-03T18:00:00.000Z')).toBe('Your free trial ends in 7 days. Choose a plan to keep opening jobs');
      expect(ending('2026-12-07T12:00:00.000Z')).toBe('Your free trial ends in 3 days. Choose a plan to keep opening jobs');
      expect(ending('2026-12-09T12:00:00.000Z')).toBe('Your free trial ends tomorrow. Choose a plan to keep opening jobs');
      expect(ending('2026-12-10T06:00:00.000Z')).toBe('Your free trial ends in less than a day. Choose a plan to keep opening jobs');
    });

    it('says the trial ended once the end has passed, even before the server swaps the notification', () => {
      expect(ending('2026-12-10T12:00:01.000Z')).toBe('Your free trial has ended. Choose a plan to open new jobs');
      expect(notificationLine('trial-ended', { endsAt, plan: 'Growth' }, at('2026-12-20T00:00:00.000Z'))).toBe(
        'Your free trial has ended. Choose a plan to open new jobs',
      );
    });

    it('still reads as a sentence when the payload lacks the end', () => {
      expect(notificationLine('trial-ending', {})).toBe('Your free trial is ending. Choose a plan to keep opening jobs');
    });
  });
});
