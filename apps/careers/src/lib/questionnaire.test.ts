import { afterEach, describe, expect, it, vi } from 'vitest';
import type { PublicQuestion } from './api';
import { answerQuestionnaire, isToken, toAnswers } from './questionnaire';

const question = (id: string, type: PublicQuestion['type'], required = false): PublicQuestion => ({
  id,
  type,
  label: id,
  required,
  helpText: null,
  options: type === 'MultiChoice' ? [{ id: 'a', label: 'A' }, { id: 'b', label: 'B' }] : null,
  min: null,
  max: null,
  maxLength: type === 'ShortText' ? 300 : null,
  acceptedFileTypes: null,
});

const token = 'CfDJ8Abc_def-123456789xyz';

const form = (entries: Record<string, string | string[]>): FormData => {
  const data = new FormData();
  for (const [name, value] of Object.entries(entries)) {
    for (const each of Array.isArray(value) ? value : [value]) data.append(name, each);
  }
  return data;
};

const json = (status: number, body: unknown = null) =>
  new Response(body === null ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

describe('questionnaire links', () => {
  it('takes only base64url tokens, so a crafted link cannot change the API path', () => {
    expect(isToken(token)).toBe(true);
    expect(isToken('short')).toBe(false);
    expect(isToken(`${token}/../jobs`)).toBe(false);
    expect(isToken(`${token}?x=1`)).toBe(false);
    expect(isToken(undefined)).toBe(false);
  });

  it('sends answers in question order and leaves out the unanswered', () => {
    const questions = [question('start', 'ShortText'), question('shifts', 'MultiChoice'), question('licence', 'YesNo')];
    expect(toAnswers({ licence: 'no', shifts: ['a', 'b'] }, questions)).toEqual([
      { questionId: 'shifts', values: ['a', 'b'] },
      { questionId: 'licence', value: 'no' },
    ]);
  });
});

describe('answering a questionnaire', () => {
  afterEach(() => vi.unstubAllGlobals());

  const open = { organization: { name: 'Acme', slug: 'acme', branding: { colors: null, header: null, headingFont: null, logo: null, favicon: null, sharingImage: null } }, jobTitle: 'Warehouse lead', name: 'Availability', status: 'Open', questions: [question('start', 'ShortText', true), question('licence', 'YesNo', true)] };

  it('checks required answers before it sends anything', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(200, open));
    vi.stubGlobal('fetch', fetch);

    const result = await answerQuestionnaire(form({ organization: 'acme', token, 'q:licence': 'yes' }), undefined);

    expect(result.errors).toEqual({ 'q:start': 'Answer this question.' });
    expect(result.answers).toEqual({ licence: 'yes' });
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it('sends the answers and reports a link answered meanwhile', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(200, open)).mockResolvedValueOnce(json(204)).mockResolvedValueOnce(json(200, open)).mockResolvedValueOnce(json(409, { title: 'Already answered' }));
    vi.stubGlobal('fetch', fetch);
    const posted = form({ organization: 'acme', token, 'q:start': 'Next month', 'q:licence': 'yes' });

    expect((await answerQuestionnaire(posted, '192.0.2.10')).outcome).toBe('sent');
    const [url, init] = fetch.mock.calls[1]!;
    expect(String(url)).toContain(`/v1/public/organizations/acme/questionnaires/${token}`);
    expect(JSON.parse(String((init as RequestInit).body))).toEqual({
      answers: [
        { questionId: 'start', value: 'Next month' },
        { questionId: 'licence', value: 'yes' },
      ],
    });
    expect((await answerQuestionnaire(posted, undefined)).outcome).toBe('answered');
  });

  it('says an expired or unknown link ended without posting', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(200, { ...open, status: 'Expired', questions: [] })).mockResolvedValueOnce(json(404));
    vi.stubGlobal('fetch', fetch);

    expect((await answerQuestionnaire(form({ organization: 'acme', token }), undefined)).outcome).toBe('expired');
    expect((await answerQuestionnaire(form({ organization: 'acme', token }), undefined)).outcome).toBe('not-found');
    expect((await answerQuestionnaire(form({ organization: 'acme', token: 'bad' }), undefined)).outcome).toBe('not-found');
    expect(fetch).toHaveBeenCalledTimes(2);
  });
});
