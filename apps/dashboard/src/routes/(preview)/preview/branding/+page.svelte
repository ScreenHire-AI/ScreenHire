<script lang="ts">
  import { page } from '$app/state';
  import BrandingPreview, { type BrandingDraft } from '#lib/components/branding-preview.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { createPreviewSink } from '#lib/preview-window.svelte.js';

  // Branding's pop-out preview (design system A8): the careers page, the shared link and the email's top in the colours,
  // font and images as the editor has them, redrawn on each edit it sends over its channel. Outside the app shell, with
  // no sidebar and no live updates connection of its own; signing in still applies.
  const sink = createPreviewSink<BrandingDraft>(page.url);
</script>

<svelte:head>
  <title>Preview · ScreenHire</title>
</svelte:head>

<main class="min-h-screen bg-background p-4">
  <h1 class="sr-only">Preview</h1>
  {#if sink.closed}
    <p role="status" class="pb-4 text-muted-foreground">Editor closed.</p>
  {/if}
  {#if sink.value}
    <BrandingPreview draft={sink.value} headingLevel={2} />
  {:else}
    <div class="flex flex-col gap-4" aria-hidden="true">
      <Skeleton class="h-40 w-full" />
      <Skeleton class="h-24 w-full" />
    </div>
    <p role="status" class="sr-only">Loading the preview</p>
  {/if}
</main>
