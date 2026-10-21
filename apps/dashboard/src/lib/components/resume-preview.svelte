<script lang="ts" module>
  /** One page of a file, as the server renders it for preview. */
  export interface PreviewPage {
    /** An image of the page. */
    image: string;
    /** The page's text, for screen readers and search in the page. */
    text: string;
  }
</script>

<script lang="ts">
  import type { Snippet } from 'svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';

  // A candidate's file shown inline, in an inset (design system section 11). The file never renders itself: the page
  // shows images of its pages and their text, which cannot run code or reach the session, so an untrusted file stays
  // isolated whatever it holds.
  let {
    name,
    pages,
    fallback,
  }: {
    name: string;
    /** Undefined while the preview loads; null or empty when there is none, such as for a file type it cannot draw. */
    pages: readonly PreviewPage[] | null | undefined;
    /** Shown when there is no preview, usually the file's `FileTile` so it can still be downloaded. */
    fallback?: Snippet;
  } = $props();
</script>

<div data-slot="resume-preview" class="flex min-w-0 flex-col gap-3 rounded-lg bg-secondary p-3">
  {#if pages === undefined}
    <Skeleton aria-hidden="true" class="mx-auto aspect-[17/22] w-full max-w-2xl rounded-md bg-card" />
    <p role="status" class="sr-only">Loading the preview of {name}</p>
  {:else if !pages || pages.length === 0}
    <p class="text-muted-foreground">No preview for this file.</p>
    {@render fallback?.()}
  {:else}
    <ol aria-label={`${name}, ${pages.length} ${pages.length === 1 ? 'page' : 'pages'}`} class="flex flex-col items-center gap-3">
      {#each pages as page, index (index)}
        <li class="w-full max-w-2xl">
          <img
            src={page.image}
            alt={`Page ${index + 1} of ${pages.length}`}
            loading="lazy"
            referrerpolicy="no-referrer"
            class="w-full rounded-md bg-card"
          />
          <div class="sr-only">{page.text}</div>
        </li>
      {/each}
    </ol>
  {/if}
</div>
