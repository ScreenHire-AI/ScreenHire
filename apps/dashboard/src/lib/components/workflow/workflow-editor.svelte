<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { tick } from 'svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { laneOf, removeAt } from '#lib/workflow.js';
  import { LANE_NODE, addAutomation, connect, removeAutomation, stageNode, triggerNode } from '#lib/workflow-graph.js';
  import AutomationForm from './automation-form.svelte';
  import PipelineView from './pipeline-view.svelte';
  import ProblemsPanel from './problems-panel.svelte';
  import QuestionnaireBuilder from './questionnaire-builder.svelte';
  import ScorecardPicker from './scorecard-picker.svelte';
  import SimulatePanel from './simulate-panel.svelte';
  import type { GraphActions } from './step-node.svelte';
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
  let scorecardOpen = $state(false);
  // The stage whose scorecard the sheet edits, by id, so reordering stages behind the sheet keeps the same one.
  let scorecardStageId = $state<string | null>(null);
  const scorecardIndex = $derived(workflow.stages.findIndex((stage) => stage.id === scorecardStageId));

  function openScorecard(index: number) {
    scorecardStageId = workflow.stages[index]?.id ?? null;
    scorecardOpen = true;
  }
  let drawerLane = $state<string | null>(null);
  // The automations listed in the open drawer, fixed when it opens, so changing one's trigger does not make it vanish.
  let drawerIds = $state<string[]>([]);

  const laneName = $derived(drawerLane === null ? 'Whole workflow' : (workflow.stages.find((stage) => stage.id === drawerLane)?.name ?? drawerLane));
  const drawerItems = $derived(
    drawerIds.map((id) => workflow.automations.findIndex((automation) => automation.id === id)).filter((index) => index >= 0),
  );

  // Opened from the graph: the form control to focus first (`data-focus` in AutomationForm), and the graph node that
  // takes the focus back when the sheet closes if the button that opened it is gone.
  let focusPath = $state<string | null>(null);
  let returnTo = $state<{ node: string; opener: Element | null } | null>(null);

  function openAutomation(index: number, path: string) {
    const automation = workflow.automations[index];
    if (!automation) return;
    drawerLane = laneOf(automation);
    drawerIds = [automation.id];
    focusPath = path;
    returnTo = { node: triggerNode(automation.id), opener: document.activeElement };
    drawerOpen = true;
  }

  // The graph's edits: each one a change to the workflow from #lib/workflow-graph.ts, or the automation's own form.
  const graphActions: GraphActions = {
    edit: openAutomation,
    add(stageId) {
      const added = addAutomation(workflow, stageId);
      workflow.automations = added.workflow.automations;
      const index = workflow.automations.findIndex((automation) => automation.id === added.id);
      // A new automation's form starts at its name; closing it goes to its trigger node, not back to Add.
      openAutomation(index, `automations[${index}]`);
      returnTo = { node: triggerNode(added.id), opener: null };
    },
    async remove(automationId) {
      const automation = workflow.automations.find((candidate) => candidate.id === automationId);
      if (!automation) return;
      const lane = laneOf(automation);
      workflow.automations = removeAutomation(workflow, automationId).automations;
      // Its nodes are gone with it, so the focus goes to the lane or stage it was on.
      await tick();
      focusNode(lane && workflow.stages.some((stage) => stage.id === lane) ? stageNode(lane) : LANE_NODE);
    },
    connect(source, target) {
      workflow.automations = connect(workflow, source, target).automations;
    },
  };

  function focusNode(id: string): boolean {
    const node = document.querySelector(`.svelte-flow__node[data-id="${CSS.escape(id)}"]`);
    if (!(node instanceof HTMLElement)) return false;
    node.focus();
    return true;
  }

  function onOpenAutoFocus(event: Event) {
    const target = focusPath ? document.querySelector(`[data-focus="${CSS.escape(focusPath)}"]`) : null;
    if (!(target instanceof HTMLElement)) return;
    event.preventDefault();
    target.focus();
  }

  function onCloseAutoFocus(event: Event) {
    const back = returnTo;
    focusPath = null;
    returnTo = null;
    if (!back || (back.opener?.isConnected && back.opener !== document.body)) return;
    if (focusNode(back.node)) event.preventDefault();
  }

  function openLane(stageId: string | null) {
    focusPath = null;
    returnTo = null;
    drawerLane = stageId;
    drawerIds = workflow.automations.filter((automation) => laneOf(automation) === stageId).map((automation) => automation.id);
    drawerOpen = true;
  }

  function addToDrawer() {
    const added = addAutomation(workflow, drawerLane);
    workflow.automations = added.workflow.automations;
    drawerIds = [...drawerIds, added.id];
  }
</script>

<ProblemsPanel title="Problems to fix" {workflow} {errors} />

<Tabs.Content value="pipeline">
  <PipelineView bind:workflow {errors} onopen={openLane} onscorecard={openScorecard} />
</Tabs.Content>
<Tabs.Content value="graph">
  <!-- Mounted only while shown: the tab panels stay in the page when hidden, and the graph measures its nodes when
       it mounts, which it cannot do while hidden. -->
  <Panel title="Stages and automations" flush>
    {#if tab === 'graph'}<WorkflowGraph {workflow} {errors} actions={graphActions} />{/if}
  </Panel>
</Tabs.Content>
<Tabs.Content value="questionnaire">
  <QuestionnaireBuilder bind:workflow {errors} />
</Tabs.Content>
<Tabs.Content value="simulate">
  <SimulatePanel {workflow} />
</Tabs.Content>

<Sheet.Root bind:open={drawerOpen}>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-3xl" {onOpenAutoFocus} {onCloseAutoFocus}>
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
      <div><Button variant="outline" size="lg" onclick={addToDrawer}>Add automation</Button></div>
    </div>
  </Sheet.Content>
</Sheet.Root>

<Sheet.Root bind:open={scorecardOpen}>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-3xl">
    <Sheet.Header>
      <Sheet.Title>Scorecard: {workflow.stages[scorecardIndex]?.name ?? ''}</Sheet.Title>
      <Sheet.Description>Choose one from the library. A job keeps its own copy of the questions.</Sheet.Description>
    </Sheet.Header>
    <div class="@container px-4 pb-4">
      {#if scorecardIndex >= 0}
        <ScorecardPicker bind:stage={workflow.stages[scorecardIndex]!} index={scorecardIndex} {errors} />
      {/if}
    </div>
  </Sheet.Content>
</Sheet.Root>
