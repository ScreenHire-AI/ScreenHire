<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { untrack } from 'svelte';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import WorkflowEditor from '#lib/components/workflow/workflow-editor.svelte';
  import WorkflowSummary from '#lib/components/workflow/workflow-summary.svelte';
  import WorkflowTabs from '#lib/components/workflow/workflow-tabs.svelte';
  import type { WorkflowTab } from '#lib/workflow-tabs.js';

  // A workflow's tabs and panes as a page lays them out, for component tests: the tabs list (on the header band in the
  // app) and the builder or the read-only view under one `Tabs.Root`. Render inside `WithQueryClient`.
  let {
    workflow,
    errors = [],
    mode,
    tab = 'pipeline',
    checking = false,
  }: { workflow: HiringWorkflow; errors?: DefinitionError[]; mode: 'editor' | 'summary'; tab?: WorkflowTab; checking?: boolean } = $props();

  // Copies the harness owns, as the page owns its draft and tab, so edits and tab switches in a test work like the page.
  let current = $state<WorkflowTab>(untrack(() => tab));
  let edited = $state(untrack(() => workflow));
</script>

<main>
  <Tabs.Root bind:value={current}>
    <WorkflowTabs />
    {#if mode === 'editor'}
      <WorkflowEditor bind:workflow={edited} {errors} {checking} bind:tab={current} hrefOf={(next) => `?tab=${next}`} />
    {:else}
      <WorkflowSummary workflow={edited} {errors} tab={current as WorkflowTab} hrefOf={(next) => `?tab=${next}`} />
    {/if}
  </Tabs.Root>
</main>
