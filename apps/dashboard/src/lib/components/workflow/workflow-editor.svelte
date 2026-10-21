<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { describePath, laneOf, newAutomation, removeAt } from '#lib/workflow.js';
  import AutomationForm from './automation-form.svelte';
  import PipelineView from './pipeline-view.svelte';
  import ProblemMark from './problem-mark.svelte';
  import QuestionnaireBuilder from './questionnaire-builder.svelte';
  import SimulatePanel from './simulate-panel.svelte';
  import WorkflowGraph from './workflow-graph.svelte';

  // The workflow builder (workflow-engine.md §4) for a template version or a job's own copy: pipeline, graph,
  // questionnaire and simulation over one workflow. The page owns loading and saving; `errors` are the API validator's,
  // with paths relative to the workflow.
  let {
    workflow = $bindable(),
    errors,
    checking = false,
    readonly = false,
  }: { workflow: HiringWorkflow; errors: readonly DefinitionError[]; checking?: boolean; readonly?: boolean } = $props();

  let tab = $state('pipeline');
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

<div class="flex flex-col gap-3">
  <section aria-label="Validation" aria-live="polite" aria-busy={checking} class="text-body">
    {#if errors.length > 0}
      <div class="rounded-xl border border-destructive/40 bg-card px-3.5 py-2.5">
        <p class="font-semibold text-destructive">
          <ProblemMark />
          {errors.length}
          {errors.length === 1 ? 'problem' : 'problems'} to fix before this can be saved{checking ? ' (checking your latest change)' : ''}
        </p>
        <ul class="mt-1 flex flex-col gap-0.5">
          {#each errors as error, index (index)}
            <li><span class="font-semibold">{describePath(workflow, error.path)}:</span> {error.message}</li>
          {/each}
        </ul>
      </div>
    {:else}
      <p class="text-muted-foreground">{checking ? 'Checking…' : 'No problems found.'}</p>
    {/if}
  </section>

  <Tabs.Root bind:value={tab}>
    <Tabs.List aria-label="Builder views">
      <Tabs.Trigger value="pipeline">Pipeline</Tabs.Trigger>
      <Tabs.Trigger value="graph">Graph</Tabs.Trigger>
      <Tabs.Trigger value="questionnaire">Questionnaire</Tabs.Trigger>
      <Tabs.Trigger value="simulate">Simulate</Tabs.Trigger>
    </Tabs.List>
    <Tabs.Content value="pipeline" class="pt-2">
      <PipelineView bind:workflow {errors} {readonly} onopen={openLane} />
    </Tabs.Content>
    <Tabs.Content value="graph" class="pt-2">
      <p class="mb-2 text-body text-muted-foreground">
        The same workflow as a graph: stages along the top, then each automation from trigger to actions. Edit an automation
        from its node, or in the pipeline view.
      </p>
      <!-- Mounted only while shown: the tab panels stay in the page when hidden, and the graph fits its view to the size
           it mounts at, which is nothing while hidden. -->
      {#if tab === 'graph'}<WorkflowGraph {workflow} {errors} onedit={openAutomation} />{/if}
    </Tabs.Content>
    <Tabs.Content value="questionnaire" class="pt-2">
      <QuestionnaireBuilder bind:workflow {errors} {readonly} />
    </Tabs.Content>
    <Tabs.Content value="simulate" class="pt-2">
      <SimulatePanel {workflow} />
    </Tabs.Content>
  </Tabs.Root>
</div>

<Sheet.Root bind:open={drawerOpen}>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-3xl">
    <Sheet.Header>
      <Sheet.Title>Automations: {laneName}</Sheet.Title>
      <Sheet.Description>
        {drawerLane === null
          ? 'Automations tied to no one stage. Each runs its actions when its trigger fires and its conditions hold.'
          : `Automations that fire when candidates enter, leave or wait in ${laneName}.`}
      </Sheet.Description>
    </Sheet.Header>
    <fieldset disabled={readonly} class="flex flex-col gap-3 px-4 pb-4">
      <legend class="sr-only">Automations for {laneName}</legend>
      {#if drawerItems.length === 0}
        <p class="text-body text-muted-foreground">No automations here yet.</p>
      {/if}
      {#each drawerItems as index (workflow.automations[index]!.id)}
        <AutomationForm
          bind:automation={workflow.automations[index]!}
          {index}
          {workflow}
          {errors}
          onremove={() => (workflow.automations = removeAt(workflow.automations, index))}
        />
      {/each}
      {#if !readonly}
        <div><Button variant="outline" onclick={addAutomation}>Add automation</Button></div>
      {/if}
    </fieldset>
  </Sheet.Content>
</Sheet.Root>
