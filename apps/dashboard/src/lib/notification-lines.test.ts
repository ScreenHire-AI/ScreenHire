import { describe, expect, it } from 'vitest';
import { notificationLine } from './notification-lines.js';

describe('notification lines', () => {
  it('words the sending domain alert from the domain in its payload', () => {
    expect(notificationLine('sending-domain-failing', { domain: 'example.com' })).toBe('example.com stopped verifying');
  });

  it('still reads as a sentence when the payload lacks the fact', () => {
    expect(notificationLine('sending-domain-failing', {})).toBe('Your email domain stopped verifying');
  });

  it('words the AI credit alerts, the balance to the cent from the payload, and never makes a number up when it is missing', () => {
    expect(notificationLine('ai-credits-low', { balance: '4.5' })).toBe('AI credits are running low: $4.50 left');
    expect(notificationLine('ai-credits-low', {})).toBe('AI credits are running low');
    expect(notificationLine('ai-credits-empty', {})).toBe('AI credits ran out: the AI checks are paused');
    expect(notificationLine('ai-credits-reload-failed', {})).toBe('Auto-reload could not add AI credits, so it is off');
  });

  it('retired the budget and cap alerts: a notification of either kind still on file reads as the generic line, never raw', () => {
    expect(notificationLine('ai-usage-budget', { percent: '80' })).toBe('Something needs a look');
    expect(notificationLine('ai-usage-cap', {})).toBe('Something needs a look');
  });

  it('words the AI usage spike alert', () => {
    expect(notificationLine('ai-usage-spike', { day: '2026-11-08' })).toBe('AI usage yesterday was well above the recent average');
  });

  it('shows a kind it does not know as a generic line, never the raw kind', () => {
    expect(notificationLine('something-new', { anything: 'x' })).toBe('Something needs a look');
  });

  describe('the free trial', () => {
    const endsAt = '2026-12-10T12:00:00.000Z';
    const at = (iso: string) => new Date(iso);
    const ending = (now: string) => notificationLine('trial-ending', { endsAt, plan: 'Growth' }, at(now));

    it('counts the calendar days left to the end, so the one notification stays right as the days pass', () => {
      expect(ending('2026-12-03T12:00:00.000Z')).toBe('Your free trial ends in 7 days');
      expect(ending('2026-12-03T18:00:00.000Z')).toBe('Your free trial ends in 7 days');
      expect(ending('2026-12-07T12:00:00.000Z')).toBe('Your free trial ends in 3 days');
      expect(ending('2026-12-09T12:00:00.000Z')).toBe('Your free trial ends tomorrow');
      expect(ending('2026-12-10T06:00:00.000Z')).toBe('Your free trial ends today');
    });

    it('never counts a part of a day as a whole one, so the line agrees with the date the trial ends on', () => {
      // Three days and an hour before the end, early on the 7th: the end is on the 10th, three days away, not four.
      expect(ending('2026-12-07T11:00:00.000Z')).toBe('Your free trial ends in 3 days');
      // A day and an hour before, still the 9th: the end is tomorrow, as the email that comes an hour later says.
      expect(ending('2026-12-09T11:00:00.000Z')).toBe('Your free trial ends tomorrow');
      // The first minute of the 10th: today, though nearly half a day is left.
      expect(ending('2026-12-10T00:00:00.000Z')).toBe('Your free trial ends today');
    });

    it('says the trial ended once the end has passed, even before the server swaps the notification', () => {
      expect(ending('2026-12-10T12:00:01.000Z')).toBe('Your free trial has ended');
      expect(notificationLine('trial-ended', { endsAt, plan: 'Growth' }, at('2026-12-20T00:00:00.000Z'))).toBe(
        'Your free trial has ended',
      );
    });

    it('still reads as a sentence when the payload lacks the end', () => {
      expect(notificationLine('trial-ending', {})).toBe('Your free trial is ending');
    });
  });
});

describe('interview notices', () => {
  it('says what happened to the interview, the next step the team takes included', () => {
    expect(notificationLine('interview-changed', { change: 'Booked' })).toBe('An interview was booked');
    expect(notificationLine('interview-changed', { change: 'TimesSuggested' })).toBe('A candidate sent times to book');
    expect(notificationLine('interview-changed', { change: 'AnotherTimeAsked' })).toBe('A candidate asked for another interview time');
    expect(notificationLine('interview-changed', {})).toBe('An interview changed');
  });
});
