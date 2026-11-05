<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import type { ViewFilters, ViewScope } from '@screenhire/api-client';
  import Hint from '#lib/components/hint.svelte';
  import SaveViewDialog from '#lib/components/save-view-dialog.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { cn } from '#lib/utils.js';

  // "+ Save view", at the end of a candidate table's filter row, so people can see that views can be saved. It works once
  // the filters or sort differ from the view the table is on, and until then says why not (A3,
  // docs/architecture/saved-views.md). It opens the Save view dialog. The dialog is only in the page while it is open.
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
{:else}
  <Hint content="Change a filter or the sort, then save it as a view.">
    {#snippet children({ props })}
      <!-- A phone's filter row has no room to spare, so the disabled button waits there until it works. -->
      <span {...props} class={cn('inline-flex shrink-0 max-md:hidden', className)}>
        <Button variant="outline" disabled><PlusIcon aria-hidden="true" />Save view</Button>
      </span>
    {/snippet}
  </Hint>
{/if}
{#if open}
  <SaveViewDialog bind:open {scope} {jobId} {filters} />
{/if}
