<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { untrack } from 'svelte';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import WorkflowEditor from '#lib/components/workflow/workflow-editor.svelte';
  import WorkflowSummary from '#lib/components/workflow/workflow-summary.svelte';
  import WorkflowTabs from '#lib/components/workflow/workflow-tabs.svelte';

  // A workflow's tabs and panes as a page lays them out, for component tests: the tabs list (on the header band in the
  // app) and the builder or the read-only view under one `Tabs.Root`. Render inside `WithQueryClient`.
  let {
    workflow,
    errors = [],
    mode,
    tab = 'pipeline',
  }: { workflow: HiringWorkflow; errors?: DefinitionError[]; mode: 'editor' | 'summary'; tab?: string } = $props();

  // Copies the harness owns, as the page owns its draft and tab, so edits and tab switches in a test work like the page.
  let current = $state(untrack(() => tab));
  let edited = $state(untrack(() => workflow));
</script>

<main>
  <Tabs.Root bind:value={current}>
    <WorkflowTabs />
    {#if mode === 'editor'}
      <WorkflowEditor bind:workflow={edited} {errors} tab={current} />
    {:else}
      <WorkflowSummary workflow={edited} {errors} tab={current} />
    {/if}
  </Tabs.Root>
</main>
