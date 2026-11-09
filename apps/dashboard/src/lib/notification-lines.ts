/**
 * The plain line for a notification in the bell, made from its kind and payload (docs/architecture/notifications.md). The
 * server stores facts, not prose, so the wording lives here. A new kind adds a line to `lines`; one this code does not know
 * (an older dashboard, a newer API) still reads as a sentence with its link beside it.
 */
type Payload = Readonly<Record<string, string>>;

const dayMs = 24 * 60 * 60 * 1000;

const ended = 'Your free trial has ended. Choose a plan to open new jobs';

// The days left are worked out here from the trial's end, not stored: one row stays right as the days pass, with nothing to
// update each morning (docs/architecture/billing.md, "Trial reminders").
function trialEnding(payload: Payload, now: Date): string {
  const endsAt = Date.parse(payload.endsAt ?? '');
  if (Number.isNaN(endsAt)) return 'Your free trial is ending. Choose a plan to keep opening jobs';
  const left = endsAt - now.getTime();
  if (left <= 0) return ended;
  const days = Math.ceil(left / dayMs);
  if (left < dayMs) return 'Your free trial ends in less than a day. Choose a plan to keep opening jobs';
  if (days === 1) return 'Your free trial ends tomorrow. Choose a plan to keep opening jobs';
  return `Your free trial ends in ${days} days. Choose a plan to keep opening jobs`;
}

const lines: Record<string, (payload: Payload, now: Date) => string> = {
  'sending-domain-failing': (payload) => `${payload.domain ?? 'Your email domain'} stopped verifying`,
  'trial-ending': trialEnding,
  'trial-ended': () => ended,
};

const generic = 'Something needs a look';

export function notificationLine(kind: string, payload: Payload, now: Date = new Date()): string {
  return lines[kind]?.(payload, now) ?? generic;
}
