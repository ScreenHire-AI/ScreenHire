import type { ApplicationSummary } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { applications, now } from '../test-support/fixtures.js';
import { findSavedView, overviewViews, savedViews } from './saved-views.js';

const day = 24 * 60 * 60 * 1000;
const at = (offset: number) => new Date(now + offset).toISOString();
const base: ApplicationSummary = applications[0]!;
const match = (id: string, application: Partial<ApplicationSummary>) => findSavedView(id)!.matches({ ...base, ...application }, now);

describe('the Overview views', () => {
  it('count a rolling week of new applications, and not an older one', () => {
    expect(match('new-this-week', { submittedAt: at(-6 * day) })).toBe(true);
    expect(match('new-this-week', { submittedAt: at(-8 * day) })).toBe(false);
  });

  it('count a booked interview that starts within the week, and not one later or none', () => {
    expect(match('interviews-this-week', { nextInterviewAt: at(2 * day) })).toBe(true);
    expect(match('interviews-this-week', { nextInterviewAt: at(9 * day) })).toBe(false);
    expect(match('interviews-this-week', { nextInterviewAt: null })).toBe(false);
    expect(match('interviews-this-week', {})).toBe(false);
  });

  it('open from ?view= like any built-in view, but stay out of the sidebar list', () => {
    for (const view of overviewViews) {
      expect(findSavedView(view.id)).toBe(view);
      expect(savedViews.some((listed) => listed.id === view.id)).toBe(false);
    }
  });
});
