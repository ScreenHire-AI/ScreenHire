import { invalidate } from '$app/navigation';
import {
  createConnectionState,
  invalidateOnEvents,
  RealtimeClient,
  type ConnectionState,
  type InvalidationMap,
} from '@screenhire/realtime';

/** The SvelteKit dependency the jobs list's `load` declares; live job events invalidate it. */
export const jobsDependency = 'api:jobs';

/**
 * Which `load` dependencies each tenant event makes stale. Add a line when a page shows what an event changes.
 * HiringTemplateVersionPublished is here because the jobs list shows "template update available". The application
 * events and JobPostingsChanged (a job's board postings) have no page yet; the pipeline and distribution views map them
 * when they land.
 */
export const invalidations: InvalidationMap = {
  JobPublished: [jobsDependency],
  JobClosed: [jobsDependency],
  HiringTemplateVersionPublished: [jobsDependency],
};

export interface LiveUpdates {
  /** The connection's status, as runes state, for the shell's indicator. */
  readonly connection: ConnectionState;
  /** Receives one record's events (`topic` such as `job`) until the returned function is called. */
  subscribe(topic: string, id: string): () => void;
  stop(): Promise<void>;
}

/**
 * Connects the signed-in shell to the API's real-time hub. Events re-run the `load` functions that depend on what
 * changed, and a reconnect re-runs all of them, so the screen catches up on anything it missed.
 */
export function startLiveUpdates(): LiveUpdates {
  const client = new RealtimeClient();
  const connection = createConnectionState(client);
  const stopInvalidating = invalidateOnEvents(client, invalidations, invalidate);
  void client.start();

  return {
    connection,
    subscribe: (topic, id) => client.subscribe(topic, id),
    async stop() {
      stopInvalidating();
      connection.dispose();
      await client.stop();
    },
  };
}
