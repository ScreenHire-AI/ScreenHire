<script lang="ts">
  import type { EmailPreview as Preview } from '@screenhire/api-client';
  import { page } from '$app/state';
  import EmailPreview from '#lib/components/email-preview.svelte';
  import { createPreviewSink } from '#lib/preview-window.svelte.js';

  // The email template editor's pop-out preview (design system A8): the email as the sample candidate receives it,
  // redrawn on each edit the editor sends over its channel. The editor renders it; this window only shows it, so it
  // makes no request of its own.
  const sink = createPreviewSink<{ preview: Preview | undefined; failure: string | undefined }>(page.url);
</script>

<svelte:head>
  <title>Preview · ScreenHire</title>
</svelte:head>

<main class="min-h-screen bg-background p-4">
  <h1 class="sr-only">Preview</h1>
  {#if sink.closed}
    <p role="status" class="pb-4 text-muted-foreground">Editor closed.</p>
  {/if}
  <div class="mx-auto max-w-form rounded-panel border border-border bg-card p-4 shadow-surface">
    <EmailPreview preview={sink.value?.preview} failure={sink.value?.failure} />
  </div>
</main>
