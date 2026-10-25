/**
 * The pop-out preview (design system A8): an editor's preview opens in a second window that follows the edits. The two
 * windows share an origin, so the editor posts each change over a `BroadcastChannel` with no server round trip. Each
 * editor has its own channel, named in the window's address, so two editors open at once never cross.
 *
 * In the editor: `const source = createPreviewSource(() => draft)`, and `source.open(url)` from the window icon, where
 * `url` is the editor's preview page. In that page: `const sink = createPreviewSink<Draft>(page.url)`, then draw
 * `sink.value`.
 */

/** The query parameter that carries the channel's id to the preview window. */
export const previewChannelParameter = 'channel';

type PreviewMessage<T> = { type: 'hello' } | { type: 'value'; value: T } | { type: 'closed' };

const channelName = (id: string) => `screenhire:preview:${id}`;

/** The editor's side. It opens its channel only once the window is popped out, so an editor nobody pops out holds none. */
export class PreviewSource<T> {
  readonly id = crypto.randomUUID();

  readonly #read: () => T;
  #channel: BroadcastChannel | undefined;

  constructor(read: () => T) {
    this.#read = read;
    // Every change to what `read` returns reaches the window while it is open.
    $effect(() => {
      const value = $state.snapshot(this.#read()) as T;
      this.#channel?.postMessage({ type: 'value', value } satisfies PreviewMessage<T>);
    });
    $effect(() => () => this.close());
  }

  /** Opens the preview window at `url` (the editor's preview page) and starts sending it the edits. */
  open(url: string | URL): void {
    if (!this.#channel) {
      this.#channel = new BroadcastChannel(channelName(this.id));
      // A window that starts after a change asks for the current value.
      this.#channel.onmessage = (event: MessageEvent<PreviewMessage<T>>) => {
        if (event.data?.type === 'hello') this.#post();
      };
    }
    const address = new URL(url, globalThis.location?.href);
    address.searchParams.set(previewChannelParameter, this.id);
    // A named window: a second click brings the same window back rather than opening another.
    globalThis.open?.(address, `preview-${this.id}`, 'popup,width=480,height=840');
    this.#post();
  }

  /** Tells the window the editor has gone, and stops. */
  close(): void {
    this.#channel?.postMessage({ type: 'closed' } satisfies PreviewMessage<T>);
    this.#channel?.close();
    this.#channel = undefined;
  }

  #post(): void {
    this.#channel?.postMessage({ type: 'value', value: $state.snapshot(this.#read()) as T } satisfies PreviewMessage<T>);
  }
}

/** The preview window's side: the latest value, and whether the editor has closed. */
export class PreviewSink<T> {
  value = $state.raw<T>();
  closed = $state(false);

  readonly #channel: BroadcastChannel | undefined;

  constructor(id: string | null) {
    if (!id) return;
    this.#channel = new BroadcastChannel(channelName(id));
    this.#channel.onmessage = (event: MessageEvent<PreviewMessage<T>>) => {
      if (event.data?.type === 'value') {
        this.value = event.data.value;
        this.closed = false;
      } else if (event.data?.type === 'closed') {
        this.closed = true;
      }
    };
    this.#channel.postMessage({ type: 'hello' } satisfies PreviewMessage<T>);
    $effect(() => () => this.#channel?.close());
  }
}

/** In an editor component's script: the source for its pop-out preview. */
export function createPreviewSource<T>(read: () => T): PreviewSource<T> {
  return new PreviewSource(read);
}

/** In the preview page's script: the sink for the editor named in the page's address. */
export function createPreviewSink<T>(url: { readonly searchParams: { get(name: string): string | null } }): PreviewSink<T> {
  return new PreviewSink<T>(url.searchParams.get(previewChannelParameter));
}
