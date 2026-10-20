import { HttpError, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { reconnectDelay } from './backoff.js';
import { parseRealtimeEvent, type RealtimeEvent } from './events.js';

/** The API's hub, same-origin: the dashboard proxies `/v1` (with WebSockets) to the API. */
export const defaultHubUrl = '/v1/hubs/events';

/**
 * Where the connection stands. `connecting` is the first attempt and every attempt after a full drop; `reconnecting`
 * is SignalR's automatic reconnect after a connection that was up; `disconnected` means stopped, or refused because
 * the session is gone (401), and nothing more will be tried.
 */
export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/** The part of `@microsoft/signalr`'s `HubConnection` the client uses, so tests can pass a fake. */
export interface HubConnectionLike {
  start(): Promise<void>;
  stop(): Promise<void>;
  on(methodName: string, handler: (...args: unknown[]) => void): void;
  invoke(methodName: string, ...args: unknown[]): Promise<unknown>;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
  onclose(callback: (error?: Error) => void): void;
}

export interface RealtimeClientOptions {
  /** The hub's URL; defaults to {@link defaultHubUrl}. */
  url?: string;
  /** Builds the connection. Tests pass a fake; the default is a real SignalR connection with automatic reconnect. */
  createConnection?: (url: string, retryDelay: (attempt: number) => number) => HubConnectionLike;
  /** Delay before reconnect attempt n (0-based); defaults to {@link reconnectDelay}. */
  retryDelay?: (attempt: number) => number;
  /** Timer, swappable for tests. */
  setTimeout?: (callback: () => void, ms: number) => unknown;
}

function createSignalRConnection(url: string, retryDelay: (attempt: number) => number): HubConnectionLike {
  return new HubConnectionBuilder()
    .withUrl(url, { withCredentials: true })
    // Never returns null, so SignalR keeps trying for as long as the page is open.
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => retryDelay(context.previousRetryCount) })
    .configureLogging(LogLevel.Warning)
    .build();
}

const subscriptionKey = (topic: string, id: string) => `${topic}\u0000${id}`;

/**
 * The dashboard's connection to the API's real-time hub. It keeps the connection up (SignalR's automatic reconnect
 * after a drop, plus its own retry with the same backoff when the first start or a reconnect fails), re-subscribes to
 * every record after it comes back, and tells listeners when to refetch: events sent while it was away are gone.
 */
export class RealtimeClient {
  #status: ConnectionStatus = 'disconnected';
  #stopped = true;
  #attempt = 0;
  #everConnected = false;
  readonly #connection: HubConnectionLike;
  readonly #retryDelay: (attempt: number) => number;
  readonly #setTimeout: (callback: () => void, ms: number) => unknown;
  readonly #eventHandlers = new Set<(event: RealtimeEvent) => void>();
  readonly #statusHandlers = new Set<(status: ConnectionStatus) => void>();
  readonly #resumedHandlers = new Set<() => void>();
  readonly #subscriptions = new Map<string, { topic: string; id: string }>();

  constructor(options: RealtimeClientOptions = {}) {
    this.#retryDelay = options.retryDelay ?? reconnectDelay;
    this.#setTimeout = options.setTimeout ?? ((callback, ms) => globalThis.setTimeout(callback, ms));
    const create = options.createConnection ?? createSignalRConnection;
    this.#connection = create(options.url ?? defaultHubUrl, this.#retryDelay);

    this.#connection.on('ReceiveEvent', (raw) => this.#receive(raw));
    this.#connection.onreconnecting(() => this.#setStatus('reconnecting'));
    this.#connection.onreconnected(() => void this.#connected());
    // SignalR gave up on this connection (or the server closed it). Start over unless someone stopped the client.
    this.#connection.onclose(() => {
      if (!this.#stopped) {
        this.#setStatus('connecting');
        this.#scheduleStart();
      }
    });
  }

  get status(): ConnectionStatus {
    return this.#status;
  }

  /** Connects, retrying with backoff until it succeeds, {@link stop} is called, or the API answers 401. */
  start(): Promise<void> {
    if (!this.#stopped) {
      return Promise.resolve();
    }

    this.#stopped = false;
    this.#attempt = 0;
    this.#setStatus('connecting');
    return this.#tryStart();
  }

  async stop(): Promise<void> {
    this.#stopped = true;
    this.#setStatus('disconnected');
    await this.#connection.stop();
  }

  /** Calls `handler` with every event this client knows the type of. Returns a function that removes it. */
  onEvent(handler: (event: RealtimeEvent) => void): () => void {
    return this.#add(this.#eventHandlers, handler);
  }

  onStatusChange(handler: (status: ConnectionStatus) => void): () => void {
    return this.#add(this.#statusHandlers, handler);
  }

  /**
   * Calls `handler` each time the connection is back after it was lost. Events sent in between were missed, so
   * refetch whatever is on screen.
   */
  onResumed(handler: () => void): () => void {
    return this.#add(this.#resumedHandlers, handler);
  }

  /**
   * Receives the events for one record (`topic` such as `job`, and its id) until the returned function is called. The
   * hub checks the record belongs to the caller's organization. The subscription survives reconnects.
   */
  subscribe(topic: string, id: string): () => void {
    const key = subscriptionKey(topic, id);
    this.#subscriptions.set(key, { topic, id });
    if (this.#status === 'connected') {
      void this.#invoke('Subscribe', topic, id);
    }

    return () => {
      if (this.#subscriptions.delete(key) && this.#status === 'connected') {
        void this.#invoke('Unsubscribe', topic, id);
      }
    };
  }

  async #tryStart(): Promise<void> {
    try {
      await this.#connection.start();
    } catch (error) {
      if (this.#stopped) {
        return;
      }

      if (error instanceof HttpError && error.statusCode === 401) {
        // Signed out or the session expired: retrying cannot help. The next navigation sends the person to sign in.
        this.#stopped = true;
        this.#setStatus('disconnected');
        return;
      }

      this.#scheduleStart();
      return;
    }

    if (this.#stopped) {
      await this.#connection.stop();
      return;
    }

    await this.#connected();
  }

  #scheduleStart(): void {
    const delay = this.#retryDelay(this.#attempt++);
    this.#setTimeout(() => {
      if (!this.#stopped) {
        void this.#tryStart();
      }
    }, delay);
  }

  async #connected(): Promise<void> {
    this.#attempt = 0;
    this.#setStatus('connected');
    await Promise.all([...this.#subscriptions.values()].map(({ topic, id }) => this.#invoke('Subscribe', topic, id)));
    if (this.#everConnected) {
      for (const handler of this.#resumedHandlers) {
        handler();
      }
    }

    this.#everConnected = true;
  }

  async #invoke(method: string, topic: string, id: string): Promise<void> {
    try {
      await this.#connection.invoke(method, topic, id);
    } catch (error) {
      // A record that was deleted, or moved out of reach, refuses the subscription. The screen still works by refetching.
      console.warn(`Live updates: ${method} ${topic} ${id} failed.`, error);
    }
  }

  #receive(raw: unknown): void {
    let event: RealtimeEvent | null;
    try {
      event = parseRealtimeEvent(raw);
    } catch (error) {
      console.error('Live updates: an event did not match its schema.', error);
      return;
    }

    if (event) {
      for (const handler of this.#eventHandlers) {
        handler(event);
      }
    }
  }

  #setStatus(status: ConnectionStatus): void {
    if (status !== this.#status) {
      this.#status = status;
      for (const handler of this.#statusHandlers) {
        handler(status);
      }
    }
  }

  #add<T>(handlers: Set<T>, handler: T): () => void {
    handlers.add(handler);
    return () => handlers.delete(handler);
  }
}
