<script lang="ts">
  import type { AiUsePolicy, DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { onMount, tick } from 'svelte';
  import { page } from '$app/state';
  import { APPLY_FORM_ID } from '#lib/forms-timeline.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import RecordTabs from '#lib/components/record-tabs.svelte';
  import { sentenceLimit } from '#lib/automation-sentence.js';
  import { preferences, type AutomationView } from '#lib/preferences.svelte.js';
  import { placeOf, removeAt, withoutStage } from '#lib/workflow.js';
  import type { WorkflowTab } from '#lib/workflow-tabs.js';
  import { LANE_NODE, addAction, addAutomation, addCondition, connect, focusGraphNode, stageNode, triggerNode } from '#lib/workflow-graph.js';
  import AutomationForm from './automation-form.svelte';
  import AutomationList from './automation-list.svelte';
  import AutomationName from './automation-name.svelte';
  import AutomationRecipes from './automation-recipes.svelte';
  import AutomationSentence from './automation-sentence.svelte';
  import AutomationsPanel from './automations-panel.svelte';
  import ProblemsPanel from './problems-panel.svelte';
  import QuestionnaireBuilder from './questionnaire-builder.svelte';
  import ScorecardPicker from './scorecard-picker.svelte';
  import StageCards from './stage-cards.svelte';
  import StageSheet from './stage-sheet.svelte';
  import type { GraphActions } from './step-node.svelte';
  import TryIt from './try-it.svelte';

  // The pipeline builder (workflow-engine.md §4) for a template's draft or a job's own copy: the pipeline's stages,
  // questions, automations (the map or the list) and Try it over one workflow, as the panes of the page's
  // `Tabs.Root` (`WorkflowTabs` sits on the header band). The page owns loading and saving, and shows the validation
  // result beside its title; `errors` are the API validator's, with paths relative to the workflow. A version that cannot
  // be edited is a `WorkflowSummary`.
  let {
    workflow = $bindable(),
    errors,
    tab = $bindable(),
    hrefOf,
    aiUse,
    templateId,
    finish,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    tab: WorkflowTab;
    /** The address that shows a tab, for the links that go to one (A5). */
    hrefOf: (tab: WorkflowTab) => string;
    /** The AI-use policy in force and where to change it, if the person may. Undefined while it loads. */
    aiUse?: { policy?: AiUsePolicy; href?: string };
    /** The hiring template being edited, so a new library form made from its Forms tab comes back to it; a job has none. */
    templateId?: string;
    /** The page's own action at the end of Try it: "Looks good, publish" on a template's draft, "Looks good, save" on a job's. */
    finish?: { label: string; run: () => void; disabled?: boolean };
  } = $props();

  // The stage whose sheet is open, by id, so reordering the stages behind it keeps the same one.
  let stageId = $state<string | null>(null);
  const openStage = (id: string) => (stageId = id);

  function removeStage(index: number) {
    const next = withoutStage(workflow, index);
    workflow.stages = next.stages;
    workflow.automations = next.automations;
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
  // automation, with its Name and On over two tabs, Sentence and Rules, that edit the same automation. The automation is
  // held by id, so editing the list behind the sheet keeps the same one. The sheet keeps one width in every view, so
  // switching changes only what is in it.
  let view = $state<'lane' | 'recipes' | 'edit'>('lane');
  let drawerLane = $state<string | null>(null);
  // Opened from the Any stage band (its header or its plus), not from Add automation: Write your own then stays in the band.
  let drawerAny = $state(false);
  let editId = $state<string | null>(null);
  const editIndex = $derived(workflow.automations.findIndex((automation) => automation.id === editId));
  // The tab the automation shows: the person's last choice (`preferences`), or Rules for one too complex for a sentence,
  // whose Sentence tab then says why. Only a click on a tab changes the choice that is remembered.
  let editView = $state<AutomationView>('sentence');
  const limit = $derived(editIndex >= 0 ? sentenceLimit(workflow, editIndex) : null);
  const viewTabs = [
    { value: 'sentence', label: 'Sentence' },
    { value: 'rules', label: 'Rules' },
  ];
  function chooseView(next: string) {
    if (next !== 'sentence' && next !== 'rules') return;
    editView = next;
    preferences.setAutomationView(next);
  }
  const laneName = $derived(drawerLane === null ? 'Any stage' : (workflow.stages.find((stage) => stage.id === drawerLane)?.name ?? drawerLane));
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
    const place = placeOf(workflow, automation);
    drawerLane = place === 'any' ? null : place;
    drawerAny = place === 'any';
    editId = automation.id;
    editView = sentenceLimit(workflow, index) ? 'rules' : preferences.automationView;
    const wasOpen = drawerOpen;
    view = 'edit';
    focusPath = path;
    returnTo = { node: triggerNode(automation.id), opener: wasOpen ? returnTo?.opener ?? null : document.activeElement };
    drawerOpen = true;
    if (wasOpen) void settle();
  }

  // The recipes for a stage, or every recipe (null): what every Add an automation opens.
  function openRecipes(lane: string | null, anyStage = false) {
    const wasOpen = drawerOpen;
    focusPath = null;
    drawerLane = lane;
    drawerAny = lane === null && anyStage;
    view = 'recipes';
    returnTo = { node: lane ? stageNode(lane) : LANE_NODE, opener: wasOpen ? (returnTo?.opener ?? null) : document.activeElement };
    drawerOpen = true;
    if (wasOpen) void settle();
  }

  function openLane(lane: string | null) {
    focusPath = null;
    drawerLane = lane;
    drawerAny = lane === null;
    view = 'lane';
    returnTo = { node: lane ? stageNode(lane) : LANE_NODE, opener: document.activeElement };
    drawerOpen = true;
  }

  // Write your own: an empty automation in the lane, opened as a sentence at its name. Closing it goes to its trigger.
  function writeOwn() {
    const added = addAutomation(workflow, drawerLane, drawerAny);
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

  // A stage card's or the stage sheet's line names one automation: it opens on the Automations tab, as a sentence. The
  // link's own address (`?tab=automations&show=<id>`) does the same when it is opened in a new tab or loaded.
  function automationHref(id: string): string {
    const [path, query = ''] = hrefOf('automations').split('?');
    const params = new URLSearchParams(query);
    params.set('show', id);
    return `${path}?${params}`;
  }

  function showAutomation(id: string) {
    tab = 'automations';
    editById(id);
  }

  // A stage's form item opens that form on the Forms tab. Its link (`?tab=forms&form=<id>`) does the same when loaded,
  // but the Forms tab is already mounted here and reads `form` only once, so a click chooses it directly.
  let shownForm = $state(page.url.searchParams.get('form') ?? APPLY_FORM_ID);
  function showForm(id: string) {
    tab = 'forms';
    shownForm = id;
  }

  onMount(() => {
    const shown = new URLSearchParams(location.search).get('show');
    if (tab === 'automations' && shown && workflow.automations.some((automation) => automation.id === shown)) editById(shown);
  });

  // The stage sheet's "Add a reminder": an automation for when the stage's time limit passes, which tells the team (the
  // first of the steps a passed time limit may take), opened on the Automations tab to say more.
  function addReminder(stage: string) {
    const added = addAutomation(workflow, stage);
    const name = workflow.stages.find((candidate) => candidate.id === stage)?.name ?? 'this stage';
    workflow.automations = added.workflow.automations.map((automation) =>
      automation.id === added.id
        ? {
            ...automation,
            name: `Remind the team when ${name} is overdue`,
            trigger: { type: 'SlaBreached' as const, stageId: stage },
            conditions: null,
            actions: [{ type: 'NotifyTeam' as const, note: `Someone has waited in ${name} longer than the time limit.` }],
          }
        : automation,
    );
    showAutomation(added.id);
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
    tab = /^(questionnaire|followUpQuestionnaires)/.test(path) ? 'forms' : 'pipeline';
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

  // Remove automation lives at the foot of the Rules tab, so the Sentence tab holds only the sentence.
  function removeEdited() {
    if (editIndex < 0) return;
    workflow.automations = removeAt(workflow.automations, editIndex);
    drawerOpen = false;
  }
</script>

<!-- The Try it tab shows its own notice with the fix inline, so the list is left out there (W7). -->
{#if tab !== 'test'}<ProblemsPanel title="Problems to fix" {workflow} {errors} onfix={fix} />{/if}

<Tabs.Content value="pipeline">
  <StageCards
    bind:workflow
    {errors}
    onopen={openStage}
    onscorecard={openScorecard}
    onautomations={openRecipes}
    {automationHref}
    questionsHref={hrefOf('forms')}
    onshow={showAutomation}
    onshowform={showForm}
  />
</Tabs.Content>
<Tabs.Content value="automations">
  <!-- Mounted only while shown: the tab panels stay in the page when hidden, and the map measures its nodes when it
       mounts, which it cannot do while hidden. -->
  {#if tab === 'automations'}
    <AutomationsPanel
      {workflow}
      {errors}
      {graphActions}
      onopen={(index) => openAutomation(index, `automations[${index}]`)}
      onadd={() => openRecipes(null)}
    />
  {/if}
</Tabs.Content>
<Tabs.Content value="forms">
  <QuestionnaireBuilder bind:workflow bind:selected={shownForm} {errors} {aiUse} automationsHref={hrefOf('automations')} {automationHref} onshow={showAutomation} {templateId} />
</Tabs.Content>
<Tabs.Content value="test">
  <TryIt bind:workflow {errors} onfix={fix} aiUse={aiUse?.policy} {finish} onchange={openStage} />
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
  onreminder={(id) => {
    stageId = null;
    addReminder(id);
  }}
  {automationHref}
  onshow={(id) => {
    stageId = null;
    showAutomation(id);
  }}
/>

<Sheet.Root bind:open={drawerOpen}>
  <Sheet.Content
    side="right"
    class="w-full overflow-y-auto data-[side=right]:w-full data-[side=right]:sm:max-w-2xl"
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
        <div><Button variant="outline" size="lg" onclick={() => openRecipes(drawerLane, drawerAny)}>Add automation</Button></div>
      {:else if view === 'recipes'}
        <AutomationRecipes bind:workflow lane={drawerLane} onwrite={writeOwn} onedit={editById} />
      {:else if editIndex >= 0}
        <!-- Its own Tabs.Root: the sheet sits inside the page's, whose tabs these are not. Each panel stays in the page for
             its tab's aria-controls, but only the shown one holds its view, so a data-focus path names one control. -->
        <Tabs.Root value={editView} onValueChange={chooseView} class="flex flex-col gap-4">
          <RecordTabs tabs={viewTabs} label="Edit the automation as" class="-mt-2 border-b border-border-subtle" />
          <AutomationName bind:automation={workflow.automations[editIndex]!} index={editIndex} {errors} />
          <Tabs.Content value="sentence">
            {#if editView === 'sentence'}
              {#if limit}
                <p data-sentence-limit class="text-body text-muted-foreground">{limit}</p>
              {:else}
                <AutomationSentence bind:automation={workflow.automations[editIndex]!} index={editIndex} {workflow} {errors} />
              {/if}
            {/if}
          </Tabs.Content>
          <Tabs.Content value="rules">
            {#if editView === 'rules'}
              <AutomationForm bind:automation={workflow.automations[editIndex]!} index={editIndex} {workflow} {errors} onremove={removeEdited} />
            {/if}
          </Tabs.Content>
        </Tabs.Root>
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
