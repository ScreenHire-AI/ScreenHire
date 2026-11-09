/**
 * The plain line for a notification in the bell, made from its kind and payload (docs/architecture/notifications.md). The
 * server stores facts, not prose, so the wording lives here. A new kind adds a line to `lines`; one this code does not know
 * (an older dashboard, a newer API) still reads as a sentence with its link beside it.
 */
const lines: Record<string, (payload: Readonly<Record<string, string>>) => string> = {
  'sending-domain-failing': (payload) => `${payload.domain ?? 'Your email domain'} stopped verifying`,
};

const generic = 'Something needs a look';

export function notificationLine(kind: string, payload: Readonly<Record<string, string>>): string {
  return lines[kind]?.(payload) ?? generic;
}
