import type { RealtimeClient } from './client.js';
import type { RealtimeEvent, RealtimeEventType } from './events.js';

/**
 * Which cached queries each event makes stale, as cache keys. In the dashboard a key is a SvelteKit dependency that a
 * `load` function declares with `depends('api:jobs')`; with a query library it would be a query key.
 */
export type InvalidationMap = Partial<Record<RealtimeEventType, readonly string[]>>;

export interface Invalidator {
  /** Invalidates the keys mapped to the event's type. */
  handle(event: RealtimeEvent): void;
  /** Invalidates every mapped key, for after a reconnect, when events may have been missed. */
  invalidateAll(): void;
}

export function createInvalidator(map: InvalidationMap, invalidate: (key: string) => unknown): Invalidator {
  const allKeys = [...new Set(Object.values(map).flat())];
  return {
    handle(event) {
      for (const key of map[event.type] ?? []) {
        void invalidate(key);
      }
    },
    invalidateAll() {
      for (const key of allKeys) {
        void invalidate(key);
      }
    },
  };
}

/**
 * Invalidates by event type as events arrive, and everything mapped when the connection comes back after a drop.
 * Returns a function that stops both.
 */
export function invalidateOnEvents(client: RealtimeClient, map: InvalidationMap, invalidate: (key: string) => unknown): () => void {
  const invalidator = createInvalidator(map, invalidate);
  const offEvent = client.onEvent((event) => invalidator.handle(event));
  const offResumed = client.onResumed(() => invalidator.invalidateAll());
  return () => {
    offEvent();
    offResumed();
  };
}
