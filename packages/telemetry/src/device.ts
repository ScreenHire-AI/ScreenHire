// Page-level automation markers and the device fingerprint. Both read what the browser says about itself, never anything
// the candidate typed.

/** The markers the API knows (`TelemetrySnapshotInputValidator.KnownMarkers`), in a fixed order. */
export type AutomationMarker = 'webdriver' | 'headless' | 'phantom' | 'selenium' | 'nightmare' | 'chromedriver';

/** Automation frameworks' tell-tale traits: `navigator.webdriver`, a headless user agent and globals drivers inject. */
export function automationMarkers(w: Window = window): AutomationMarker[] {
  const found: AutomationMarker[] = [];
  const has = (key: string) => key in w;
  if (w.navigator.webdriver) found.push('webdriver');
  if (/headless/i.test(w.navigator.userAgent)) found.push('headless');
  if (has('callPhantom') || has('_phantom')) found.push('phantom');
  if (has('_selenium') || has('__selenium_unwrapped') || w.document.documentElement.hasAttribute('webdriver')) found.push('selenium');
  if (has('__nightmare')) found.push('nightmare');
  if (Object.keys(w.document).some((key) => key.startsWith('$cdc_') || key.startsWith('cdc_'))) found.push('chromedriver');
  return found;
}

/**
 * A device fingerprint: SHA-256 over coarse traits the browser reports (user agent, languages, screen, time zone,
 * hardware concurrency). No canvas, audio or font probing. The API keyed-hashes it again per organization before
 * storing it, so it never matches across employers (integrity-engine.md §10.4). Null where Web Crypto is missing.
 */
export async function deviceFingerprint(w: Window = window): Promise<string | null> {
  const subtle = w.crypto?.subtle;
  if (!subtle) return null;
  const { navigator: n, screen: s } = w;
  const traits = [
    n.userAgent,
    n.language,
    (n.languages ?? []).join(','),
    n.hardwareConcurrency,
    (n as Navigator & { deviceMemory?: number }).deviceMemory,
    n.maxTouchPoints,
    s.width,
    s.height,
    s.colorDepth,
    w.devicePixelRatio,
    Intl.DateTimeFormat().resolvedOptions().timeZone,
  ].join('|');
  const digest = await subtle.digest('SHA-256', new TextEncoder().encode(traits));
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, '0')).join('');
}
