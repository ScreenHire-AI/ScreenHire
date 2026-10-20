import { describe, expect, it } from 'vitest';
import { RealtimeClient, type HubConnectionLike } from './client.js';
import { createConnectionState } from './connection-state.svelte.js';

const idleConnection: HubConnectionLike = {
  start: () => Promise.resolve(),
  stop: () => Promise.resolve(),
  on: () => {},
  invoke: () => Promise.resolve(),
  onreconnecting: () => {},
  onreconnected: () => {},
  onclose: () => {},
};

describe('createConnectionState', () => {
  it("follows the client's status", async () => {
    const client = new RealtimeClient({ createConnection: () => idleConnection });
    const state = createConnectionState(client);
    expect(state.status).toBe('disconnected');

    await client.start();
    expect(state.status).toBe('connected');

    await client.stop();
    expect(state.status).toBe('disconnected');
  });

  it('stops following once disposed', async () => {
    const client = new RealtimeClient({ createConnection: () => idleConnection });
    const state = createConnectionState(client);

    state.dispose();
    await client.start();

    expect(state.status).toBe('disconnected');
  });
});
