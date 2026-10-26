<script lang="ts">
  import { getDownloadApplicationFileUrl, type ApplicationFileSummary } from '@screenhire/api-client';
  import QueryError from '#lib/components/query-error.svelte';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { maxPreviewPages, readFrameMessage, type RenderRequest } from '#lib/file-viewer/protocol.js';
  import { cn } from '#lib/utils.js';

  // A candidate's PDF, drawn in isolation, for the full-window file viewer. Uploaded files are untrusted, so the
  // file is drawn inside a sandboxed frame with an opaque origin and a policy that allows only the viewer's own script
  // (file-viewer-plugin.ts): nothing in the file can reach the dashboard's cookies, DOM or API. The dashboard fetches the
  // bytes and hands them over; the frame answers with its height and page count, and nothing else is read from it. Show
  // it only for a file `canView` (#lib/files.ts) allows.
  let {
    applicationId,
    file,
    pageCount = $bindable(),
    class: className,
  }: {
    applicationId: string;
    file: ApplicationFileSummary;
    /** The file's page count, once the frame has drawn it. */
    pageCount?: number;
    /** The box the pages sit in while they load and once drawn. */
    class?: string;
  } = $props();

  let viewer = $state<string>();
  let frame = $state<HTMLIFrameElement>();
  let bytes = $state.raw<ArrayBuffer>();
  let ready = $state(false);
  let height = $state(0);
  let outcome = $state<'loading' | 'shown' | 'unreadable' | 'unavailable'>('loading');
  let attempt = $state(0);
  let sent = false;

  $effect(() => {
    // Try again re-imports too, in case the viewer was what failed to load.
    void attempt;
    let current = true;
    // The viewer carries pdf.js, so it loads only when a file is on screen.
    import('virtual:file-viewer').then(
      (module) => current && (viewer = module.viewerDocument),
      () => current && (outcome = 'unavailable'),
    );
    return () => (current = false);
  });

  $effect(() => {
    const url = getDownloadApplicationFileUrl(applicationId, file.id);
    void attempt;
    bytes = undefined;
    ready = false;
    sent = false;
    height = 0;
    pageCount = undefined;
    outcome = 'loading';

    // The generated downloadApplicationFile reads its response as JSON, and this route redirects to the file itself, so
    // the bytes come from a plain fetch of the generated URL. Storage allows the cross-origin read (apply-flow.md).
    const controller = new AbortController();
    fetch(url, { signal: controller.signal })
      .then((response) => {
        if (!response.ok) throw new Error(`The file answered HTTP ${response.status}.`);
        return response.arrayBuffer();
      })
      .then((data) => (bytes = data))
      .catch(() => {
        if (!controller.signal.aborted) outcome = 'unavailable';
      });
    return () => controller.abort();
  });

  $effect(() => {
    if (!ready || !bytes || sent || !frame?.contentWindow) return;
    sent = true;
    // The frame's origin is opaque, so it cannot be named as the target. It is this component's own sandboxed frame,
    // and it gets a copy of the file it was made to show.
    frame.contentWindow.postMessage({ type: 'render', bytes } satisfies RenderRequest, '*');
  });

  function onmessage(event: MessageEvent) {
    if (!frame || event.source !== frame.contentWindow) return;
    const message = readFrameMessage(event.data);
    if (message?.type === 'ready') ready = true;
    else if (message?.type === 'size') height = message.height;
    else if (message?.type === 'rendered') {
      pageCount = message.pageCount;
      outcome = 'shown';
    } else if (message?.type === 'failed') outcome = 'unreadable';
  }
</script>

<svelte:window {onmessage} />

{#if outcome === 'unavailable'}
  <QueryError message="Could not load the preview." onretry={() => attempt++} inset />
{:else if outcome === 'unreadable'}
  <Empty.Root class="p-4"><Empty.Description>Preview unavailable for this file.</Empty.Description></Empty.Root>
{:else}
  <div class={cn('flex flex-col gap-3', className)}>
    {#if outcome === 'loading'}
      <Skeleton aria-hidden="true" class="aspect-[17/22] w-full" />
      <p class="sr-only" role="status">Loading the preview…</p>
    {/if}
    {#if viewer}
      {#key `${file.id}:${attempt}`}
        <iframe
          bind:this={frame}
          title={`Preview of ${file.fileName}`}
          sandbox="allow-scripts"
          srcdoc={viewer}
          class="block w-full border-0 [color-scheme:light]"
          style:height={`${height}px`}
        ></iframe>
      {/key}
    {/if}
    {#if pageCount && pageCount > maxPreviewPages}
      <p class="text-muted-foreground">Showing the first {maxPreviewPages} pages. Download the file to read the rest.</p>
    {/if}
  </div>
{/if}
