import { describe, expect, it } from 'vitest';
import { draftFrom, inAppOn, requestFrom, setInApp, setToast } from './notice-grid.js';

const draft = draftFrom({
  scope: 'MyJobs',
  pauseToasts: false,
  closeToasts: true,
  toastsOff: ['Notes'],
  slackAvailable: false,
  slackLinked: false,
  rows: [
    { kind: 'Mentions', toasts: ['Mentions'], email: 'Instantly', emailDefault: 'Instantly', slack: true, scoped: false },
    { kind: 'TeammateChanges', toasts: ['Moves', 'Notes'], email: 'Off', emailDefault: 'Off', slack: null, scoped: true },
  ],
});

describe('the notification grid', () => {
  it('reads a row as on only while every toast it covers is on', () => {
    expect(inAppOn(draft, ['Mentions'])).toBe(true);
    expect(inAppOn(draft, ['Moves', 'Notes'])).toBe(false);
    expect(inAppOn(draft, [])).toBe(false);
  });

  it('turns every toast of a row on or off, and leaves the others', () => {
    expect(setInApp(draft, ['Moves', 'Notes'], true)).toEqual([]);
    expect(setInApp(draft, ['Mentions'], false)).toEqual(['Notes', 'Mentions']);
  });

  it('turns one kind on or off', () => {
    expect(setToast(draft, 'Notes', true)).toEqual([]);
    expect(setToast(draft, 'Moves', false)).toEqual(['Notes', 'Moves']);
  });

  it('sends the whole grid, each kind of toast once', () => {
    const body = requestFrom({ ...draft, toastsOff: ['Notes', 'Notes'] });
    expect(body.toastsOff).toEqual(['Notes']);
    expect(body.rows).toEqual([
      { kind: 'Mentions', email: 'Instantly', slack: true },
      { kind: 'TeammateChanges', email: 'Off', slack: null },
    ]);
  });
});
