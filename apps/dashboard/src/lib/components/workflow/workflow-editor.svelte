<script lang="ts">
  import type { AiUsePolicy, DefinitionError, EvaluationOutcome, HiringWorkflow, WorkflowSimulationResult } from '@screenhire/api-client';
  import { tick, untrack } from 'svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { laneOf, removeAt, withoutStage } from '#lib/workflow.js';
  import type { WorkflowTab } from '#lib/workflow-tabs.js';
  import { LANE_NODE, addAction, addAutomation, addCondition, connect, definitionStamp, focusGraphNode, stageNode, triggerNode } from '#lib/workflow-graph.js';
  import AutomationForm from './automation-form.svelte';
  import AutomationList from './automation-list.svelte';
  import AutomationRecipes from './automation-recipes.svelte';
  import AutomationSentence from './automation-sentence.svelte';
  import AutomationsPanel from './automations-panel.svelte';
  import JourneyOverview from './journey-overview.svelte';
  import ProblemsPanel from './problems-panel.svelte';
  import QuestionnaireBuilder from './questionnaire-builder.svelte';
  import ScorecardPicker from './scorecard-picker.svelte';
  import StageCards from './stage-cards.svelte';
  import StageSheet from './stage-sheet.svelte';
  import type { GraphActions } from './step-node.svelte';
  import TestDrive from './test-drive.svelte';

  // The workflow builder (workflow-engine.md §4) for a template's draft or a job's own copy: overview, pipeline,
  // questions, automations (the map or the list) and the test drive over one workflow, as the panes of the page's
  // `Tabs.Root` (`WorkflowTabs` sits on the header band). The page owns loading and saving, and shows the validation
  // result beside its title; `errors` are the API validator's, with paths relative to the workflow. A version that cannot
  // be edited is a `WorkflowSummary`.
  let {
    workflow = $bindable(),
    errors,
    checking = false,
    tab = $bindable(),
    hrefOf,
    aiUse,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    /** The validator's check of the latest change is on its way. */
    checking?: boolean;
    tab: WorkflowTab;
    /** The address that shows a tab, for the links that go to one (A5). */
    hrefOf: (tab: WorkflowTab) => string;
    /** The AI-use policy in force and where to change it, if the person may. Undefined while it loads. */
    aiUse?: { policy?: AiUsePolicy; href?: string };
  } = $props();

  // The stage whose sheet is open, by id, so reordering the stages behind it keeps the same one.
  let stageId = $state<string | null>(null);
  const openStage = (id: string) => (stageId = id);

  function removeStage(index: number) {
    const next = withoutStage(workflow, index);
    workflow.stages = next.stages;
    workflow.automations = next.automations;
  }

  // The last test's outcome per automation, lit on the map once the person asks to see it there. The automations tab
  // mounts only while it is shown, and a test is about the workflow as it was, so an edit clears it.
  let tested = $state<ReadonlyMap<string, EvaluationOutcome> | undefined>();
  const stamp = $derived(definitionStamp(workflow));
  $effect(() => {
    void stamp;
    untrack(() => (tested = undefined));
  });

  async function showTested(result: WorkflowSimulationResult) {
    tested = new Map(result.automations.map((automation) => [automation.automationId, automation.outcome]));
    tab = 'automations';
    // The button that asked leaves with its tab, so the focus goes to Clear, beside the words saying what is lit. On a
    // node it would light that node's path in place of the test's. A phone has the list, which has nothing to clear.
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

  // The automations sheet is one sheet with three views: a lane's automations as rows, the recipes to add from, and one
  // automation as a sentence (or, behind Edit as rules, as the full form). The automation is held by id, so editing the
  // list behind the sheet keeps the same one.
  let view = $state<'lane' | 'recipes' | 'edit'>('lane');
  let drawerLane = $state<string | null>(null);
  let editId = $state<string | null>(null);
  let asRules = $state(false);
  const editIndex = $derived(workflow.automations.findIndex((automation) => automation.id === editId));
  const laneName = $derived(drawerLane === null ? 'Whole workflow' : (workflow.stages.find((stage) => stage.id === drawerLane)?.name ?? drawerLane));
  const sheetTitle = $derived(
    view === 'lane' ? `Automations: ${laneName}` : view === 'recipes' ? 'Add an automation' : workflow.automations[editIndex]?.name || 'Untitled automation',
  );

  // Opened from the map or a row: the control to focus first (`data-focus` in the sentence and the form), and the map node
  // that takes the focus back when the sheet closes if the button that opened it is gone.
  let focusPath = $state<string | null>(null);
  let returnTo = $state<{ node: string; opener: Element | null } | null>(null);

  // Changing view inside the open sheet takes away the control that had the focus, so the focus goes to what the new view
  // starts with: the part a path names, else its first control. (A sheet that opens does this itself, in onOpenAutoFocus.)
  async function settle() {
    if (!drawerOpen) return;
    await tick();
    const sheet = document.querySelector<HTMLElement>('[data-slot="sheet-content"]');
    const target = (focusPath ? focusTarget(sheet, focusPath) : null) ?? sheet?.querySelector<HTMLElement>('button:not([data-slot="sheet-close"]), input, [href]');
    target?.focus();
  }

  function openAutomation(index: number, path: string) {
    const automation = workflow.automations[index];
    if (!automation) return;
    drawerLane = laneOf(automation);
    editId = automation.id;
    asRules = false;
    const wasOpen = drawerOpen;
    view = 'edit';
    focusPath = path;
    returnTo = { node: triggerNode(automation.id), opener: wasOpen ? returnTo?.opener ?? null : document.activeElement };
    drawerOpen = true;
    if (wasOpen) void settle();
  }

  // The recipes for a stage's lane, or the whole workflow's (null): what every Add an automation opens.
  function openRecipes(lane: string | null) {
    const wasOpen = drawerOpen;
    focusPath = null;
    drawerLane = lane;
    view = 'recipes';
    returnTo = { node: lane ? stageNode(lane) : LANE_NODE, opener: wasOpen ? (returnTo?.opener ?? null) : document.activeElement };
    drawerOpen = true;
    if (wasOpen) void settle();
  }

  function openLane(lane: string | null) {
    focusPath = null;
    drawerLane = lane;
    view = 'lane';
    returnTo = { node: lane ? stageNode(lane) : LANE_NODE, opener: document.activeElement };
    drawerOpen = true;
  }

  // Write your own: an empty automation in the lane, opened as a sentence at its name. Closing it goes to its trigger.
  function writeOwn() {
    const added = addAutomation(workflow, drawerLane);
    workflow.automations = added.workflow.automations;
    const index = workflow.automations.findIndex((automation) => automation.id === added.id);
    openAutomation(index, `automations[${index}]`);
    returnTo = { node: triggerNode(added.id), opener: null };
  }

  function editById(id: string) {
    const index = workflow.automations.findIndex((automation) => automation.id === id);
    if (index < 0) return;
    openAutomation(index, `automations[${index}]`);
    returnTo = { node: triggerNode(id), opener: null };
  }

  // The map's edits: each one a change to the workflow from #lib/workflow-graph.ts, or the automation's own sentence.
  const graphActions: GraphActions = {
    edit: openAutomation,
    openLane,
    add: openRecipes,
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

  // The sentence of an automation the map just changed, at the part it added; closing it goes to the automation's trigger.
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
    const invalid = sheet instanceof HTMLElement ? sheet.querySelector<HTMLElement>('[aria-invalid="true"], [data-invalid="true"]') : null;
    if (invalid) return invalid;
    const parts = [...document.querySelectorAll<HTMLElement>('[data-focus]')].filter((element) => {
      const key = element.dataset.focus ?? '';
      return path.startsWith(`${key}.`) || path.startsWith(`${key}[`);
    });
    return parts.sort((a, b) => (b.dataset.focus?.length ?? 0) - (a.dataset.focus?.length ?? 0))[0] ?? null;
  }

  // A problem's Fix: an automation's opens its sentence at the part; any other goes to its tab and the first field marked
  // invalid there.
  async function fix(path: string) {
    const automation = /^automations\[(\d+)\]/.exec(path);
    if (automation) {
      openAutomation(Number(automation[1]), path);
      return;
    }
    const stage = /^stages\[(\d+)\]/.exec(path);
    if (stage && workflow.stages[Number(stage[1])]) {
      stageId = workflow.stages[Number(stage[1])]!.id;
      return;
    }
    tab = /^(questionnaire|followUpQuestionnaires)/.test(path) ? 'questions' : 'pipeline';
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

  // Remove automation lives on the full form (Edit as rules), so the sentence's sheet holds only Name, On and Done.
  function removeEdited() {
    if (editIndex < 0) return;
    workflow.automations = removeAt(workflow.automations, editIndex);
    drawerOpen = false;
  }
</script>

<!-- The Test tab shows its own notice with the fix inline, so the list is left out there (W7). -->
{#if tab !== 'test'}<ProblemsPanel title="Problems to fix" {workflow} {errors} onfix={fix} />{/if}

<Tabs.Content value="overview">
  <JourneyOverview {workflow} onopen={openStage} questionsHref={hrefOf('questions')} />
</Tabs.Content>
<Tabs.Content value="pipeline">
  <StageCards bind:workflow {errors} onopen={openStage} onscorecard={openScorecard} onautomations={openRecipes} />
</Tabs.Content>
<Tabs.Content value="automations">
  <!-- Mounted only while shown: the tab panels stay in the page when hidden, and the map measures its nodes when it
       mounts, which it cannot do while hidden. -->
  {#if tab === 'automations'}
    <AutomationsPanel
      {workflow}
      {errors}
      {graphActions}
      {tested}
      onclear={() => (tested = undefined)}
      onopen={(index) => openAutomation(index, `automations[${index}]`)}
      onadd={() => openRecipes(null)}
    />
  {/if}
</Tabs.Content>
<Tabs.Content value="questions">
  <QuestionnaireBuilder bind:workflow {errors} {aiUse} />
</Tabs.Content>
<Tabs.Content value="test">
  <TestDrive bind:workflow {errors} {checking} onshow={showTested} onadd={() => openRecipes(null)} onfix={fix} automationsHref={hrefOf('automations')} />
</Tabs.Content>

<StageSheet
  bind:workflow
  bind:stageId
  {errors}
  automationsHref={hrefOf('automations')}
  onscorecard={(index) => {
    stageId = null;
    openScorecard(index);
  }}
  onautomations={(id) => {
    stageId = null;
    openRecipes(id);
  }}
  onremove={removeStage}
/>

<Sheet.Root bind:open={drawerOpen}>
  <Sheet.Content
    side="right"
    class={['w-full overflow-y-auto data-[side=right]:w-full', view === 'edit' && asRules ? 'data-[side=right]:sm:max-w-3xl' : 'data-[side=right]:sm:max-w-xl']}
    aria-describedby={undefined}
    {onOpenAutoFocus}
    {onCloseAutoFocus}
  >
    <Sheet.Header>
      <Sheet.Title class="pr-8">{sheetTitle}</Sheet.Title>
    </Sheet.Header>
    <div class="@container flex flex-col gap-6 px-4 pb-4">
      {#if view === 'lane'}
        <AutomationList {workflow} lane={drawerLane} onopen={(index) => openAutomation(index, `automations[${index}]`)} />
        <div><Button variant="outline" size="lg" onclick={() => openRecipes(drawerLane)}>Add automation</Button></div>
      {:else if view === 'recipes'}
        <AutomationRecipes bind:workflow lane={drawerLane} onwrite={writeOwn} onedit={editById} />
      {:else if editIndex >= 0}
        {#if asRules}
          <AutomationForm bind:automation={workflow.automations[editIndex]!} index={editIndex} {workflow} {errors} onremove={removeEdited} />
          <div><Button variant="outline" size="lg" onclick={() => (asRules = false)}>Edit as a sentence</Button></div>
        {:else}
          <AutomationSentence bind:automation={workflow.automations[editIndex]!} index={editIndex} {workflow} {errors} onrules={() => (asRules = true)} />
        {/if}
      {/if}
    </div>
    {#if view === 'edit'}
      <Sheet.Footer><Button size="lg" onclick={() => (drawerOpen = false)}>Done</Button></Sheet.Footer>
    {/if}
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
