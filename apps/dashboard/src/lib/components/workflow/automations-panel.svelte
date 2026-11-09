<script lang="ts">
  import type { DefinitionError, EvaluationOutcome, HiringWorkflow } from '@screenhire/api-client';
  import ListIcon from '~icons/lucide/list';
  import PlusIcon from '~icons/lucide/plus';
  import WorkflowIcon from '~icons/lucide/workflow';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { IsMobile } from '#lib/hooks/is-mobile.svelte.js';
  import AutomationList from './automation-list.svelte';
  import type { GraphActions } from './step-node.svelte';
  import WorkflowGraph from './workflow-graph.svelte';

  // The Automations tab (design system section 11, Workflow graph and Automation list): the stages and what runs in them,
  // as the map or as a list, with Add automation where the page can edit. The map is the overview; a phone gets the list,
  // since the map needs room, and edits through the same sheets. Mounted only while its tab is shown, because the map
  // measures its nodes when it mounts, which it cannot do while hidden.
  let {
    workflow,
    errors,
    graphActions,
    tested,
    onclear,
    onopen,
    onadd,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    /** What pressing a node does; absent where the workflow only reads. */
    graphActions?: GraphActions;
    tested?: ReadonlyMap<string, EvaluationOutcome>;
    onclear?: () => void;
    /** Opens the automation at this index; absent where the workflow only reads. */
    onopen?: (index: number) => void;
    /** Opens the recipes for the whole workflow; absent where the workflow only reads. */
    onadd?: () => void;
  } = $props();

  const phone = new IsMobile();
  let chosen = $state<'map' | 'list'>('map');
  const view = $derived(phone.current ? 'list' : chosen);
</script>

<Panel title={view === 'map' ? 'Stages and automations' : 'Automations'} flush={view === 'map'}>
  {#snippet actions()}
    {#if !phone.current}
      <div role="group" aria-label="View" class="flex gap-1">
        <Toggle bind:pressed={() => chosen === 'list', () => (chosen = 'list')}><ListIcon aria-hidden="true" />List</Toggle>
        <Toggle bind:pressed={() => chosen === 'map', () => (chosen = 'map')}><WorkflowIcon aria-hidden="true" />Map</Toggle>
      </div>
    {/if}
    <!-- An empty map says what to do under the stages, with its own button, so the header's is left out then (W7). -->
    {#if onadd && !(view === 'map' && workflow.automations.length === 0)}<Button variant="outline" onclick={onadd}><PlusIcon aria-hidden="true" />Add automation</Button>{/if}
  {/snippet}
  {#if view === 'map'}
    <WorkflowGraph {workflow} {errors} actions={graphActions} {tested} {onclear} />
  {:else}
    <AutomationList {workflow} {onopen} />
  {/if}
</Panel>
