<script lang="ts">
  import { getDownloadApplicationFileUrl, type ApplicationFileSummary } from '@screenhire/api-client';
  import DownloadIcon from '@lucide/svelte/icons/download';
  import FileImageIcon from '@lucide/svelte/icons/file-image';
  import FileTextIcon from '@lucide/svelte/icons/file-text';
  import type { Snippet } from 'svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { withheldReason } from '#lib/files.js';
  import { fileSize } from '#lib/format.js';
  import { needsAttention } from '#lib/status.js';

  // One uploaded file: its type, name and size, and Download. A file the scan withholds says why instead.
  let { applicationId, file, facts }: { applicationId: string; file: ApplicationFileSummary; facts?: Snippet } = $props();

  const Icon = $derived(file.contentType.startsWith('image/') ? FileImageIcon : FileTextIcon);
  const attention = $derived(withheldReason(file));
</script>

<div class="flex flex-wrap items-center gap-x-3 gap-y-2">
  <div class="flex min-w-0 flex-[1_1_12rem] items-center gap-2">
    <Icon aria-hidden="true" class="shrink-0 text-muted-foreground" />
    <!-- The name may be cut short; the tooltip holds it whole, on hover, focus or tap. -->
    <TruncatedText text={file.fileName} class="font-semibold" />
  </div>
  <div class="flex flex-wrap items-center gap-2">
    {@render facts?.()}
    <Badge variant="muted" class="tabular-nums">{fileSize(file.sizeBytes)}</Badge>
    {#if attention}
      <StatusBadge status={attention.status} label={attention.label} quiet={!needsAttention(attention.status)} />
    {:else}
      <!-- A full navigation, not the client router: the API redirects to a short-lived link that downloads the file. -->
      <Button variant="outline" size="lg" href={getDownloadApplicationFileUrl(applicationId, file.id)} data-sveltekit-reload aria-label={`Download ${file.fileName}`} class="pointer-coarse:h-11">
        <DownloadIcon aria-hidden="true" />Download
      </Button>
    {/if}
  </div>
</div>
