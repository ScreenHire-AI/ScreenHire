/**
 * The plain line for a notification in the bell, made from its kind and payload (docs/architecture/notifications.md). The
 * server stores facts, not prose, so the wording lives here. A new kind adds a line to `lines`; one this code does not know
 * (an older dashboard, a newer API) still reads as a sentence with its link beside it.
 */
type Payload = Readonly<Record<string, string>>;

const dayMs = 24 * 60 * 60 * 1000;

const ended = 'Your free trial has ended';

// Midnight of an instant's day in the browser's zone, as a UTC count, so a daylight saving change does not shift the days.
const localDay = (at: Date) => Date.UTC(at.getFullYear(), at.getMonth(), at.getDate());

// The days left are worked out here from the trial's end, not stored: one row stays right as the days pass, with nothing to
// update each morning (docs/architecture/billing.md, "Trial reminders"). They are calendar days in the browser's zone, as a
// person counts them: a trial that ends on Thursday ends "tomorrow" all of Wednesday, as the email that arrives then says.
function trialEnding(payload: Payload, now: Date): string {
  const endsAt = Date.parse(payload.endsAt ?? '');
  if (Number.isNaN(endsAt)) return 'Your free trial is ending';
  if (endsAt <= now.getTime()) return ended;
  const days = Math.round((localDay(new Date(endsAt)) - localDay(now)) / dayMs);
  if (days <= 0) return 'Your free trial ends today';
  if (days === 1) return 'Your free trial ends tomorrow';
  return `Your free trial ends in ${days} days`;
}

const lines: Record<string, (payload: Payload, now: Date) => string> = {
  'sending-domain-failing': (payload) => `${payload.domain ?? 'Your email domain'} stopped verifying`,
  'trial-ending': trialEnding,
  'trial-ended': () => ended,
  'ai-usage-budget': (payload) => `AI usage is at ${payload.percent ?? ''}% of this month's budget`,
  'ai-usage-cap': () => `AI checks are paused: this month's cap is reached`,
  'ai-usage-spike': () => 'AI usage yesterday was well above the recent average',
};

const generic = 'Something needs a look';

export function notificationLine(kind: string, payload: Payload, now: Date = new Date()): string {
  return lines[kind]?.(payload, now) ?? generic;
}
