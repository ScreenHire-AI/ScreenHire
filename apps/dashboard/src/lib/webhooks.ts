import type { WebhookDeliveryStatus, WebhookEndpointEntry } from '@screenhire/api-client';
import type { Status } from '#lib/components/status-badge.svelte';

/** An event type's name in words: `application.submitted` is "Application submitted". */
export function eventLabel(type: string): string {
  const words = type.replace(/[._]/g, ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/**
 * An endpoint's status: on; off because the worker gave up after failed deliveries, which needs someone (verify); or
 * turned off by a person, which is calm.
 */
export function endpointStatus(endpoint: Pick<WebhookEndpointEntry, 'enabled' | 'disabledReason'>): { status: Status; label: string } {
  if (endpoint.enabled) return { status: 'clear', label: 'On' };
  return endpoint.disabledReason ? { status: 'verify', label: 'Off after failures' } : { status: 'neutral', label: 'Off' };
}

/** A delivery's status: delivered is calm, a retry in progress needs a look, a delivery that gave up needs someone. */
export const deliveryStatus: Record<WebhookDeliveryStatus, { status: Status; label: string }> = {
  Succeeded: { status: 'clear', label: 'Delivered' },
  Pending: { status: 'review', label: 'Pending' },
  Failed: { status: 'verify', label: 'Failed' },
  Skipped: { status: 'neutral', label: 'Skipped' },
};
