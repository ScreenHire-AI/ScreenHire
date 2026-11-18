<script lang="ts">
  import PencilIcon from '~icons/lucide/pencil';
  import SendIcon from '~icons/lucide/send';
  import type { PublishCheckItem } from '@screenhire/api-client';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import { editHref, fieldWords, thingsToCheck } from '#lib/publish-check.js';

  // The check before a job goes live (design system section 11, Confirm dialog; docs/architecture/job-distribution.md,
  // "Check before publishing"). It warns and never blocks: each problem is one sentence with the link that fixes it, and
  // Publish anyway publishes with them. The page owns the publish itself (its failure and plan notices), so this closes
  // and hands over.
  let {
    open = $bindable(false),
    jobId,
    items,
    onpublish,
    busy = false,
  }: {
    open?: boolean;
    jobId: string;
    items: PublishCheckItem[];
    onpublish: () => void;
    /** A publish is under way. */
    busy?: boolean;
  } = $props();

  const first = $derived(items[0]);
</script>

<Dialog.Root bind:open>
  <Dialog.Content class="sm:max-w-lg">
    <Dialog.Header>
      <Dialog.Title>{thingsToCheck(items.length)}</Dialog.Title>
      <Dialog.Description>A board may refuse the job, or the careers page may look unfinished. You can publish anyway.</Dialog.Description>
    </Dialog.Header>
    <ul aria-label="Things to check" class="flex flex-col divide-y divide-border-subtle rounded-panel border border-border">
      {#each items as item (item.kind)}
        <li class="flex items-center justify-between gap-3 px-3.5 py-3 max-sm:flex-col max-sm:items-start">
          <p class="min-w-0">{item.text}</p>
          <Button href={editHref(jobId, item.field)} variant="outline" size="sm" class="shrink-0" aria-label={`Edit details, ${fieldWords[item.field]}`}>Edit details</Button>
        </li>
      {/each}
    </ul>
    <Dialog.Footer>
      <!-- With one problem its row already has this link (W7); the footer's is the way out of a list of several. -->
      {#if first && items.length > 1}
        <Button href={editHref(jobId, first.field)} variant="outline" size="lg"><PencilIcon aria-hidden="true" />Edit details</Button>
      {/if}
      <Button size="lg" disabled={busy} onclick={() => { open = false; onpublish(); }}><SendIcon aria-hidden="true" />Publish anyway</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
