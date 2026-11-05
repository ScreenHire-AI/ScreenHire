import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  downloadLink,
  downloadTarget,
  linkOutcome,
  looksLikeEmail,
  makeRequest,
  parseKind,
  requestLink,
  requestOutcome,
} from './data-request';

const token = 'CfDJ8Abc_def-123456789xyz';

const form = (entries: Record<string, string>): FormData => {
  const data = new FormData();
  for (const [name, value] of Object.entries(entries)) data.append(name, value);
  return data;
};

const json = (status: number, body: unknown = null) =>
  new Response(body === null ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

afterEach(() => vi.unstubAllGlobals());

describe('looksLikeEmail', () => {
  it('accepts an address and refuses what cannot be one', () => {
    expect(looksLikeEmail('sam@example.com')).toBe(true);
    expect(looksLikeEmail('')).toBe(false);
    expect(looksLikeEmail('sam')).toBe(false);
    expect(looksLikeEmail('sam@')).toBe(false);
    expect(looksLikeEmail('sam smith@example.com')).toBe(false);
    expect(looksLikeEmail(`${'a'.repeat(250)}@example.com`)).toBe(false);
  });
});

describe('parseKind', () => {
  it('knows the two kinds and nothing else', () => {
    expect(parseKind('Copy')).toBe('Copy');
    expect(parseKind('Erasure')).toBe('Erasure');
    expect(parseKind('copy')).toBeNull();
    expect(parseKind('Delete')).toBeNull();
    expect(parseKind('')).toBeNull();
  });
});

describe('outcome mapping', () => {
  it('maps the answers to asking for a link', () => {
    expect(linkOutcome(202)).toBe('sent');
    expect(linkOutcome(400)).toBe('bad-email');
    expect(linkOutcome(403)).toBe('bot-check-failed');
    expect(linkOutcome(404)).toBe('unavailable');
    expect(linkOutcome(429)).toBe('busy');
    expect(linkOutcome(500)).toBe('error');
  });

  it('maps the answers to a choice, a 404 being a link that no longer works', () => {
    expect(requestOutcome(200)).toBe('received');
    expect(requestOutcome(400)).toBe('bad-choice');
    expect(requestOutcome(404)).toBe('expired');
    expect(requestOutcome(429)).toBe('busy');
    expect(requestOutcome(503)).toBe('error');
  });
});

describe('downloadTarget', () => {
  it('follows a web address and refuses anything else', () => {
    expect(downloadTarget('https://files.example.com/data-exports/a.json?sig=abc')).toBe('https://files.example.com/data-exports/a.json?sig=abc');
    expect(downloadTarget('http://127.0.0.1:10000/devstoreaccount1/a.json')).toBe('http://127.0.0.1:10000/devstoreaccount1/a.json');
    expect(downloadTarget('javascript:alert(1)')).toBeNull();
    expect(downloadTarget('data:text/html,hi')).toBeNull();
    expect(downloadTarget('not a url')).toBeNull();
    expect(downloadTarget(undefined)).toBeNull();
  });
});

describe('requestLink', () => {
  it('sends the address, the bot check token and the visitor address, and moves on when the API accepts', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(202));
    vi.stubGlobal('fetch', fetch);

    const result = await requestLink(form({ organization: 'acme', email: ' sam@example.com ', 'cf-turnstile-response': 'ok-token' }), '203.0.113.9');

    expect(result).toEqual({ outcome: 'sent' });
    const [url, init] = fetch.mock.calls[0]! as [string, RequestInit];
    expect(url).toContain('/v1/public/organizations/acme/data-requests/links');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ email: 'sam@example.com', botCheckToken: 'ok-token' });
    expect(new Headers(init.headers).get('X-Forwarded-For')).toBe('203.0.113.9');
  });

  it('sends no bot check token when the form had none', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(202));
    vi.stubGlobal('fetch', fetch);

    await requestLink(form({ organization: 'acme', email: 'sam@example.com' }), undefined);

    expect(JSON.parse((fetch.mock.calls[0]![1] as RequestInit).body as string)).toEqual({ email: 'sam@example.com', botCheckToken: null });
  });

  it('never calls the API for an address that cannot be one, and hands what was typed back', async () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);

    expect(await requestLink(form({ organization: 'acme', email: '  ' }), undefined)).toEqual({ outcome: 'bad-email', email: '' });
    expect(await requestLink(form({ organization: 'acme', email: 'sam' }), undefined)).toEqual({ outcome: 'bad-email', email: 'sam' });
    expect(fetch).not.toHaveBeenCalled();
  });

  it.each([
    [403, 'bot-check-failed'],
    [404, 'unavailable'],
    [429, 'busy'],
    [500, 'error'],
  ])('answers %i with %s, keeping what was typed', async (status, outcome) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(status)));

    expect(await requestLink(form({ organization: 'acme', email: 'sam@example.com' }), undefined)).toEqual({ outcome, email: 'sam@example.com' });
  });
});

describe('makeRequest', () => {
  it('sends the chosen kind with the token in the path, never in the body', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(200, { id: 'r', kind: 'Erasure', receivedAt: '2026-11-05T09:00:00+00:00' }));
    vi.stubGlobal('fetch', fetch);

    const result = await makeRequest(form({ organization: 'acme', token, kind: 'Erasure' }), '203.0.113.9');

    expect(result).toEqual({ outcome: 'received' });
    const [url, init] = fetch.mock.calls[0]! as [string, RequestInit];
    expect(url).toContain(`/v1/public/organizations/acme/data-requests/${token}`);
    expect(JSON.parse(init.body as string)).toEqual({ kind: 'Erasure' });
    expect(new Headers(init.headers).get('X-Forwarded-For')).toBe('203.0.113.9');
  });

  it('never calls the API for a token of the wrong shape or a kind it does not know', async () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);

    expect(await makeRequest(form({ organization: 'acme', token: '../secrets', kind: 'Copy' }), undefined)).toEqual({ outcome: 'expired' });
    expect(await makeRequest(form({ organization: 'acme', token: '', kind: 'Copy' }), undefined)).toEqual({ outcome: 'expired' });
    expect(await makeRequest(form({ organization: 'acme', token, kind: 'Everything' }), undefined)).toEqual({ outcome: 'bad-choice' });
    expect(await makeRequest(form({ organization: 'acme', token }), undefined)).toEqual({ outcome: 'bad-choice' });
    expect(fetch).not.toHaveBeenCalled();
  });

  it.each([
    [404, 'expired'],
    [429, 'busy'],
    [500, 'error'],
  ])('answers %i with %s', async (status, outcome) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(json(status)));

    expect(await makeRequest(form({ organization: 'acme', token, kind: 'Copy' }), undefined)).toEqual({ outcome });
  });
});

describe('downloadLink', () => {
  it('returns the API read link for a link that works', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(200, { url: 'https://files.example.com/a.json?sig=abc' }));
    vi.stubGlobal('fetch', fetch);

    expect(await downloadLink('acme', token, '203.0.113.9')).toBe('https://files.example.com/a.json?sig=abc');
    expect(fetch.mock.calls[0]![0]).toContain(`/v1/public/organizations/acme/data-requests/downloads/${token}`);
    expect(new Headers((fetch.mock.calls[0]![1] as RequestInit).headers).get('X-Forwarded-For')).toBe('203.0.113.9');
  });

  it('returns null for an expired link, a link of the wrong shape and a read link that is not a web address', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(json(404)).mockResolvedValueOnce(json(200, { url: 'file:///etc/passwd' }));
    vi.stubGlobal('fetch', fetch);

    expect(await downloadLink('acme', token, undefined)).toBeNull();
    expect(await downloadLink('acme', undefined, undefined)).toBeNull();
    expect(await downloadLink('acme', 'a/b', undefined)).toBeNull();
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(await downloadLink('acme', token, undefined)).toBeNull();
    expect(fetch).toHaveBeenCalledTimes(2);
  });
});
