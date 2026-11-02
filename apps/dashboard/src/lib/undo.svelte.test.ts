import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { UndoOffer } from './undo.svelte.js';

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('the offer to undo', () => {
  it('stays for 10 seconds, then goes', () => {
    const offer = new UndoOffer();
    offer.offer('Deleted "Weekly".', async () => null);

    expect(offer.message).toBe('Deleted "Weekly".');
    expect(offer.open).toBe(true);
    vi.advanceTimersByTime(9_999);
    expect(offer.open).toBe(true);
    vi.advanceTimersByTime(1);
    expect(offer.open).toBe(false);
  });

  it('runs Undo once and closes the offer', async () => {
    const offer = new UndoOffer();
    const undo = vi.fn(async () => null);
    offer.offer('Deleted "Weekly".', undo);

    await Promise.all([offer.run(), offer.run()]);

    expect(undo).toHaveBeenCalledTimes(1);
    expect(offer.open).toBe(false);
  });

  it('says why Undo failed, for a while', async () => {
    const offer = new UndoOffer();
    offer.offer('Deleted "Weekly".', async () => 'Could not bring the view back (HTTP 409).');

    await offer.run();

    expect(offer.message).toBe('');
    expect(offer.failure).toBe('Could not bring the view back (HTTP 409).');
    vi.advanceTimersByTime(10_000);
    expect(offer.open).toBe(false);
  });

  it('waits while the person points at or focuses the notice, then gives the full time again', () => {
    const offer = new UndoOffer();
    offer.offer('Deleted "Weekly".', async () => null);

    vi.advanceTimersByTime(8_000);
    offer.hold();
    vi.advanceTimersByTime(60_000);
    expect(offer.open).toBe(true);
    offer.release();
    vi.advanceTimersByTime(9_999);
    expect(offer.open).toBe(true);
    vi.advanceTimersByTime(1);
    expect(offer.open).toBe(false);
  });

  it('keeps waiting when a new offer arrives while the person holds the notice', () => {
    const offer = new UndoOffer();
    offer.offer('Deleted "One".', async () => null);
    offer.hold();

    offer.offer('Deleted "Two".', async () => null);
    vi.advanceTimersByTime(60_000);
    expect(offer.message).toBe('Deleted "Two".');

    offer.release();
    vi.advanceTimersByTime(10_000);
    expect(offer.open).toBe(false);
  });

  it('starts the clock on the next offer after a held one ended', () => {
    const offer = new UndoOffer();
    offer.offer('Deleted "One".', async () => null);
    offer.hold();
    offer.dismiss();

    offer.offer('Deleted "Two".', async () => null);
    vi.advanceTimersByTime(10_000);

    expect(offer.open).toBe(false);
  });

  it('leaves a newer offer alone when an earlier Undo finishes', async () => {
    const offer = new UndoOffer();
    let finish: (failure: string | null) => void = () => {};
    offer.offer('Deleted "One".', () => new Promise((resolve) => (finish = resolve)));
    const running = offer.run();

    const second = vi.fn(async () => null);
    offer.offer('Deleted "Two".', second);
    finish(null);
    await running;

    expect(offer.message).toBe('Deleted "Two".');
    await offer.run();
    expect(second).toHaveBeenCalledOnce();
  });

  it('lets a second offer replace the first', () => {
    const offer = new UndoOffer();
    const first = vi.fn(async () => null);
    offer.offer('Deleted "One".', first);
    vi.advanceTimersByTime(6_000);
    offer.offer('Deleted "Two".', async () => null);

    expect(offer.message).toBe('Deleted "Two".');
    vi.advanceTimersByTime(6_000);
    expect(offer.open).toBe(true);
    vi.advanceTimersByTime(4_000);
    expect(offer.open).toBe(false);
    expect(first).not.toHaveBeenCalled();
  });
});
