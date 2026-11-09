import type { PasskeySummary } from '@screenhire/api-client';
import {
  makePasskeyRegistrationOptions,
  makePasskeySignInOptions,
  registerPasskey,
  signInWithPasskey,
} from '@screenhire/api-client';

// Passkeys through the browser's own WebAuthn, no library: the API sends the options as the W3C JSON shape, the browser
// parses them (`PublicKeyCredential.parse*OptionsJSON`) and answers with `credential.toJSON()`, so nothing here touches
// base64url. A browser without those two parsers gets no passkey button at all.

/** How a passkey ceremony ended. A person who closes the browser's prompt is `cancelled`, which is not an error. */
export type PasskeyOutcome<T = void> =
  | { kind: 'success'; value: T }
  | { kind: 'cancelled' }
  /** `message` is the API's own sentence or a known browser failure's; absent when there is nothing better than the page's own line. */
  | { kind: 'failed'; message?: string };

/** Whether this browser can sign in with a passkey and add one. Read once when a page opens. */
export function passkeysSupported(): boolean {
  return (
    typeof window !== 'undefined' &&
    typeof PublicKeyCredential !== 'undefined' &&
    typeof PublicKeyCredential.parseRequestOptionsFromJSON === 'function' &&
    typeof PublicKeyCredential.parseCreationOptionsFromJSON === 'function' &&
    typeof navigator.credentials?.get === 'function' &&
    typeof navigator.credentials?.create === 'function'
  );
}

/** Signs in: asks the API for options, lets the person pick a passkey in the browser's prompt, and sends the answer. */
export async function runPasskeySignIn(): Promise<PasskeyOutcome> {
  try {
    const options = await makePasskeySignInOptions();
    if (options.status !== 200) return { kind: 'failed' };

    const credential = await navigator.credentials.get({
      publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(JSON.parse(options.data.optionsJson)),
    });
    if (!isPublicKeyCredential(credential)) return { kind: 'failed' };

    const response = await signInWithPasskey({ credentialJson: JSON.stringify(credential.toJSON()) });
    return response.status === 204 ? { kind: 'success', value: undefined } : { kind: 'failed' };
  } catch (error) {
    return fromError(error);
  }
}

/** Adds a passkey to the signed-in person's account under `name`: the same ceremony, with `create` in place of `get`. */
export async function runPasskeyRegistration(name: string): Promise<PasskeyOutcome<PasskeySummary>> {
  try {
    const options = await makePasskeyRegistrationOptions();
    if (options.status === 409) return { kind: 'failed', message: options.data.detail ?? undefined };
    if (options.status !== 200) return { kind: 'failed' };

    const credential = await navigator.credentials.create({
      publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(JSON.parse(options.data.optionsJson)),
    });
    if (!isPublicKeyCredential(credential)) return { kind: 'failed' };

    const response = await registerPasskey({ credentialJson: JSON.stringify(credential.toJSON()), name });
    if (response.status === 200) return { kind: 'success', value: response.data };
    if (response.status === 409) return { kind: 'failed', message: response.data.detail ?? undefined };
    if (response.status === 400) return { kind: 'failed', message: Object.values(response.data.errors ?? {}).flat()[0] };
    return { kind: 'failed' };
  } catch (error) {
    return fromError(error);
  }
}

function isPublicKeyCredential(credential: Credential | null): credential is PublicKeyCredential {
  return credential !== null && typeof (credential as PublicKeyCredential).toJSON === 'function';
}

function fromError(error: unknown): PasskeyOutcome<never> {
  if (error instanceof DOMException) {
    // The person closed the prompt, or it timed out waiting for them.
    if (error.name === 'NotAllowedError' || error.name === 'AbortError') return { kind: 'cancelled' };
    // `excludeCredentials` found one of this account's passkeys on the authenticator already.
    if (error.name === 'InvalidStateError') return { kind: 'failed', message: 'This device already has a passkey for your account.' };
  }

  return { kind: 'failed' };
}

/** The parts of `navigator` a device name reads, so a test can pass its own. */
export interface DeviceHints {
  userAgent: string;
  userAgentData?: { platform?: string };
}

/**
 * A name for the passkey about to be added, "Chrome on Windows", from what the browser says about itself. The person can
 * change it before adding; it only helps them tell their passkeys apart later.
 */
export function deviceName(hints: DeviceHints = navigator): string {
  const agent = hints.userAgent;
  // Order matters: Edge and Opera also say Chrome, and Chrome also says Safari.
  const browser = /Edg\//.test(agent)
    ? 'Edge'
    : /OPR\/|Opera/.test(agent)
      ? 'Opera'
      : /Firefox\/|FxiOS/.test(agent)
        ? 'Firefox'
        : /Chrome\/|CriOS/.test(agent)
          ? 'Chrome'
          : /Safari\//.test(agent)
            ? 'Safari'
            : undefined;

  const platform = hints.userAgentData?.platform;
  const system = /iPhone|iPad|iPod/.test(agent)
    ? 'iOS'
    : /Android/.test(agent)
      ? 'Android'
      : /CrOS/.test(agent)
        ? 'ChromeOS'
        : /Windows/.test(agent) || platform === 'Windows'
          ? 'Windows'
          : /Mac OS X|Macintosh/.test(agent) || platform === 'macOS'
            ? 'macOS'
            : /Linux/.test(agent) || platform === 'Linux'
              ? 'Linux'
              : undefined;

  if (browser && system) return `${browser} on ${system}`;
  return browser ?? system ?? 'This device';
}
