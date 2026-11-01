<script lang="ts">
  import { getDownloadApplicationFileUrl, type ApplicationFileSummary } from '@screenhire/api-client';
  import DownloadIcon from '~icons/lucide/download';
  import FileImageIcon from '~icons/lucide/file-image';
  import FileTextIcon from '~icons/lucide/file-text';
  import FileViewer from '#lib/components/file-viewer.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { canView, withheldReason } from '#lib/files.js';
  import { fileSize } from '#lib/format.js';
  import { needsAttention } from '#lib/status.js';

  // A file row (design system section 11): the type icon, the name, its size, View and Download. View opens the file in
  // the full-window viewer, for the types it draws in isolation (PDFs); anything else keeps Download. A file the scan
  // withholds says why instead, and offers neither.
  let {
    applicationId,
    file,
    description,
  }: {
    applicationId: string;
    file: ApplicationFileSummary;
    /** The viewer's line under the name, such as the candidate and the job. */
    description?: string;
  } = $props();

  let open = $state(false);
  const Icon = $derived(file.contentType.startsWith('image/') ? FileImageIcon : FileTextIcon);
  const attention = $derived(withheldReason(file));
</script>

<div data-slot="file-row" class="flex min-w-0 items-center gap-3">
  <Icon aria-hidden="true" class="shrink-0 text-muted-foreground" />
  <div class="flex min-w-0 flex-1 flex-col">
    <!-- The name may be cut short; the tooltip holds it whole, on hover, focus or tap. -->
    <TruncatedText text={file.fileName} class="font-semibold" />
    <span class="text-caption text-muted-foreground tabular-nums">{fileSize(file.sizeBytes)}</span>
  </div>
  {#if attention}
    <StatusBadge status={attention.status} label={attention.label} quiet={!needsAttention(attention.status)} />
  {:else}
    {#if canView(file)}
      <Button variant="outline" class="shrink-0" aria-label={`View ${file.fileName}`} onclick={() => (open = true)}>View</Button>
    {/if}
    <IconButton label={`Download ${file.fileName}`} variant="outline" href={getDownloadApplicationFileUrl(applicationId, file.id)} data-sveltekit-reload>
      <DownloadIcon aria-hidden="true" />
    </IconButton>
  {/if}
</div>

{#if canView(file)}
  <FileViewer bind:open {applicationId} {file} {description} />
{/if}
