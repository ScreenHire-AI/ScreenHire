import type { WebhookDeliveryStatus, WebhookEndpointEntry } from '@screenhire/api-client';
import type { StatusName } from '#lib/status.js';

/** An event type's name in words: `application.submitted` is "Application submitted". */
export function eventLabel(type: string): string {
  const words = type.replace(/[._]/g, ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/**
 * An endpoint's status (design system section 3.2): delivering; turned off because the worker gave up after failed
 * deliveries, which needs fixing; or turned off by a person, which is calm.
 */
export function endpointStatus(endpoint: Pick<WebhookEndpointEntry, 'enabled' | 'disabledReason'>): { status: StatusName; label: string } {
  if (endpoint.enabled) return { status: 'done', label: 'Delivering' };
  return endpoint.disabledReason ? { status: 'needsFixes', label: 'Turned off' } : { status: 'notInEffect', label: 'Off' };
}

/**
 * A delivery's status: delivered is calm, a delivery waiting for its next try is grey because nobody needs to act, and
 * a delivery that gave up needs fixing.
 */
export const deliveryStatus: Record<WebhookDeliveryStatus, { status: StatusName; label: string }> = {
  Succeeded: { status: 'done', label: 'Delivered' },
  Pending: { status: 'waiting', label: 'Pending' },
  Failed: { status: 'needsFixes', label: 'Failed' },
  Skipped: { status: 'notInEffect', label: 'Skipped' },
};
