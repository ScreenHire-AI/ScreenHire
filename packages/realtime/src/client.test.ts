import { afterEach, describe, expect, it, vi } from 'vitest';
import { RealtimeClient, type ConnectionHooks, type ConnectionStatus, type HubConnectionLike } from './client.js';
import type { RealtimeEvent } from './events.js';

/** A hub connection the test drives: it records calls and fires SignalR's callbacks on demand. */
class FakeConnection implements HubConnectionLike {
  readonly invocations: unknown[][] = [];
  readonly handlers = new Map<string, (...args: unknown[]) => void>();
  startResults: Array<() => Promise<void>> = [];
  hooks!: ConnectionHooks;
  starts = 0;
  stops = 0;
  #reconnecting?: () => void;
  #reconnected?: () => void;
  #close?: () => void;

  start(): Promise<void> {
    this.starts++;
    const next = this.startResults.shift();
    return next ? next() : Promise.resolve();
  }

  stop(): Promise<void> {
    this.stops++;
    return Promise.resolve();
  }

  on(methodName: string, handler: (...args: unknown[]) => void): void {
    this.handlers.set(methodName, handler);
  }

  invoke(methodName: string, ...args: unknown[]): Promise<unknown> {
    this.invocations.push([methodName, ...args]);
    return Promise.resolve();
  }

  onreconnecting(callback: () => void): void {
    this.#reconnecting = callback;
  }

  onreconnected(callback: () => void): void {
    this.#reconnected = callback;
  }

  onclose(callback: () => void): void {
    this.#close = callback;
  }

  dropAndReconnect(): void {
    this.#reconnecting?.();
    this.#reconnected?.();
  }

  close(): void {
    this.#close?.();
  }

  send(raw: unknown): void {
    this.handlers.get('ReceiveEvent')?.(raw);
  }
}

function setUp() {
  const connection = new FakeConnection();
  const timers: Array<{ callback: () => void; ms: number }> = [];
  const clock = { now: 0 };
  const client = new RealtimeClient({
    createConnection: (_url, hooks) => {
      connection.hooks = hooks;
      return connection;
    },
    retryDelay: (attempt) => 1_000 * (attempt + 1),
    setTimeout: (callback, ms) => timers.push({ callback, ms }),
    now: () => clock.now,
  });
  const statuses: ConnectionStatus[] = [];
  client.onStatusChange((status) => statuses.push(status));
  return { connection, client, timers, statuses, clock };
}

// What HubConnection.start() rejects with when negotiate gets a 401: SignalR's FailedToNegotiateWithServerError, which
// keeps only the message. The status code reaches the client through ConnectionHooks.onUnauthorized instead.
const negotiateFailed = () => new Error("Failed to complete negotiation with the server: Error: Unauthorized: Status code '401'");

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

const jobPublished = {
  type: 'JobPublished',
  data: {
    organizationId: '0199f0c4-0000-7000-8000-000000000001',
    jobId: '0199f0c4-0000-7000-8000-000000000002',
    publishedAt: '2026-10-20T09:00:00+00:00',
  },
};

describe('RealtimeClient', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('connects and reports each status', async () => {
    const { client, statuses } = setUp();

    await client.start();

    expect(client.status).toBe('connected');
    expect(statuses).toEqual(['connecting', 'connected']);
  });

  it('passes parsed events to its handlers and ignores types it does not know', async () => {
    const { client, connection } = setUp();
    const received: RealtimeEvent[] = [];
    client.onEvent((event) => received.push(event));
    await client.start();

    connection.send(jobPublished);
    connection.send({ type: 'SomethingNew', data: {} });

    expect(received).toEqual([jobPublished]);
  });

  it('drops an event that breaks its schema without breaking the connection', async () => {
    const { client, connection } = setUp();
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});
    const received: RealtimeEvent[] = [];
    client.onEvent((event) => received.push(event));
    await client.start();

    connection.send({ type: 'JobPublished', data: { jobId: 42 } });
    connection.send(jobPublished);

    expect(received).toEqual([jobPublished]);
    expect(error).toHaveBeenCalledOnce();
  });

  it('retries a failed first start with backoff until it connects', async () => {
    const { client, connection, timers } = setUp();
    connection.startResults = [() => Promise.reject(new Error('API down')), () => Promise.reject(new Error('API down'))];

    await client.start();
    expect(client.status).toBe('connecting');
    expect(timers.map((timer) => timer.ms)).toEqual([1_000]);

    timers[0]!.callback();
    await flush();
    expect(timers.map((timer) => timer.ms)).toEqual([1_000, 2_000]);

    timers[1]!.callback();
    await flush();
    expect(client.status).toBe('connected');
    expect(connection.starts).toBe(3);
  });

  it('stops trying when the API answers 401: the session is gone', async () => {
    const { client, connection, timers } = setUp();
    connection.startResults = [
      () => {
        connection.hooks.onUnauthorized();
        return Promise.reject(negotiateFailed());
      },
    ];

    await client.start();

    expect(client.status).toBe('disconnected');
    expect(timers).toEqual([]);
  });

  it('keeps retrying a start that fails for any other reason', async () => {
    const { client, connection, timers } = setUp();
    connection.startResults = [() => Promise.reject(new Error('Failed to fetch'))];

    await client.start();

    expect(client.status).toBe('connecting');
    expect(timers.map((timer) => timer.ms)).toEqual([1_000]);
  });

  it('gives up reconnecting once the session is gone', async () => {
    const { client, connection, timers } = setUp();
    await client.start();
    expect(connection.hooks.reconnectDelay(0)).toBe(1_000);

    // SignalR's automatic reconnect negotiates again, gets 401, asks for the next delay, then closes.
    connection.hooks.onUnauthorized();
    expect(connection.hooks.reconnectDelay(1)).toBeNull();
    connection.close();

    expect(client.status).toBe('disconnected');
    expect(timers).toEqual([]);
  });

  it('keeps the backoff growing when the server closes the connection soon after it opens', async () => {
    const { client, connection, timers, clock } = setUp();
    await client.start();

    connection.close();
    timers[0]!.callback();
    await flush();
    clock.now += 500;
    connection.close();

    expect(timers.map((timer) => timer.ms)).toEqual([1_000, 2_000]);
  });

  it('starts the backoff over after a connection that stayed up', async () => {
    const { client, connection, timers, clock } = setUp();
    await client.start();
    connection.close();
    timers[0]!.callback();
    await flush();

    clock.now += 60_000;
    connection.close();

    expect(timers.map((timer) => timer.ms)).toEqual([1_000, 1_000]);
  });

  it('subscribes once connected, and again after every reconnect', async () => {
    const { client, connection } = setUp();
    client.subscribe('job', 'job-1');
    await client.start();
    expect(connection.invocations).toEqual([['Subscribe', 'job', 'job-1']]);

    connection.dropAndReconnect();
    await flush();

    expect(connection.invocations).toEqual([
      ['Subscribe', 'job', 'job-1'],
      ['Subscribe', 'job', 'job-1'],
    ]);
  });

  it('unsubscribes and stops re-subscribing a record that is no longer wanted', async () => {
    const { client, connection } = setUp();
    await client.start();
    const unsubscribe = client.subscribe('job', 'job-1');

    unsubscribe();
    connection.dropAndReconnect();
    await flush();

    expect(connection.invocations).toEqual([
      ['Subscribe', 'job', 'job-1'],
      ['Unsubscribe', 'job', 'job-1'],
    ]);
  });

  it('reports reconnecting, then tells listeners to refetch once it is back', async () => {
    const { client, connection, statuses } = setUp();
    const resumed = vi.fn();
    client.onResumed(resumed);
    await client.start();
    expect(resumed).not.toHaveBeenCalled();

    connection.dropAndReconnect();
    await flush();

    expect(statuses).toEqual(['connecting', 'connected', 'reconnecting', 'connected']);
    expect(resumed).toHaveBeenCalledOnce();
  });

  it('starts over after the connection closes, and catches up when it is back', async () => {
    const { client, connection, timers } = setUp();
    const resumed = vi.fn();
    client.onResumed(resumed);
    client.subscribe('job', 'job-1');
    await client.start();

    connection.close();
    expect(client.status).toBe('connecting');
    timers[0]!.callback();
    await flush();

    expect(client.status).toBe('connected');
    expect(connection.starts).toBe(2);
    expect(connection.invocations).toHaveLength(2);
    expect(resumed).toHaveBeenCalledOnce();
  });

  it('stays down after stop()', async () => {
    const { client, connection, timers } = setUp();
    await client.start();

    await client.stop();
    connection.close();

    expect(client.status).toBe('disconnected');
    expect(connection.stops).toBe(1);
    expect(timers).toEqual([]);
  });

  it('stops on a 401 from the real SignalR connection', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 401, statusText: 'Unauthorized' })));
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const timers: number[] = [];
    const client = new RealtimeClient({
      url: 'http://localhost:5080/v1/hubs/events',
      setTimeout: (_callback, ms) => timers.push(ms),
    });

    await client.start();

    expect(fetch).toHaveBeenCalledOnce();
    expect(client.status).toBe('disconnected');
    expect(timers).toEqual([]);
  });
});
