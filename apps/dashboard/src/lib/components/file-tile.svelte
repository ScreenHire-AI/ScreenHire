<script lang="ts" module>
  import FileIcon from '@lucide/svelte/icons/file';
  import FileImageIcon from '@lucide/svelte/icons/file-image';
  import FileTextIcon from '@lucide/svelte/icons/file-text';
  import type { MalwareScanStatus } from '@screenhire/api-client';
  import type { Status } from '#lib/components/status-badge.svelte';

  const documents = new Set(['pdf', 'doc', 'docx', 'odt', 'rtf', 'txt']);
  const images = new Set(['png', 'jpg', 'jpeg', 'gif', 'webp', 'heic']);

  function iconFor(name: string) {
    const extension = name.split('.').pop()?.toLowerCase() ?? '';
    return documents.has(extension) ? FileTextIcon : images.has(extension) ? FileImageIcon : FileIcon;
  }

  /** The status the tile shows in place of Download while the file is not safe to open (section 3.2). */
  const attention: Partial<Record<MalwareScanStatus, { status: Status; label: string; detail: string }>> = {
    Pending: { status: 'review', label: 'Scanning', detail: 'Being scanned for malware' },
    Infected: { status: 'verify', label: 'Withheld', detail: 'The malware scan found a threat' },
  };
</script>

<script lang="ts">
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { formatFileSize } from '#lib/format.js';

  // A file a candidate sent (design system section 11): its type icon, name and Download. The scan status shows only
  // when the file needs attention; then there is no Download. An inset, so it sits inside a panel without an edge.
  let {
    name,
    size,
    href,
    scanStatus = 'NotScanned',
  }: {
    name: string;
    /** In bytes. */
    size?: number;
    /** The download address. The API answers with a redirect to a short-lived link, so the browser loads it in full. */
    href: string;
    scanStatus?: MalwareScanStatus;
  } = $props();

  const Icon = $derived(iconFor(name));
  const warning = $derived(attention[scanStatus]);
</script>

<div data-slot="file-tile" data-scan-status={scanStatus} class="flex min-w-0 items-center gap-3 rounded-lg bg-secondary p-3">
  <span aria-hidden="true" class="grid size-9 shrink-0 place-items-center rounded-md bg-card text-muted-foreground">
    <Icon />
  </span>
  <div class="flex min-w-0 flex-1 flex-col">
    <TruncatedText text={name} class="font-semibold" />
    {#if warning}
      <span class="text-caption text-muted-foreground">{warning.detail}</span>
    {:else if size !== undefined}
      <span class="text-caption text-muted-foreground tabular-nums">{formatFileSize(size)}</span>
    {/if}
  </div>
  {#if warning}
    <StatusBadge status={warning.status} label={warning.label} class="shrink-0" />
  {:else}
    <Button variant="outline" {href} data-sveltekit-reload aria-label={`Download ${name}`} class="shrink-0">Download</Button>
  {/if}
</div>
