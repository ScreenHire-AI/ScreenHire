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

describe('the bot check on a first save', () => {
  // Saving for the first time on the questions page, as sam@example.com with no draft yet.
  const saveFirst = (extra: Record<string, string> = {}) =>
    form({
      organization: 'acme',
      job: 'barista',
      step: 'page-1',
      pageQuestions: 'start',
      'q:start': 'Monday',
      intent: 'save',
      state: JSON.stringify({ firstName: 'Sam', email: 'sam@example.com' }),
      ...extra,
    });
  const bodyOf = (call: unknown[]) => JSON.parse((call[1] as RequestInit).body as string) as Record<string, unknown>;

  it('sends the widget token with the request that emails the link', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(200, applicationForm)).mockResolvedValueOnce(json(201, { token, expiresAt }));
    vi.stubGlobal('fetch', fetch);

    const result = await applyStep(saveFirst({ 'cf-turnstile-response': 'widget-token' }), undefined);

    expect(result.saved).toMatchObject({ emailed: true });
    expect(bodyOf(fetch.mock.calls[1]!).botCheckToken).toBe('widget-token');
  });

  it('sends no token when the widget gave none, and saving again sends none either', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(200, applicationForm)).mockResolvedValueOnce(json(201, { token, expiresAt }));
    vi.stubGlobal('fetch', fetch);
    await applyStep(saveFirst(), undefined);
    expect(bodyOf(fetch.mock.calls[1]!).botCheckToken).toBeNull();

    const again = vi.fn().mockResolvedValueOnce(json(200, applicationForm)).mockResolvedValueOnce(json(200, { token, expiresAt }));
    vi.stubGlobal('fetch', again);
    await applyStep(saveAgain(), undefined);
    expect(bodyOf(again.mock.calls[1]!)).not.toHaveProperty('botCheckToken');
  });

  it('says no link was emailed and offers sending the application when the check is refused', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(200, applicationForm)).mockResolvedValueOnce(json(403, { title: 'Security check not passed' }));
    vi.stubGlobal('fetch', fetch);

    const result = await applyStep(saveFirst(), undefined);

    expect(result.saved).toBeUndefined();
    expect(result.state.draft).toBeNull();
    expect(result.formError).toContain('no link was emailed');
    expect(result.formError).toContain('send your application now');
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

describe('the site the careers page is embedded in', () => {
  const embedded = { ...applicationForm, organization: { embedOrigins: ['https://www.example.com'] } } as unknown as PublicApplicationForm;
  const send = () =>
    form({
      organization: 'acme',
      job: 'barista',
      step: 'review',
      state: JSON.stringify({ firstName: 'Sam', email: 'sam@example.com' }),
    });
  const sent = (fetch: ReturnType<typeof vi.fn>) => JSON.parse((fetch.mock.calls[1]![1] as RequestInit).body as string);

  it('goes with the application while it is on the organization list', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(200, embedded)).mockResolvedValueOnce(json(201, { applicationId: 'x' }));
    vi.stubGlobal('fetch', fetch);

    const result = await applyStep(send(), undefined, null, new Date(), 'https://www.example.com');

    expect(result.outcome).toBe('submitted');
    expect(sent(fetch).embedOrigin).toBe('https://www.example.com');
  });

  it('is dropped once the organization no longer lists it', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(200, embedded)).mockResolvedValueOnce(json(201, { applicationId: 'x' }));
    vi.stubGlobal('fetch', fetch);

    await applyStep(send(), undefined, null, new Date(), 'https://removed.example.org');

    expect(sent(fetch).embedOrigin).toBeNull();
  });
});
