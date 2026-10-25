import type { WebhookDeliveryStatus } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { deliveryStatus, endpointStatus, eventLabel } from './webhooks.js';

describe('eventLabel', () => {
  it('words a dotted event type', () => {
    expect(eventLabel('application.submitted')).toBe('Application submitted');
  });

  it('words an underscored event type', () => {
    expect(eventLabel('job.stage_changed')).toBe('Job stage changed');
  });

  it('copes with an empty type', () => {
    expect(eventLabel('')).toBe('');
  });
});

describe('endpointStatus', () => {
  it('is calm and labelled Delivering while the endpoint is on', () => {
    expect(endpointStatus({ enabled: true, disabledReason: null })).toEqual({ status: 'done', label: 'Delivering' });
  });

  it('needs fixes when the worker turned it off after failures', () => {
    expect(endpointStatus({ enabled: false, disabledReason: 'Failed 10 deliveries in a row' })).toEqual({
      status: 'needsFixes',
      label: 'Turned off',
    });
  });

  it('is calm when a person turned it off', () => {
    expect(endpointStatus({ enabled: false, disabledReason: null })).toEqual({ status: 'notInEffect', label: 'Off' });
  });

  it('is Delivering even when an old reason is still recorded', () => {
    expect(endpointStatus({ enabled: true, disabledReason: 'Failed 10 deliveries in a row' }).label).toBe('Delivering');
  });
});

describe('deliveryStatus', () => {
  it.each<[WebhookDeliveryStatus, string, string]>([
    ['Succeeded', 'done', 'Delivered'],
    ['Pending', 'waiting', 'Pending'],
    ['Failed', 'needsFixes', 'Failed'],
    ['Skipped', 'notInEffect', 'Skipped'],
  ])('shows %s as %s, labelled %s', (status, tone, label) => {
    expect(deliveryStatus[status]).toEqual({ status: tone, label });
  });
});
