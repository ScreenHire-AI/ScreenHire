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
});
