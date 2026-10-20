// What the careers server accepts from the telemetry module (packages/telemetry) before passing it to the API: one
// snapshot shape, strict, so nothing but counts and known ids travels on (integrity-engine.md §10.2). The API checks it
// all again, including the field ids against the job's questions.
import type { TelemetrySnapshotInput } from '@screenhire/api-client';
import { z } from 'astro/zod';

/** The largest snapshot taken, as the API (`PublicTelemetryEndpoints.MaxBodyBytes`). */
export const MAX_TELEMETRY_BYTES = 32 * 1024;

/** The automation markers the module reports. */
export const AUTOMATION_MARKERS = ['webdriver', 'headless', 'phantom', 'selenium', 'nightmare', 'chromedriver'] as const;

const count = z.number().int().min(0).max(1_000_000_000);

const snapshotSchema = z.strictObject({
  sessionToken: z.string().min(1).max(1024),
  pageViewId: z.uuid(),
  sequence: z.number().int().min(1).max(10_000),
  fields: z
    .array(
      z.strictObject({
        fieldId: z.string().regex(/^[A-Za-z0-9_-]{1,40}$/),
        pasteCount: count,
        pastedCharacters: count,
        typedCharacters: count,
        focusCount: count,
        blurCount: count,
        activeMilliseconds: count,
        autofilled: z.boolean(),
      }),
    )
    .max(110),
  page: z.strictObject({
    visibilityChanges: count,
    automationMarkers: z.array(z.enum(AUTOMATION_MARKERS)).max(AUTOMATION_MARKERS.length),
  }),
});

/** A snapshot from its JSON text, or null when it is too large, malformed or carries anything unexpected. */
export function parseSnapshot(raw: unknown): TelemetrySnapshotInput | null {
  if (typeof raw !== 'string' || raw.length === 0 || raw.length > MAX_TELEMETRY_BYTES) return null;
  let value: unknown;
  try {
    value = JSON.parse(raw);
  } catch {
    return null;
  }

  const parsed = snapshotSchema.safeParse(value);
  return parsed.success ? parsed.data : null;
}

/** The module's device fingerprint: a SHA-256 in hex, or null. */
export const parseFingerprint = (raw: unknown): string | null =>
  typeof raw === 'string' && /^[0-9a-f]{64}$/.test(raw) ? raw : null;

/** The module's automation markers, comma-separated, as known markers only. */
export const parseMarkers = (raw: unknown): string[] =>
  typeof raw === 'string'
    ? [...new Set(raw.split(',').filter((marker): marker is (typeof AUTOMATION_MARKERS)[number] => (AUTOMATION_MARKERS as readonly string[]).includes(marker)))]
    : [];
