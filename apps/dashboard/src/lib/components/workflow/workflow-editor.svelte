<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { laneOf, newAutomation, removeAt } from '#lib/workflow.js';
  import AutomationForm from './automation-form.svelte';
  import PipelineView from './pipeline-view.svelte';
  import ProblemsPanel from './problems-panel.svelte';
  import QuestionnaireBuilder from './questionnaire-builder.svelte';
  import SimulatePanel from './simulate-panel.svelte';
  import WorkflowGraph from './workflow-graph.svelte';

  // The workflow builder (workflow-engine.md §4) for a template's draft or a job's own copy: pipeline, graph,
  // questionnaire and simulation over one workflow, as the panes of the page's `Tabs.Root` (`WorkflowTabs` sits on the
  // header band). The page owns loading and saving, and shows the validation result beside its title; `errors` are the
  // API validator's, with paths relative to the workflow. A version that cannot be edited is a `WorkflowSummary`.
  let {
    workflow = $bindable(),
    errors,
    tab,
  }: { workflow: HiringWorkflow; errors: readonly DefinitionError[]; tab: string } = $props();

  let drawerOpen = $state(false);
  let drawerLane = $state<string | null>(null);
  // The automations listed in the open drawer, fixed when it opens, so changing one's trigger does not make it vanish.
  let drawerIds = $state<string[]>([]);

  const laneName = $derived(drawerLane === null ? 'Whole workflow' : (workflow.stages.find((stage) => stage.id === drawerLane)?.name ?? drawerLane));
  const drawerItems = $derived(
    drawerIds.map((id) => workflow.automations.findIndex((automation) => automation.id === id)).filter((index) => index >= 0),
  );

  function openLane(stageId: string | null) {
    drawerLane = stageId;
    drawerIds = workflow.automations.filter((automation) => laneOf(automation) === stageId).map((automation) => automation.id);
    drawerOpen = true;
  }

  function openAutomation(index: number) {
    const automation = workflow.automations[index];
    if (!automation) return;
    drawerLane = laneOf(automation);
    drawerIds = [automation.id];
    drawerOpen = true;
  }

  function addAutomation() {
    const automation = newAutomation(workflow, drawerLane);
    workflow.automations = [...workflow.automations, automation];
    drawerIds = [...drawerIds, automation.id];
  }
</script>

<ProblemsPanel title="Problems to fix" {workflow} {errors} />

<Tabs.Content value="pipeline">
  <PipelineView bind:workflow {errors} onopen={openLane} />
</Tabs.Content>
<Tabs.Content value="graph">
  <!-- Mounted only while shown: the tab panels stay in the page when hidden, and the graph fits its view to the size
       it mounts at, which is nothing while hidden. -->
  <Panel title="Stages and automations" flush>
    {#if tab === 'graph'}<WorkflowGraph {workflow} {errors} onedit={openAutomation} />{/if}
  </Panel>
</Tabs.Content>
<Tabs.Content value="questionnaire">
  <QuestionnaireBuilder bind:workflow {errors} />
</Tabs.Content>
<Tabs.Content value="simulate">
  <SimulatePanel {workflow} />
</Tabs.Content>

<Sheet.Root bind:open={drawerOpen}>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-3xl">
    <Sheet.Header>
      <Sheet.Title>Automations: {laneName}</Sheet.Title>
    </Sheet.Header>
    <div class="@container flex flex-col gap-6 px-4 pb-4">
      {#each drawerItems as index (workflow.automations[index]!.id)}
        <AutomationForm
          bind:automation={workflow.automations[index]!}
          {index}
          {workflow}
          {errors}
          onremove={() => (workflow.automations = removeAt(workflow.automations, index))}
        />
      {:else}
        <p class="text-muted-foreground">No automations here yet.</p>
      {/each}
      <div><Button variant="outline" onclick={addAutomation}>Add automation</Button></div>
    </div>
  </Sheet.Content>
</Sheet.Root>
