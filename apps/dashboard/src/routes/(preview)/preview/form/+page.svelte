<script lang="ts">
  import { page } from '$app/state';
  import QuestionnairePreview from '#lib/components/workflow/questionnaire-preview.svelte';
  import type { FormPreview } from '#lib/components/workflow/questionnaire-builder.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { createPreviewSink } from '#lib/preview-window.svelte.js';

  // The form builder's pop-out preview (design system A8): the form a candidate sees, redrawn on each edit the editor
  // sends over its channel. The window is the device: no frame, and the form takes the width of the window, up to the
  // column the careers page gives it. It sits outside the app shell, with no sidebar and no live updates connection of
  // its own; signing in still applies.
  const sink = createPreviewSink<FormPreview>(page.url);
</script>

<svelte:head>
  <title>Preview · ScreenHire</title>
</svelte:head>

<main class="min-h-screen bg-candidate-background">
  <h1 class="sr-only">Preview</h1>
  {#if sink.closed}
    <p role="status" class="px-4 pt-4 text-muted-foreground">Editor closed.</p>
  {/if}
  {#if sink.value}
    <div class="mx-auto max-w-form">
      <QuestionnairePreview questions={sink.value.questions} followUp={sink.value.form === 'follow-up'} />
    </div>
  {:else}
    <!-- Until the editor's first message: the form's shape (E1). -->
    <div class="mx-auto flex max-w-form flex-col gap-4 p-4" aria-hidden="true">
      <Skeleton class="h-16 w-full" />
      <Skeleton class="h-16 w-full" />
    </div>
    <p role="status" class="sr-only">Loading the preview</p>
  {/if}
</main>
