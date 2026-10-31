import { afterEach, describe, expect, it, vi } from 'vitest';
import type { PublicApplicationForm, PublicQuestion } from './api';
import { applyStep } from './apply-server';

const question: PublicQuestion = {
  id: 'start',
  type: 'ShortText',
  label: 'When can you start?',
  required: false,
  helpText: null,
  options: null,
  min: null,
  max: null,
  maxLength: 300,
  acceptedFileTypes: null,
};

const applicationForm = {
  job: { id: '0199e8a0-0000-7000-8000-000000000101', slug: 'barista', title: 'Barista' },
  questions: [question],
  aiUsePolicy: 'Allowed',
  turnstileSiteKey: null,
  sections: null,
  questionsPerPage: 8,
  saveDraftEnabled: true,
} as unknown as PublicApplicationForm;

const token = 'CfDJ8Abc_def-123456789xyz';
const expiresAt = '2026-11-30T09:00:00+00:00';

const form = (entries: Record<string, string>): FormData => {
  const data = new FormData();
  for (const [name, value] of Object.entries(entries)) data.append(name, value);
  return data;
};

const json = (status: number, body: unknown = null) =>
  new Response(body === null ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

// Saving on the questions page of a form already saved once, as sam@example.com.
const saveAgain = () =>
  form({
    organization: 'acme',
    job: 'barista',
    step: 'page-1',
    pageQuestions: 'start',
    'q:start': 'Monday',
    intent: 'save',
    state: JSON.stringify({ firstName: 'Sam', email: 'sam@example.com', draft: { token, email: 'sam@example.com' } }),
  });

afterEach(() => vi.unstubAllGlobals());

describe('saving a draft again', () => {
  it('updates the draft its link names and says no new email went out', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(200, applicationForm)).mockResolvedValueOnce(json(200, { token, expiresAt }));
    vi.stubGlobal('fetch', fetch);

    const result = await applyStep(saveAgain(), undefined);

    expect(result).toMatchObject({ step: 'page-1', saved: { email: 'sam@example.com', expiresAt, emailed: false } });
    expect(fetch).toHaveBeenCalledTimes(2);
    expect((fetch.mock.calls[1]![1] as RequestInit).method).toBe('PUT');
  });

  it('makes a new draft, and emails its link, when the old one is gone', async () => {
    const fresh = 'CfDJ8Xyz_new-987654321abc';
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(json(200, applicationForm))
      .mockResolvedValueOnce(json(404))
      .mockResolvedValueOnce(json(201, { token: fresh, expiresAt }));
    vi.stubGlobal('fetch', fetch);

    const result = await applyStep(saveAgain(), undefined);

    expect(result).toMatchObject({ saved: { email: 'sam@example.com', emailed: true } });
    expect(result.state.draft).toEqual({ token: fresh, email: 'sam@example.com' });
    expect((fetch.mock.calls[2]![1] as RequestInit).method).toBe('POST');
  });

  it.each([429, 500])('keeps the old draft and sends no second link when saving it fails with %i', async (status) => {
    const fetch = vi.fn().mockResolvedValueOnce(json(200, applicationForm)).mockResolvedValueOnce(json(status));
    vi.stubGlobal('fetch', fetch);

    const result = await applyStep(saveAgain(), undefined);

    expect(result.saved).toBeUndefined();
    expect(result.formError).toBeTruthy();
    expect(result.state.draft).toEqual({ token, email: 'sam@example.com' });
    expect(fetch).toHaveBeenCalledTimes(2);
  });
});

describe('time on each page', () => {
  const details = (accommodation: boolean) =>
    form({
      organization: 'acme',
      job: 'barista',
      step: 'details',
      firstName: 'Sam',
      email: 'sam@example.com',
      ...(accommodation ? { accommodationRequested: 'on' } : {}),
      state: JSON.stringify({ pageShownAt: '2026-10-30T09:00:00.000Z', pageSeconds: { details: 30 } }),
    });
  const now = new Date('2026-10-30T09:01:00.000Z');

  it('counts the seconds a page was open', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(200, applicationForm)));

    const result = await applyStep(details(false), undefined, null, now);

    expect(result.state.pageSeconds).toEqual({ details: 90 });
  });

  it('keeps no time at all with the assistive-technology switch on', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(200, applicationForm)));

    const result = await applyStep(details(true), undefined, null, now);

    expect(result.state.accommodationRequested).toBe(true);
    expect(result.state.pageSeconds).toEqual({});
  });
});
