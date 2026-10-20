import { createSubscriber } from 'svelte/reactivity';

/** How often the clock moves on. Ages and "new today" are shown to the minute. */
const tickMs = 60_000;

let current = Date.now();

const subscribe = createSubscriber((update) => {
  current = Date.now();
  const timer = setInterval(() => {
    current = Date.now();
    update();
  }, tickMs);
  return () => clearInterval(timer);
});

/**
 * The dashboard's one minute clock, so the sidebar's saved-view counts and the views they open agree. It ticks only
 * while an effect or template reads it.
 */
export const clock = {
  get now(): number {
    // Read outside an effect, nothing would keep it ticking, so give the real time.
    if (!$effect.tracking()) return Date.now();
    subscribe();
    return current;
  },
};
