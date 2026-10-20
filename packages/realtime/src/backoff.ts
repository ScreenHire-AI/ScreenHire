/** The first retry waits about this long; each later one doubles it, up to `maxDelayMs`. */
const baseDelayMs = 1_000;
const maxDelayMs = 30_000;

/**
 * How long to wait before reconnect attempt `attempt` (0 for the first): exponential backoff capped at 30 seconds,
 * with up to 20% random jitter so tabs that lost the connection together do not all come back in the same instant.
 * It never gives up: the dashboard stays useful offline and catches up when the API is back.
 */
export function reconnectDelay(attempt: number, random: () => number = Math.random): number {
  const exponential = Math.min(maxDelayMs, baseDelayMs * 2 ** Math.max(0, attempt));
  return Math.round(exponential * (0.8 + 0.2 * random()));
}
