<script lang="ts">
  import type { DefinitionError, EvaluationOutcome, HiringWorkflow, WorkflowSimulationResult } from '@screenhire/api-client';
  import { tick, untrack } from 'svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { laneOf, removeAt } from '#lib/workflow.js';
  import { LANE_NODE, addAction, addAutomation, addCondition, connect, definitionStamp, focusGraphNode, stageNode, triggerNode } from '#lib/workflow-graph.js';
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
    tab = $bindable(),
  }: { workflow: HiringWorkflow; errors: readonly DefinitionError[]; tab: string } = $props();

  // The last test's outcome per automation, lit on the graph once the person asks to see it there. The graph tab mounts
  // only while it is shown, and a test is about the workflow as it was, so an edit clears it.
  let tested = $state<ReadonlyMap<string, EvaluationOutcome> | undefined>();
  const stamp = $derived(definitionStamp(workflow));
  $effect(() => {
    void stamp;
    untrack(() => (tested = undefined));
  });

  async function showTested(result: WorkflowSimulationResult) {
    tested = new Map(result.automations.map((automation) => [automation.automationId, automation.outcome]));
    tab = 'graph';
    // The button that asked leaves with its tab, so the focus goes to Clear, beside the words saying what is lit. On a
    // node it would light that node's path in place of the test's.
    await tick();
    document.querySelector<HTMLElement>('[data-graph-clear]')?.focus();
  }

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
    openLane(stageId) {
      openLane(stageId);
      returnTo = { node: stageId ? stageNode(stageId) : LANE_NODE, opener: document.activeElement };
    },
    add(stageId) {
      const added = addAutomation(workflow, stageId);
      workflow.automations = added.workflow.automations;
      const index = workflow.automations.findIndex((automation) => automation.id === added.id);
      // A new automation's form starts at its name; closing it goes to its trigger node, not back to the plus.
      openAutomation(index, `automations[${index}]`);
      returnTo = { node: triggerNode(added.id), opener: null };
    },
    addCondition(automationId) {
      const added = addCondition(workflow, automationId);
      if (added) openChanged(added, automationId);
    },
    addAction(automationId) {
      const added = addAction(workflow, automationId);
      if (added) openChanged(added, automationId);
    },
    connect(source, target) {
      workflow.automations = connect(workflow, source, target).automations;
    },
  };

  // The form of an automation the graph just changed, at the part it added; closing it goes to the automation's trigger.
  function openChanged(added: { workflow: HiringWorkflow; path: string }, automationId: string) {
    workflow.automations = added.workflow.automations;
    const index = workflow.automations.findIndex((automation) => automation.id === automationId);
    openAutomation(index, added.path);
    returnTo = { node: triggerNode(automationId), opener: null };
  }

  function onOpenAutoFocus(event: Event) {
    const target = focusPath ? focusTarget(event.currentTarget, focusPath) : null;
    if (!target) return;
    event.preventDefault();
    target.focus();
  }

  // The control to focus for a path: the one that names it, else (from Fix) the first field the sheet marks invalid,
  // else the control of the nearest part that contains it ("automations[0].actions[1]" for its tag).
  function focusTarget(sheet: EventTarget | null, path: string): HTMLElement | null {
    const exact = document.querySelector<HTMLElement>(`[data-focus="${CSS.escape(path)}"]`);
    if (exact) return exact;
    const invalid = sheet instanceof HTMLElement ? sheet.querySelector<HTMLElement>('[aria-invalid="true"]') : null;
    if (invalid) return invalid;
    const parts = [...document.querySelectorAll<HTMLElement>('[data-focus]')].filter((element) => {
      const key = element.dataset.focus ?? '';
      return path.startsWith(`${key}.`) || path.startsWith(`${key}[`);
    });
    return parts.sort((a, b) => (b.dataset.focus?.length ?? 0) - (a.dataset.focus?.length ?? 0))[0] ?? null;
  }

  // A problem's Fix: an automation's opens its form at the field; any other goes to its tab and the first field marked
  // invalid there.
  async function fix(path: string) {
    const automation = /^automations\[(\d+)\]/.exec(path);
    if (automation) {
      openAutomation(Number(automation[1]), path);
      return;
    }
    tab = /^(questionnaire|followUpQuestionnaires)/.test(path) ? 'questionnaire' : 'pipeline';
    await tick();
    const field = document.querySelector<HTMLElement>('[role="tabpanel"][data-state="active"] [aria-invalid="true"]');
    field?.scrollIntoView({ block: 'center' });
    field?.focus({ preventScroll: true });
  }

  function onCloseAutoFocus(event: Event) {
    const back = returnTo;
    focusPath = null;
    returnTo = null;
    if (!back || (back.opener?.isConnected && back.opener !== document.body)) return;
    if (focusGraphNode(document, back.node)) event.preventDefault();
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

<ProblemsPanel title="Problems to fix" {workflow} {errors} onfix={fix} />

<Tabs.Content value="pipeline">
  <PipelineView bind:workflow {errors} onopen={openLane} onscorecard={openScorecard} />
</Tabs.Content>
<Tabs.Content value="graph">
  <!-- Mounted only while shown: the tab panels stay in the page when hidden, and the graph measures its nodes when
       it mounts, which it cannot do while hidden. -->
  <Panel title="Stages and automations" flush>
    {#if tab === 'graph'}<WorkflowGraph {workflow} {errors} actions={graphActions} {tested} onclear={() => (tested = undefined)} />{/if}
  </Panel>
</Tabs.Content>
<Tabs.Content value="questionnaire">
  <QuestionnaireBuilder bind:workflow {errors} />
</Tabs.Content>
<Tabs.Content value="simulate">
  <SimulatePanel {workflow} onshow={showTested} onadd={() => graphActions.add(null)} onpipeline={() => (tab = 'pipeline')} />
</Tabs.Content>

<Sheet.Root bind:open={drawerOpen}>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-3xl" {onOpenAutoFocus} {onCloseAutoFocus}>
    <Sheet.Header>
      <Sheet.Title>Automations: {laneName}</Sheet.Title>
      <Sheet.Description>Each runs on its own when its trigger happens, and saves with the workflow.</Sheet.Description>
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
