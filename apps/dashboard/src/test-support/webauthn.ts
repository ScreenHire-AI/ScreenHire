import { vi } from 'vitest';

// A browser with WebAuthn, for component tests: jsdom has neither `PublicKeyCredential` nor `navigator.credentials`.
// The parsers hand their input back, so a test can see what the API's options became.

/** What a passkey ceremony hands back: `credential.toJSON()` is what the page sends to the API. */
export function fakeCredential(id = 'credential-1') {
  return { id, type: 'public-key', toJSON: () => ({ id, type: 'public-key', response: { clientDataJSON: 'e30' } }) };
}

type Ceremony = (request?: { mediation?: string; signal?: AbortSignal }) => Promise<unknown>;

/**
 * Gives the page WebAuthn. `get` answers a sign-in, `create` a registration; both succeed unless a test says otherwise.
 * `conditional` says whether the browser can offer a passkey inside a field (conditional mediation); a conditional `get`
 * waits for its signal to abort unless the test gives `get` of its own.
 */
export function stubWebAuthn(ceremonies: { get?: Ceremony; create?: Ceremony; conditional?: boolean } = {}) {
  const credentials = {
    get: vi.fn(
      ceremonies.get ??
        (async (request?: { mediation?: string; signal?: AbortSignal }) =>
          request?.mediation === 'conditional'
            ? new Promise((_, reject) => request.signal?.addEventListener('abort', () => reject(new DOMException('Aborted.', 'AbortError'))))
            : fakeCredential()),
    ),
    create: vi.fn(ceremonies.create ?? (async () => fakeCredential())),
  };
  vi.stubGlobal(
    'PublicKeyCredential',
    class {
      static parseRequestOptionsFromJSON = (options: unknown) => options;
      static parseCreationOptionsFromJSON = (options: unknown) => options;
      static isConditionalMediationAvailable = async () => ceremonies.conditional ?? false;
    },
  );
  Object.defineProperty(navigator, 'credentials', { value: credentials, configurable: true });
  return credentials;
}

/** Takes WebAuthn away again; pair it with `vi.unstubAllGlobals()` in `afterEach`. */
export function unstubWebAuthn() {
  vi.unstubAllGlobals();
  Reflect.deleteProperty(navigator, 'credentials');
}

/** The browser's answer when the person closes its prompt. */
export const cancelledPrompt = () => new DOMException('The operation either timed out or was not allowed.', 'NotAllowedError');
