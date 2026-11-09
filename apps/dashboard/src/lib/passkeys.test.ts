import { afterEach, describe, expect, it, vi } from 'vitest';
import { cancelledPrompt, fakeCredential, stubWebAuthn, unstubWebAuthn } from '../test-support/webauthn.js';
import { deviceName, passkeysSupported, runPasskeyRegistration, runPasskeySignIn } from './passkeys.js';

const options = (extra: object = {}) => Response.json({ optionsJson: JSON.stringify({ challenge: 'abc', ...extra }) });

// Answers each request by method and path and records the bodies of the posts.
function stubApi(routes: Record<string, () => Response>) {
  const posts: Record<string, unknown> = {};
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const key = `${(init?.method ?? 'GET').toUpperCase()} ${String(input)}`;
      const route = routes[key];
      if (!route) throw new Error(`No stub for ${key}`);
      if (typeof init?.body === 'string') posts[key] = JSON.parse(init.body);
      return route();
    }),
  );
  return posts;
}

afterEach(() => {
  unstubWebAuthn();
});

describe('passkeysSupported', () => {
  it('is false without WebAuthn, and true once the browser has it with its JSON parsers', () => {
    expect(passkeysSupported()).toBe(false);

    stubWebAuthn();
    expect(passkeysSupported()).toBe(true);
  });

  it('is false when the browser has WebAuthn but not the JSON parsers', () => {
    stubWebAuthn();
    vi.stubGlobal('PublicKeyCredential', class {});

    expect(passkeysSupported()).toBe(false);
  });
});

describe('runPasskeySignIn', () => {
  it('asks for options, lets the browser choose, and sends the answer as text', async () => {
    const credentials = stubWebAuthn();
    const posts = stubApi({
      'POST /v1/auth/passkeys/sign-in/options': () => options(),
      'POST /v1/auth/passkeys/sign-in': () => new Response(null, { status: 204 }),
    });

    await expect(runPasskeySignIn()).resolves.toEqual({ kind: 'success', value: undefined });

    expect(credentials.get).toHaveBeenCalledWith({ publicKey: { challenge: 'abc' } });
    const sent = posts['POST /v1/auth/passkeys/sign-in'] as { credentialJson: string };
    expect(JSON.parse(sent.credentialJson)).toEqual(fakeCredential().toJSON());
  });

  it('is cancelled, not failed, when the person closes the browser prompt', async () => {
    stubWebAuthn({ get: async () => Promise.reject(cancelledPrompt()) });
    stubApi({ 'POST /v1/auth/passkeys/sign-in/options': () => options() });

    await expect(runPasskeySignIn()).resolves.toEqual({ kind: 'cancelled' });
  });

  it('fails when the API refuses the passkey', async () => {
    stubWebAuthn();
    stubApi({
      'POST /v1/auth/passkeys/sign-in/options': () => options(),
      'POST /v1/auth/passkeys/sign-in': () => Response.json({ errors: {} }, { status: 400 }),
    });

    await expect(runPasskeySignIn()).resolves.toEqual({ kind: 'failed' });
  });

  it('fails when the options cannot be had, and when the network is down', async () => {
    stubWebAuthn();
    stubApi({ 'POST /v1/auth/passkeys/sign-in/options': () => Response.json({}, { status: 429 }) });
    await expect(runPasskeySignIn()).resolves.toEqual({ kind: 'failed' });

    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new TypeError('Failed to fetch'))));
    await expect(runPasskeySignIn()).resolves.toEqual({ kind: 'failed' });
  });

  it('fails when the browser answers with something that is not a passkey', async () => {
    stubWebAuthn({ get: async () => null });
    stubApi({ 'POST /v1/auth/passkeys/sign-in/options': () => options() });

    await expect(runPasskeySignIn()).resolves.toEqual({ kind: 'failed' });
  });
});

describe('runPasskeyRegistration', () => {
  it('creates the credential and sends it with the name', async () => {
    const credentials = stubWebAuthn();
    const added = { id: 'abc', name: 'Work laptop', createdAt: '2026-11-09T09:00:00Z', lastUsedAt: null };
    const posts = stubApi({
      'POST /v1/auth/passkeys/register/options': () => options({ user: 'x' }),
      'POST /v1/auth/passkeys/register': () => Response.json(added),
    });

    await expect(runPasskeyRegistration('Work laptop')).resolves.toEqual({ kind: 'success', value: added });

    expect(credentials.create).toHaveBeenCalledWith({ publicKey: { challenge: 'abc', user: 'x' } });
    expect(posts['POST /v1/auth/passkeys/register']).toEqual(
      expect.objectContaining({ name: 'Work laptop', credentialJson: expect.stringContaining('"type":"public-key"') }),
    );
  });

  it('says why when the person is at the limit, before the browser is asked', async () => {
    const credentials = stubWebAuthn();
    stubApi({
      'POST /v1/auth/passkeys/register/options': () =>
        Response.json({ title: 'Too many passkeys', detail: 'You can keep 10 passkeys.', status: 409 }, { status: 409 }),
    });

    await expect(runPasskeyRegistration('Phone')).resolves.toEqual({ kind: 'failed', message: 'You can keep 10 passkeys.' });
    expect(credentials.create).not.toHaveBeenCalled();
  });

  it('is cancelled when the prompt is closed, and says so when the device already has one', async () => {
    stubApi({ 'POST /v1/auth/passkeys/register/options': () => options() });

    stubWebAuthn({ create: async () => Promise.reject(cancelledPrompt()) });
    await expect(runPasskeyRegistration('Phone')).resolves.toEqual({ kind: 'cancelled' });

    stubWebAuthn({ create: async () => Promise.reject(new DOMException('exists', 'InvalidStateError')) });
    await expect(runPasskeyRegistration('Phone')).resolves.toEqual({
      kind: 'failed',
      message: 'This device already has a passkey for your account.',
    });
  });

  it("shows the API's own reason when it refuses the credential", async () => {
    stubWebAuthn();
    stubApi({
      'POST /v1/auth/passkeys/register/options': () => options(),
      'POST /v1/auth/passkeys/register': () =>
        Response.json({ errors: { credentialJson: ['That passkey could not be added. Try again, or use another device.'] } }, { status: 400 }),
    });

    await expect(runPasskeyRegistration('Phone')).resolves.toEqual({
      kind: 'failed',
      message: 'That passkey could not be added. Try again, or use another device.',
    });
  });
});

describe('deviceName', () => {
  it.each([
    ['Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36', 'Chrome on Windows'],
    ['Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0', 'Edge on Windows'],
    ['Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15', 'Safari on macOS'],
    ['Mozilla/5.0 (X11; Linux x86_64; rv:132.0) Gecko/20100101 Firefox/132.0', 'Firefox on Linux'],
    ['Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1', 'Safari on iOS'],
    ['Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36', 'Chrome on Android'],
  ])('reads %#: %s', (userAgent, expected) => {
    expect(deviceName({ userAgent })).toBe(expected);
  });

  it('prefers what the browser reports about its platform when the user agent is reduced', () => {
    expect(deviceName({ userAgent: 'Mozilla/5.0 Chrome/130.0.0.0 Safari/537.36', userAgentData: { platform: 'macOS' } })).toBe('Chrome on macOS');
  });

  it('falls back to a plain name', () => {
    expect(deviceName({ userAgent: 'curl/8.0' })).toBe('This device');
    expect(deviceName({ userAgent: 'Mozilla/5.0 (Windows NT 10.0)' })).toBe('Windows');
  });
});
