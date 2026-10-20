import type { ConnectionStatus, RealtimeClient } from './client.js';

/** A client's connection status as Svelte 5 state, for the dashboard's live indicator. */
export interface ConnectionState {
  readonly status: ConnectionStatus;
  /** Stops following the client. */
  dispose(): void;
}

/** Follows `client`'s status in a `$state` rune, so components that read `status` update when it changes. */
export function createConnectionState(client: RealtimeClient): ConnectionState {
  let status = $state<ConnectionStatus>(client.status);
  const dispose = client.onStatusChange((next) => {
    status = next;
  });

  return {
    get status() {
      return status;
    },
    dispose,
  };
}
