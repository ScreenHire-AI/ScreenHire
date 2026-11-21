import { describe, expect, it } from 'vitest';
import { DEFAULT_LOCALE, firstTag, localeOf, longDate, pickLocale } from './locale';

describe('pickLocale', () => {
  it('takes the first language the runtime supports', () => {
    expect(pickLocale('en-US')).toBe('en-US');
    expect(pickLocale('en-GB')).toBe('en-GB');
    expect(pickLocale('de')).toBe('de');
    expect(pickLocale('de-DE,de;q=0.9,en;q=0.8')).toBe('de-DE');
  });

  it('prefers the higher q, and keeps the order sent for equal ones', () => {
    expect(pickLocale('en;q=0.5, de;q=0.9')).toBe('de');
    expect(pickLocale('fr, de')).toBe('fr');
    expect(pickLocale('de;q=0, en-US')).toBe('en-US');
  });

  it('skips a tag the runtime does not know and moves on', () => {
    expect(pickLocale('xx-ZZ-bogus, de')).toBe('de');
  });

  it('reads as en-GB when nothing usable is named', () => {
    expect(DEFAULT_LOCALE).toBe('en-GB');
    expect(pickLocale(null)).toBe('en-GB');
    expect(pickLocale(undefined)).toBe('en-GB');
    expect(pickLocale('')).toBe('en-GB');
    expect(pickLocale('*')).toBe('en-GB');
    expect(pickLocale('not a language tag at all')).toBe('en-GB');
    expect(pickLocale('x'.repeat(600))).toBe('en-GB');
  });

  it("reads it from a request's Accept-Language", () => {
    expect(localeOf(new Request('https://careers.example.com/', { headers: { 'accept-language': 'en-US,en;q=0.9' } }))).toBe('en-US');
    expect(localeOf(new Request('https://careers.example.com/'))).toBe('en-GB');
  });
});

describe('firstTag', () => {
  it('is the first tag of the header when it has the shape of a language tag', () => {
    expect(firstTag('de-DE,de;q=0.9,en;q=0.8')).toBe('de-DE');
    expect(firstTag(' en-US ;q=1')).toBe('en-US');
    expect(firstTag('zh-Hant-TW')).toBe('zh-Hant-TW');
  });

  it('is null for none, a wildcard or junk', () => {
    expect(firstTag(null)).toBeNull();
    expect(firstTag('')).toBeNull();
    expect(firstTag('*')).toBeNull();
    expect(firstTag('<script>')).toBeNull();
  });
});

describe('longDate', () => {
  const iso = '2026-11-29T23:30:00+00:00';

  it('writes the day in the reader order, in UTC', () => {
    expect(longDate(iso, 'en-US')).toBe('November 29, 2026');
    expect(longDate(iso, 'en-GB')).toBe('29 November 2026');
    expect(longDate(iso, 'de')).toBe('29. November 2026');
  });

  it('does not move to the next day for a reader ahead of UTC', () => {
    expect(longDate('2026-11-29T23:30:00Z', 'en-GB')).toBe('29 November 2026');
  });
});
