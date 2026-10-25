<script lang="ts">
  import type { ScorecardQuestion } from '@screenhire/api-client';
  import { page } from '$app/state';
  import ScorecardPreview from '#lib/components/scorecard-preview.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { createPreviewSink } from '#lib/preview-window.svelte.js';

  // The scorecard editor's pop-out preview (design system A8): the scorecard as a reviewer fills it in, redrawn on each
  // edit the editor sends over its channel. It sits outside the app shell; signing in still applies.
  const sink = createPreviewSink<{ questions: ScorecardQuestion[] }>(page.url);
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
    <div class="mx-auto flex max-w-xl justify-center">
      <ScorecardPreview questions={sink.value.questions} />
    </div>
  {:else}
    <!-- Until the editor's first message: the scorecard's shape (E1). -->
    <div class="mx-auto flex max-w-xl flex-col gap-4" aria-hidden="true">
      <Skeleton class="h-16 w-full" />
      <Skeleton class="h-16 w-full" />
    </div>
    <p role="status" class="sr-only">Loading the preview</p>
  {/if}
</main>
