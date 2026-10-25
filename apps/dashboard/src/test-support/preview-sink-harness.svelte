<script lang="ts">
  import { createPreviewSink } from '#lib/preview-window.svelte.js';

  // The preview window's page: what the editor named in `url` last sent, and whether it has gone.
  let { url }: { url: URL } = $props();
  // svelte-ignore state_referenced_locally: a preview window keeps the channel it opened with.
  const sink = createPreviewSink<{ title: string }>(url);
</script>

<p data-testid="window">{sink.value?.title ?? 'Waiting'}</p>
{#if sink.closed}<p>Editor closed</p>{/if}
