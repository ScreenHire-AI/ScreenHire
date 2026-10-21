<script lang="ts">
  import type { FilePreview } from '@screenhire/api-client';
  import type { Snippet } from 'svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';

  // A candidate's file shown inline, in an inset (design system section 11), from
  // `GET /v1/applications/{id}/files/{fileId}/preview`. The file never renders itself: the page shows the text the
  // worker read from it, drawn as text, so nothing in an untrusted file can run or reach the session.
  let {
    name,
    preview,
    fallback,
  }: {
    name: string;
    /** Undefined while the request loads. */
    preview: FilePreview | undefined;
    /** Shown when there is no preview, usually the file's `FileTile` so it can still be downloaded. */
    fallback?: Snippet;
  } = $props();
</script>

<div data-slot="resume-preview" class="flex min-w-0 flex-col gap-3 rounded-lg bg-secondary p-3">
  {#if !preview || preview.status === 'Pending'}
    <Skeleton aria-hidden="true" class="mx-auto aspect-[17/22] w-full max-w-2xl rounded-md bg-card" />
    <p role="status" class="sr-only">Loading the preview of {name}</p>
  {:else if preview.status === 'Ready'}
    <article
      aria-label={`${name}, ${preview.pageCount} ${preview.pageCount === 1 ? 'page' : 'pages'}`}
      class="mx-auto w-full max-w-2xl rounded-md bg-card px-6 py-5 text-reading break-words whitespace-pre-line"
    >{preview.text}</article
    >
  {:else}
    <p class="text-muted-foreground">{preview.note ?? 'No preview for this file.'}</p>
    {@render fallback?.()}
  {/if}
</div>
