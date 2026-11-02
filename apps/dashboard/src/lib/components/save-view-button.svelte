<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import type { ViewFilters, ViewScope } from '@screenhire/api-client';
  import SaveViewDialog from '#lib/components/save-view-dialog.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { cn } from '#lib/utils.js';

  // "+ Save view", at the end of a candidate table's filter row once its filters or sort differ from the view it is on
  // (docs/architecture/saved-views.md). It opens the Save view dialog. The dialog is only in the page while it is open.
  let {
    scope,
    jobId = null,
    filters,
    show,
    class: className,
  }: {
    scope: ViewScope;
    /** The job whose table it is, for a job's view. */
    jobId?: string | null;
    /** What the table filters and sorts by now. */
    filters: ViewFilters;
    /** Whether the table differs from its view. */
    show: boolean;
    class?: string;
  } = $props();

  let open = $state(false);
</script>

{#if show}
  <Button variant="outline" aria-haspopup="dialog" class={cn('shrink-0', className)} onclick={() => (open = true)}>
    <PlusIcon aria-hidden="true" />Save view
  </Button>
{/if}
{#if open}
  <SaveViewDialog bind:open {scope} {jobId} {filters} />
{/if}
