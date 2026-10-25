<script lang="ts">
  import type { Question } from '@screenhire/api-client';
  import { page } from '$app/state';
  import QuestionnairePreview from '#lib/components/workflow/questionnaire-preview.svelte';
  import { createPreviewSink } from '#lib/preview-window.svelte.js';

  // The style guide's pop-out preview (A8): the questionnaire the editor in the Edit / Preview panel sends, redrawn on each
  // edit. An editor's own preview page works the same way. Development builds only.
  const sink = createPreviewSink<Question[]>(page.url);
</script>

<svelte:head>
  <title>Preview · ScreenHire</title>
</svelte:head>

<main class="min-h-screen bg-candidate-background">
  <h1 class="sr-only">Preview</h1>
  {#if sink.closed}
    <p role="status" class="px-4 pt-4 text-muted-foreground">The editor was closed, so this preview no longer follows it.</p>
  {/if}
  {#if sink.value}
    <QuestionnairePreview questions={sink.value} followUp />
  {:else}
    <p role="status" class="p-4 text-muted-foreground">Waiting for the editor.</p>
  {/if}
</main>
