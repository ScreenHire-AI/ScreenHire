import { describe, expect, it } from 'vitest';
import { notificationLine } from './notification-lines.js';

describe('notification lines', () => {
  it('words the sending domain alert from the domain in its payload', () => {
    expect(notificationLine('sending-domain-failing', { domain: 'example.com' })).toBe('example.com stopped verifying');
  });

  it('still reads as a sentence when the payload lacks the fact', () => {
    expect(notificationLine('sending-domain-failing', {})).toBe('Your email domain stopped verifying');
  });

  it('words the AI usage alerts from their facts, and never makes a number up when the fact is missing', () => {
    expect(notificationLine('ai-usage-budget', { percent: '80' })).toBe("AI usage is at 80% of this month's budget");
    expect(notificationLine('ai-usage-cap', {})).toBe("AI checks are paused: this month's cap is reached");
    expect(notificationLine('ai-usage-spike', { day: '2026-11-08' })).toBe('AI usage yesterday was well above the recent average');
  });

  it('shows a kind it does not know as a generic line, never the raw kind', () => {
    expect(notificationLine('something-new', { anything: 'x' })).toBe('Something needs a look');
  });
});
