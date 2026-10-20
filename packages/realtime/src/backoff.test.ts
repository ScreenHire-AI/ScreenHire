import { describe, expect, it } from 'vitest';
import { reconnectDelay } from './backoff.js';

describe('reconnectDelay', () => {
  const noJitter = () => 1;

  it('doubles from one second', () => {
    expect([0, 1, 2, 3, 4].map((attempt) => reconnectDelay(attempt, noJitter))).toEqual([1_000, 2_000, 4_000, 8_000, 16_000]);
  });

  it('caps at thirty seconds and never gives up', () => {
    expect(reconnectDelay(5, noJitter)).toBe(30_000);
    expect(reconnectDelay(500, noJitter)).toBe(30_000);
  });

  it('takes up to a fifth off at random so clients spread out', () => {
    expect(reconnectDelay(3, () => 0)).toBe(6_400);
    expect(reconnectDelay(3, () => 0.5)).toBe(7_200);
  });
});
