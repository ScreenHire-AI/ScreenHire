import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  ENDED,
  filesOf,
  isGuid,
  linkOutcome,
  looksLikeEmail,
  MAX_FILE_BYTES,
  newReplyKey,
  replyOutcome,
  replyProblem,
  requestLink,
  sendReply,
  stepStates,
} from './portal';

const token = 'CfDJ8Abc_def-123456789xyz';
const application = '0192a8f0-0000-7000-8000-000000000001';

const form = (entries: Record<string, string>, files: File[] = []): FormData => {
  const data = new FormData();
  for (const [name, value] of Object.entries(entries)) data.append(name, value);
  for (const file of files) data.append('files', file);
  return data;
};

const reply = (extra: Record<string, string> = {}) =>
  form({ organization: 'brightwater', token, application, key: newReplyKey(), body: 'Thanks, Friday works.', ...extra });

afterEach(() => vi.unstubAllGlobals());

describe('looksLikeEmail', () => {
  it('accepts an address and refuses what cannot be one', () => {
    expect(looksLikeEmail('sam@example.com')).toBe(true);
    expect(looksLikeEmail('')).toBe(false);
    expect(looksLikeEmail('sam')).toBe(false);
    expect(looksLikeEmail('sam @example.com')).toBe(false);
  });
});

describe('isGuid', () => {
  it('accepts an application id and refuses anything that could change an API path', () => {
    expect(isGuid(application)).toBe(true);
    expect(isGuid('../x')).toBe(false);
    expect(isGuid('')).toBe(false);
    expect(isGuid(undefined)).toBe(false);
  });
});

describe('newReplyKey', () => {
  it('is 32 hex digits and different every time', () => {
    const first = newReplyKey();
    expect(first).toMatch(/^[0-9a-f]{32}$/);
    expect(newReplyKey()).not.toBe(first);
  });
});

describe('linkOutcome and replyOutcome', () => {
  it('map the API status to what the page says', () => {
    expect(linkOutcome(202)).toBe('sent');
    expect(linkOutcome(400)).toBe('bad-email');
    expect(linkOutcome(403)).toBe('bot-check-failed');
    expect(linkOutcome(404)).toBe('unavailable');
    expect(linkOutcome(429)).toBe('busy');
    expect(linkOutcome(500)).toBe('error');

    expect(replyOutcome(202)).toBe('sent');
    expect(replyOutcome(404)).toBe('expired');
    expect(replyOutcome(409)).toBe('nothing-to-reply-to');
    expect(replyOutcome(429)).toBe('too-many-today');
    expect(replyOutcome(500)).toBe('error');
  });
});

describe('stepStates', () => {
  const steps = ['Applied', 'In review', 'Interview', 'Decision'];

  it('marks the steps before the current one done, the current one, and the rest upcoming', () => {
    expect(stepStates(steps, 2).map((step) => step.state)).toEqual(['done', 'done', 'current', 'upcoming']);
    expect(stepStates(steps, 0).map((step) => step.state)).toEqual(['current', 'upcoming', 'upcoming', 'upcoming']);
  });

  it('marks nothing current once the application has ended', () => {
    expect(stepStates(steps, null).every((step) => step.state === 'upcoming')).toBe(true);
  });
});

describe('ended applications', () => {
  it('reads a saved candidate as not moving forward, with no word about a list or a reason', () => {
    expect(ENDED.NotMovingForward.label).toBe('Not moving forward');
    expect(ENDED.Hired.mark).toBe('check');
    expect(ENDED.NotMovingForward.mark).toBe('minus');
    expect(ENDED.Withdrawn.mark).toBe('minus');
    expect(JSON.stringify(ENDED).toLowerCase()).not.toMatch(/list|saved|talent|pool|reject|verif|score|rating/);
  });
});

describe('replyProblem', () => {
  const file = (size: number) => new File([new Uint8Array(size)], 'cv.pdf');

  it('refuses a blank message, a long one, too many files and too many bytes', () => {
    expect(replyProblem('   ', [], 100)).toBe('empty');
    expect(replyProblem('x'.repeat(101), [], 100)).toBe('too-long');
    expect(replyProblem('hi', [file(1), file(1), file(1), file(1)], 100)).toBe('too-many-files');
    expect(replyProblem('hi', [file(MAX_FILE_BYTES), file(1)], 100)).toBe('files-too-large');
  });

  it('lets a message with a few small files go', () => {
    expect(replyProblem('hi', [file(10), file(20)], 100)).toBeNull();
  });
});

describe('filesOf', () => {
  it('drops the empty part a browser sends when no file was chosen', () => {
    const data = form({}, [new File([], '')]);
    expect(filesOf(data)).toEqual([]);
    const real = form({}, [new File(['abc'], 'cv.pdf')]);
    expect(filesOf(real).map((f) => f.name)).toEqual(['cv.pdf']);
  });
});

describe('requestLink', () => {
  it('refuses an address that cannot be one without calling the API, and keeps what was typed', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    expect(await requestLink(form({ organization: 'brightwater', email: 'nope' }), '203.0.113.9')).toEqual({ outcome: 'bad-email', email: 'nope' });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('is sent when the API takes it, and carries nothing back', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 202 })));
    expect(await requestLink(form({ organization: 'brightwater', email: 'sam@example.com' }), '203.0.113.9')).toEqual({ outcome: 'sent' });
  });
});

describe('sendReply', () => {
  it('treats a token of the wrong shape as an expired link and never calls the API', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    expect((await sendReply(reply({ token: '../etc' }), undefined)).outcome).toBe('expired');
    expect((await sendReply(reply({ application: 'x' }), undefined)).outcome).toBe('expired');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('keeps what was typed and the form id when it is blank or too long', async () => {
    vi.stubGlobal('fetch', vi.fn());
    const blank = await sendReply(reply({ body: '  ' }), undefined);
    expect(blank).toMatchObject({ outcome: 'empty', body: '  ' });
    expect(blank.key).toMatch(/^[0-9a-f]{32}$/);
    expect((await sendReply(reply({ body: 'x'.repeat(30), bodyMax: '20' }), undefined)).outcome).toBe('too-long');
  });

  it('posts the words, the form id and the files as base64, and reports a sent reply', async () => {
    const fetchMock = vi.fn(async (_url: unknown, _init?: RequestInit) => new Response(null, { status: 202 }));
    vi.stubGlobal('fetch', fetchMock);
    const data = reply({ key: 'a'.repeat(32) });
    data.append('files', new File(['hello'], 'cv.pdf'));

    expect(await sendReply(data, '203.0.113.9')).toEqual({ outcome: 'sent' });

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(String(url)).toContain(`/v1/public/organizations/brightwater/portal/${token}/applications/${application}/messages`);
    expect(JSON.parse(String(init?.body))).toEqual({
      body: 'Thanks, Friday works.',
      key: 'a'.repeat(32),
      attachments: [{ fileName: 'cv.pdf', contentBase64: Buffer.from('hello').toString('base64') }],
    });
  });

  it('maps the API refusing it to a message and hands back the words', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 409 })));
    const result = await sendReply(reply(), undefined);
    expect(result).toMatchObject({ outcome: 'nothing-to-reply-to', body: 'Thanks, Friday works.' });
  });
});
