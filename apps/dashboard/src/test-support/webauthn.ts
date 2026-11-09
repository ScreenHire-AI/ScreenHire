import { vi } from 'vitest';

// A browser with WebAuthn, for component tests: jsdom has neither `PublicKeyCredential` nor `navigator.credentials`.
// The parsers hand their input back, so a test can see what the API's options became.

/** What a passkey ceremony hands back: `credential.toJSON()` is what the page sends to the API. */
export function fakeCredential(id = 'credential-1') {
  return { id, type: 'public-key', toJSON: () => ({ id, type: 'public-key', response: { clientDataJSON: 'e30' } }) };
}

type Ceremony = () => Promise<unknown>;

/** Gives the page WebAuthn. `get` answers a sign-in, `create` a registration; both succeed unless a test says otherwise. */
export function stubWebAuthn(ceremonies: { get?: Ceremony; create?: Ceremony } = {}) {
  const credentials = {
    get: vi.fn(ceremonies.get ?? (async () => fakeCredential())),
    create: vi.fn(ceremonies.create ?? (async () => fakeCredential())),
  };
  vi.stubGlobal(
    'PublicKeyCredential',
    class {
      static parseRequestOptionsFromJSON = (options: unknown) => options;
      static parseCreationOptionsFromJSON = (options: unknown) => options;
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
