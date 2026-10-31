// The careers apply form's telemetry module (verification-engine.md §5 and §10.2): session aggregates per field, computed
// in the browser and sent as cumulative snapshots with navigator.sendBeacon. Framework-free and under 5 KB brotli.
//
// Notice before collection (CLAUDE.md non-negotiable 4): on the step that shows the notice, nothing is recorded until
// the candidate starts on the form below it; later steps start at once. The assistive-technology switch turns it off:
// switching it on discards what was recorded and nothing more is sent. The careers server does not start the module at
// all once the switch is on.
import { Collector, editKind, fieldIdOf, type Snapshot } from './collector.ts';
import { automationMarkers, deviceFingerprint } from './device.ts';

export { Collector, editKind, fieldIdOf, MAX_FIELDS, type EditKind, type FieldAggregate, type Snapshot } from './collector.ts';
export { AUTOMATION_MARKERS, automationMarkers, deviceFingerprint, type AutomationMarker } from './device.ts';

export interface TelemetryOptions {
  /** Where snapshots go: the careers server's same-origin relay. */
  endpoint: string;
  /** The telemetry session from the application form. */
  sessionToken: string;
  /** True on steps after the notice; on the notice's own step, recording starts with the first use of the form. */
  acknowledged?: boolean;
  /** Compute the device fingerprint: the job's basis allows it, with the candidate's tick where it needs one. */
  deviceCheck?: boolean;
  /** Milliseconds, for tests. */
  now?: () => number;
  /** Sends a snapshot, for tests. */
  send?: (endpoint: string, body: string) => void;
  pageViewId?: string;
}

/** The form fields the module fills on submit, when the step has them. */
export const FINAL_SNAPSHOT_FIELD = 'telemetry';
export const FINGERPRINT_FIELD = 'deviceFingerprint';
export const MARKERS_FIELD = 'automationMarkers';
/** The assistive-technology switch. */
export const OPT_OUT_FIELD = 'accommodationRequested';

const TEXT_TYPES = new Set(['text', 'email', 'tel', 'url', 'search', 'number']);

export interface Telemetry {
  /** The next snapshot, or null when nothing is being recorded. */
  snapshot(): Snapshot | null;
  stop(): void;
}

/** Starts recording on `form`. */
export function startTelemetry(form: HTMLFormElement, options: TelemetryOptions): Telemetry {
  const now = options.now ?? (() => performance.now());
  const send = options.send ?? ((endpoint: string, body: string) => void navigator.sendBeacon(endpoint, body));
  const pageViewId = options.pageViewId ?? crypto.randomUUID();
  const input = (name: string) => {
    const element = form.elements.namedItem(name);
    return element instanceof HTMLInputElement ? element : null;
  };
  const optOut = input(OPT_OUT_FIELD);
  const off = () => optOut?.checked === true;
  const markers = automationMarkers();
  let collector: Collector | null = null;
  let sequence = 0;
  let leaving = false;
  let lastSent = '';

  const start = () => {
    if (collector || off()) return;
    collector = new Collector();
    if (options.deviceCheck) {
      void deviceFingerprint().then((fingerprint) => {
        const target = input(FINGERPRINT_FIELD);
        if (fingerprint && target && !off()) target.value = fingerprint;
      });
    }
  };

  const tracked = (target: EventTarget | null): [string, HTMLInputElement | HTMLTextAreaElement] | null => {
    const textual = target instanceof HTMLTextAreaElement || (target instanceof HTMLInputElement && TEXT_TYPES.has(target.type));
    if (!textual || target.form !== form) return null;
    const id = fieldIdOf(target.name);
    return id ? [id, target] : null;
  };

  // The next snapshot and its content without the sequence, so an unchanged page sends nothing twice. A page view that
  // measured nothing (a button pressed on an empty form) has nothing to send.
  const next = (): [Snapshot, string] | null => {
    if (!collector || off()) return null;
    const { fields, visibilityChanges } = collector.aggregates(now());
    if (fields.length === 0 && visibilityChanges === 0) return null;
    const page = { visibilityChanges, automationMarkers: markers };
    sequence++;
    return [{ sessionToken: options.sessionToken, pageViewId, sequence, fields, page }, JSON.stringify([fields, page])];
  };

  const flush = () => {
    const snapshot = next();
    if (!snapshot || snapshot[1] === lastSent) return;
    lastSent = snapshot[1];
    send(options.endpoint, JSON.stringify(snapshot[0]));
  };

  const onFocusIn = (event: Event) => {
    // Reaching the switch is not using the form: keyboard and screen-reader users arrive at it first.
    if (event.target !== optOut) start();
    const field = tracked(event.target);
    if (!field || !collector) return;
    const [id, element] = field;
    collector.focus(id, element.value.length, now());
    if (autofilled(element)) collector.autofilled(id);
  };

  const onFocusOut = (event: Event) => {
    const field = tracked(event.target);
    if (field && collector && !off()) collector.blur(field[0], now());
  };

  const onInput = (event: Event) => {
    const field = tracked(event.target);
    if (!field || !collector || off()) return;
    // Only the kind of edit and the new length: never `data`, the value itself or the clipboard.
    collector.edit(field[0], editKind(event instanceof InputEvent ? event.inputType : undefined), field[1].value.length);
  };

  const onChange = (event: Event) => {
    if (event.target === optOut && off()) {
      // Switched on: forget everything, including the fields the module would fill.
      collector = null;
      for (const name of [FINAL_SNAPSHOT_FIELD, FINGERPRINT_FIELD, MARKERS_FIELD]) {
        const target = input(name);
        if (target) target.value = '';
      }
    }
  };

  // On the last step the final snapshot travels with the form post, so the API has it before analysis starts. The beacon
  // on leaving still goes too: a post the server sends back with an error would otherwise lose this page view.
  const onSubmit = () => {
    leaving = true;
    const finalField = input(FINAL_SNAPSHOT_FIELD);
    if (!finalField) return;
    const final = next();
    finalField.value = final ? JSON.stringify(final[0]) : '';
    const markersField = input(MARKERS_FIELD);
    if (markersField) markersField.value = collector && !off() ? markers.join(',') : '';
  };

  const onVisibility = () => {
    if (document.visibilityState !== 'hidden') return;
    // Leaving for the next step hides the page too; that is not switching away.
    if (collector && !off() && !leaving) collector.hidden();
    flush();
  };

  const onPageShow = (event: PageTransitionEvent) => {
    if (event.persisted) leaving = false; // back from the history cache
  };

  form.addEventListener('focusin', onFocusIn);
  form.addEventListener('focusout', onFocusOut);
  form.addEventListener('input', onInput);
  form.addEventListener('change', onChange);
  form.addEventListener('submit', onSubmit);
  document.addEventListener('visibilitychange', onVisibility);
  window.addEventListener('pagehide', flush);
  window.addEventListener('pageshow', onPageShow);
  if (options.acknowledged) start();

  return {
    snapshot: () => next()?.[0] ?? null,
    stop() {
      form.removeEventListener('focusin', onFocusIn);
      form.removeEventListener('focusout', onFocusOut);
      form.removeEventListener('input', onInput);
      form.removeEventListener('change', onChange);
      form.removeEventListener('submit', onSubmit);
      document.removeEventListener('visibilitychange', onVisibility);
      window.removeEventListener('pagehide', flush);
      window.removeEventListener('pageshow', onPageShow);
      collector = null;
    },
  };
}

function autofilled(element: Element): boolean {
  try {
    return element.matches(':autofill');
  } catch {
    try {
      return element.matches(':-webkit-autofill');
    } catch {
      return false;
    }
  }
}
