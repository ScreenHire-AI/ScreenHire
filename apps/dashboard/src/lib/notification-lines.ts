/**
 * The plain line for a notification in the bell, made from its kind and payload (docs/architecture/notifications.md). The
 * server stores facts, not prose, so the wording lives here. A new kind adds a line to `lines`; one this code does not know
 * (an older dashboard, a newer API) still reads as a sentence with its link beside it.
 */
const lines: Record<string, (payload: Readonly<Record<string, string>>) => string> = {
  'sending-domain-failing': (payload) => `${payload.domain ?? 'Your email domain'} stopped verifying`,
  'ai-usage-budget': (payload) => `AI usage is at ${payload.percent ?? ''}% of this month's budget`,
  'ai-usage-cap': () => `AI checks are paused: this month's cap is reached`,
  'ai-usage-spike': () => 'AI usage yesterday was well above the recent average',
};

const generic = 'Something needs a look';

export function notificationLine(kind: string, payload: Readonly<Record<string, string>>): string {
  return lines[kind]?.(payload) ?? generic;
}
