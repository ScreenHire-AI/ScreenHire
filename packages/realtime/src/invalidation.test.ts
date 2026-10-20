import { describe, expect, it, vi } from 'vitest';
import type { RealtimeEvent } from './events.js';
import { createInvalidator } from './invalidation.js';

const ids = {
  organizationId: '0199f0c4-0000-7000-8000-000000000001',
  jobId: '0199f0c4-0000-7000-8000-000000000002',
};

const published: RealtimeEvent = {
  type: 'JobPublished',
  data: { ...ids, publishedAt: '2026-10-20T09:00:00+00:00', version: 1, jobVersionId: null },
};
const checked: RealtimeEvent = {
  type: 'LiveUpdatesChecked',
  data: { organizationId: ids.organizationId, checkId: '0199f0c4-0000-7000-8000-000000000003' },
};

describe('createInvalidator', () => {
  const map = { JobPublished: ['api:jobs', 'api:job'], JobClosed: ['api:jobs'] };

  it("invalidates the keys mapped to the event's type", () => {
    const invalidate = vi.fn();

    createInvalidator(map, invalidate).handle(published);

    expect(invalidate.mock.calls).toEqual([['api:jobs'], ['api:job']]);
  });

  it('leaves the cache alone for an event nothing maps', () => {
    const invalidate = vi.fn();

    createInvalidator(map, invalidate).handle(checked);

    expect(invalidate).not.toHaveBeenCalled();
  });

  it('invalidates every mapped key once after a reconnect', () => {
    const invalidate = vi.fn();

    createInvalidator(map, invalidate).invalidateAll();

    expect(invalidate.mock.calls).toEqual([['api:jobs'], ['api:job']]);
  });
});
