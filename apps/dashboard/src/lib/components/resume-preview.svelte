<script lang="ts">
  import type { ApplicationFileSummary } from '@screenhire/api-client';
  import FileFrame from '#lib/components/file-frame.svelte';
  import FileTile from '#lib/components/file-tile.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { canDownload, canView } from '#lib/files.js';

  // A candidate's file shown inline, in an inset under its file tile, drawn in isolation by `FileFrame`. The v0.3 build
  // replaces it with a file row and the full-window file viewer (design system section 11), because a page no longer
  // embeds a preview.
  let { applicationId, file }: { applicationId: string; file: ApplicationFileSummary } = $props();

  let pageCount = $state<number>();
</script>

<div class="flex flex-col gap-3">
  <FileTile {applicationId} {file}>
    {#snippet facts()}
      {#if pageCount}<Badge variant="muted" class="tabular-nums">{pageCount === 1 ? '1 page' : `${pageCount} pages`}</Badge>{/if}
    {/snippet}
  </FileTile>

  {#if canView(file)}
    <FileFrame {applicationId} {file} bind:pageCount class="rounded-lg bg-secondary p-3" />
  {:else if canDownload(file)}
    <Empty.Root class="p-4"><Empty.Description>No preview for this file type.</Empty.Description></Empty.Root>
  {/if}
</div>
