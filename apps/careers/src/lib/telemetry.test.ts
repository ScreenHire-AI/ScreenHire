import { describe, expect, it } from 'vitest';
import { MAX_TELEMETRY_BYTES, parseFingerprint, parseMarkers, parseSnapshot } from './telemetry';

const snapshot = {
  sessionToken: 'CfDJ8token',
  pageViewId: '0199e8a0-0000-7000-8000-00000000aaaa',
  sequence: 3,
  fields: [
    {
      fieldId: 'notice_period',
      pasteCount: 1,
      pastedCharacters: 120,
      typedCharacters: 4,
      focusCount: 2,
      blurCount: 2,
      activeMilliseconds: 31_000,
      autofilled: false,
    },
  ],
  page: { visibilityChanges: 1, automationMarkers: ['webdriver'] },
};

describe('parseSnapshot', () => {
  it("takes the module's snapshot as it is", () => {
    expect(parseSnapshot(JSON.stringify(snapshot))).toEqual(snapshot);
  });

  it('refuses anything beyond counts and known ids', () => {
    const field = snapshot.fields[0]!;
    for (const tampered of [
      { ...snapshot, answer: 'I pasted this' },
      { ...snapshot, fields: [{ ...field, value: 'I pasted this' }] },
      { ...snapshot, fields: [{ ...field, fieldId: 'notice period: two weeks' }] },
      { ...snapshot, fields: [{ ...field, typedCharacters: -1 }] },
      { ...snapshot, fields: [{ ...field, typedCharacters: 1.5 }] },
      { ...snapshot, page: { ...snapshot.page, automationMarkers: ['my-own-marker'] } },
      { ...snapshot, page: { ...snapshot.page, keys: ['a', 'b'] } },
      { ...snapshot, pageViewId: 'not-a-uuid' },
    ]) {
      expect(parseSnapshot(JSON.stringify(tampered))).toBeNull();
    }
  });

  it('refuses an oversized or malformed body', () => {
    expect(parseSnapshot(JSON.stringify({ ...snapshot, sessionToken: 'x'.repeat(MAX_TELEMETRY_BYTES) }))).toBeNull();
    expect(parseSnapshot('{not json')).toBeNull();
    expect(parseSnapshot('')).toBeNull();
    expect(parseSnapshot(null)).toBeNull();
  });
});

describe('the device fields', () => {
  it('takes a SHA-256 fingerprint and known markers only', () => {
    expect(parseFingerprint('ab'.repeat(32))).toBe('ab'.repeat(32));
    expect(parseFingerprint('device-of-casey')).toBeNull();
    expect(parseMarkers('webdriver,headless,webdriver,evil')).toEqual(['webdriver', 'headless']);
    expect(parseMarkers('')).toEqual([]);
    expect(parseMarkers(null)).toEqual([]);
  });
});
