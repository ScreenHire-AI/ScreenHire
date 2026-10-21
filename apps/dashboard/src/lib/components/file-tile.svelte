<script lang="ts" module>
  import type { MarkShape } from '#lib/distribution.js';

  // Why a file is not offered. Only these two scan results withhold a file.
  const withheld: Record<'Pending' | 'Infected', { label: string; shape: MarkShape; tone: 'review' | 'verify' }> = {
    Pending: { label: 'Being scanned for malware', shape: 'diamond', tone: 'review' },
    Infected: { label: 'Withheld: malware found', shape: 'square', tone: 'verify' },
  };
</script>

<script lang="ts">
  import { getDownloadApplicationFileUrl, type ApplicationFileSummary } from '@screenhire/api-client';
  import DownloadIcon from '@lucide/svelte/icons/download';
  import FileImageIcon from '@lucide/svelte/icons/file-image';
  import FileTextIcon from '@lucide/svelte/icons/file-text';
  import type { Snippet } from 'svelte';
  import StatusMark from '#lib/components/status-mark.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { canDownload } from '#lib/files.js';
  import { fileSize } from '#lib/format.js';

  // One uploaded file: its type, name and size, and Download. A file the scan withholds says why instead.
  let { applicationId, file, facts }: { applicationId: string; file: ApplicationFileSummary; facts?: Snippet } = $props();

  const Icon = $derived(file.contentType.startsWith('image/') ? FileImageIcon : FileTextIcon);
  const attention = $derived(canDownload(file) ? undefined : withheld[file.scanStatus as keyof typeof withheld]);
</script>

<div class="flex flex-wrap items-center gap-x-3 gap-y-2">
  <div class="flex min-w-0 flex-[1_1_12rem] items-center gap-2">
    <Icon aria-hidden="true" class="shrink-0 text-muted-foreground" />
    <!-- The name may be cut short; the tooltip holds it whole, on hover, focus or tap. -->
    <Tooltip.Provider>
      <Tooltip.Root>
        <Tooltip.Trigger>
          {#snippet child({ props })}
            <span {...props} class="min-w-0 truncate rounded-sm font-semibold">{file.fileName}</span>
          {/snippet}
        </Tooltip.Trigger>
        <Tooltip.Content>{file.fileName}</Tooltip.Content>
      </Tooltip.Root>
    </Tooltip.Provider>
  </div>
  <div class="flex flex-wrap items-center gap-2">
    {@render facts?.()}
    <Badge variant="muted" class="tabular-nums">{fileSize(file.sizeBytes)}</Badge>
    {#if attention}
      <Badge variant={attention.tone}><StatusMark shape={attention.shape} tone={attention.tone} />{attention.label}</Badge>
    {:else}
      <!-- A full navigation, not the client router: the API redirects to a short-lived link that downloads the file. -->
      <Button variant="outline" href={getDownloadApplicationFileUrl(applicationId, file.id)} data-sveltekit-reload aria-label={`Download ${file.fileName}`}>
        <DownloadIcon aria-hidden="true" />Download
      </Button>
    {/if}
  </div>
</div>
