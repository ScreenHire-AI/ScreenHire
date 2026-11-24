<script lang="ts">
  import type { AiUsePolicy, DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { onMount, tick, untrack } from 'svelte';
  import Trash2Icon from '~icons/lucide/trash-2';
  import { page } from '$app/state';
  import { APPLY_FORM_ID } from '#lib/forms-timeline.js';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import RecordTabs from '#lib/components/record-tabs.svelte';
  import UndoNotice from '#lib/components/undo-notice.svelte';
  import { addConditionBlank, addStepBlank, firstBlankPath, sentenceLimit, suggestedName } from '#lib/automation-sentence.js';
  import { preferences, type AutomationView } from '#lib/preferences.svelte.js';
  import { getUndo } from '#lib/undo-context.js';
  import { UndoOffer } from '#lib/undo.svelte.js';
  import { errorsUnder, isUnfinished, placeOf, withoutStage } from '#lib/workflow.js';
  import type { WorkflowTab } from '#lib/workflow-tabs.js';
  import { LANE_NODE, addAutomation, connect, focusGraphNode, removeAutomation, removeNode, removeStep, stageNode, tidyAutomation, triggerNode, type Removal } from '#lib/workflow-graph.js';
  import AutomationForm from './automation-form.svelte';
  import AutomationList from './automation-list.svelte';
  import AutomationName from './automation-name.svelte';
  import AutomationRecipes from './automation-recipes.svelte';
  import AutomationSentence from './automation-sentence.svelte';
  import AutomationsPanel from './automations-panel.svelte';
  import ProblemLines from './problem-lines.svelte';
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
  // automation, its name and On as the title over two tabs, Sentence and Rules, that edit the same automation. The automation is
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

  // Removing offers Undo, never a dialog (section 10). An open sheet keeps the focus inside it and covers the page's own
  // notice, so it draws its offers itself, and hands one over as it closes.
  const pageUndo = getUndo();
  const sheetUndo = new UndoOffer();
  function offer(message: string, undo: () => Promise<string | null>) {
    if (drawerOpen) sheetUndo.offer(message, undo);
    else pageUndo?.offer(message, undo);
  }

  /** Makes a removal and offers to take it back. */
  function remove(removal: Removal | null, node?: string) {
    if (!removal) return;
    workflow.automations = removal.workflow.automations;
    offerRemoval(removal, node);
  }

  // Undo takes the focus to the node that is there again.
  function offerRemoval(removal: Removal, node?: string) {
    offer(removal.message, async () => {
      workflow.automations = removal.restore(workflow).automations;
      await tick();
      if (node && !drawerOpen) focusGraphNode(document, node);
      return null;
    });
  }

  const removeById = (id: string) => remove(removeAutomation(workflow, id), triggerNode(id));

  // A condition or step taken out in the sheet. It is found by its automation's id, which Undo may reach after the sheet
  // has closed or moved to another one.
  function offerEdit(id: string, message: string, restore: (current: HiringWorkflow['automations'][number]) => HiringWorkflow['automations'][number]) {
    offer(message, async () => {
      workflow.automations = workflow.automations.map((automation) => (automation.id === id ? restore(automation) : automation));
      return null;
    });
  }

  // The automation opened by a problem's Fix, whose problems show while it is open. Any other open automation is work in
  // progress, so what it has not filled in yet shows no red until its sheet closes: not in the sheet, on the map or among the
  // problem lines. A refusal is not unfinished work and always shows, and the header, Save and Publish count every problem.
  let fixing = $state<string | null>(null);
  const inProgress = $derived(drawerOpen && view === 'edit' && editIndex >= 0 && editId !== fixing ? `automations[${editIndex}]` : null);
  const shownErrors = $derived(
    errors.filter((error) => {
      // A problem the validator found in an automation that has been removed since: gone with the next check.
      const index = /^automations\[(\d+)\]/.exec(error.path)?.[1];
      if (index !== undefined && !workflow.automations[Number(index)]) return false;
      return !(inProgress && errorsUnder([error], inProgress).length > 0 && isUnfinished(workflow, error));
    }),
  );

  // An automation's name follows its trigger and its steps until a person types one: a name that was the suggestion before a
  // change is the new suggestion after it, wherever the change was made.
  const suggestions = $derived(new Map(workflow.automations.map((automation) => [automation.id, suggestedName(automation, workflow)])));
  let followed = untrack(() => suggestions);
  $effect(() => {
    const next = suggestions;
    untrack(() => {
      for (const automation of workflow.automations) {
        const before = followed.get(automation.id);
        const after = next.get(automation.id)!;
        if (before !== undefined && before !== after && automation.name === before) automation.name = after;
      }
      followed = next;
    });
  });

  // Opened from the map or a row: the control to focus first (`data-focus` in the sentence and the form), and the map node
  // that takes the focus back when the sheet closes if the button that opened it is gone.
  let focusPath = $state<string | null>(null);
  let returnTo = $state<{ node: string; opener: Element | null } | null>(null);
  // Whether the control the focus goes to is opened too, where it is an empty text: a step just added, or a problem's Fix.
  let opensBlank = false;

  function focusIn(target: HTMLElement) {
    target.focus();
    if (opensBlank && target.dataset.empty !== undefined) target.click();
    opensBlank = false;
  }

  // Changing view inside the open sheet takes away the control that had the focus, so the focus goes to what the new view
  // starts with: the part a path names, else its first control. (A sheet that opens does this itself, in onOpenAutoFocus.)
  async function settle() {
    if (!drawerOpen) return;
    await tick();
    const sheet = document.querySelector<HTMLElement>('[data-slot="sheet-content"]');
    const target = (focusPath ? focusTarget(sheet, focusPath) : null) ?? sheet?.querySelector<HTMLElement>('button:not([data-slot="sheet-close"]), input, [href]');
    if (target) focusIn(target);
  }

  function openAutomation(index: number, path: string) {
    const automation = workflow.automations[index];
    if (!automation) return;
    fixing = null;
    opensBlank = false;
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

  // Write your own: an automation in the lane with only its trigger, opened there. Closing it goes to its trigger's node.
  function writeOwn() {
    const added = addAutomation(workflow, drawerLane, drawerAny);
    workflow.automations = added.workflow.automations;
    const index = workflow.automations.findIndex((automation) => automation.id === added.id);
    openAutomation(index, `automations[${index}].trigger`);
    returnTo = { node: triggerNode(added.id), opener: null };
  }

  function editById(id: string) {
    const index = workflow.automations.findIndex((automation) => automation.id === id);
    if (index < 0) return;
    openAutomation(index, `automations[${index}].trigger`);
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
    offers: (part, automationId) => {
      const index = workflow.automations.findIndex((automation) => automation.id === automationId);
      return part === 'condition' ? addConditionBlank(workflow, index) : addStepBlank(workflow, index);
    },
    addPart(part, automationId, choice) {
      const index = workflow.automations.findIndex((automation) => automation.id === automationId);
      const blank = part === 'condition' ? addConditionBlank(workflow, index) : addStepBlank(workflow, index);
      if (!blank) return;
      const next = blank.apply(choice);
      workflow.automations = workflow.automations.map((automation, at) => (at === index ? next : automation));
      openAutomation(index, firstBlankPath(next, index, part));
      opensBlank = true;
      returnTo = { node: triggerNode(automationId), opener: null };
    },
    remove: (nodeId) => remove(removeNode(workflow, nodeId), nodeId),
    connect(source, target) {
      workflow.automations = connect(workflow, source, target).automations;
    },
  };

  function onOpenAutoFocus(event: Event) {
    const target = focusPath ? focusTarget(event.currentTarget, focusPath) : null;
    if (!target) return;
    event.preventDefault();
    focusIn(target);
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
      fixing = editId;
      opensBlank = true;
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

  // An offer made by closing the sheet on an automation that went with it. It waits for the sheet to let go of the focus,
  // which then has nowhere to go back to, so the notice takes it and Undo is the next key press.
  let offerOnClose: (() => void) | null = null;

  function onCloseAutoFocus(event: Event) {
    const back = returnTo;
    focusPath = null;
    returnTo = null;
    const make = offerOnClose;
    offerOnClose = null;
    if (make && pageUndo) {
      event.preventDefault();
      make();
      return;
    }
    if (!back || (back.opener?.isConnected && back.opener !== document.body)) return;
    if (focusGraphNode(document, back.node) || focusGraphNode(document, drawerLane ? stageNode(drawerLane) : LANE_NODE)) event.preventDefault();
  }

  // However the sheet closes, what it leaves behind is tidied: an automation with no step goes, and so does a step with no
  // text, with Undo. Undoing that also puts back what was last taken out in the sheet, when its offer was still there.
  function closed() {
    const id = view === 'edit' ? editId : null;
    const removal = id ? tidyAutomation(workflow, id) : null;
    let earlier: (() => Promise<string | null>) | undefined;
    if (removal) sheetUndo.moveTo({ offer: (_, undo) => (earlier = undo) });
    else if (pageUndo) sheetUndo.moveTo(pageUndo);
    sheetUndo.dismiss();
    fixing = null;
    if (!removal || !id) return;
    workflow.automations = removal.workflow.automations;
    const offerIt = () =>
      pageUndo?.offer(removal.message, async () => {
        workflow.automations = removal.restore(workflow).automations;
        const failure = (await earlier?.()) ?? null;
        await tick();
        focusGraphNode(document, triggerNode(id));
        return failure;
      });
    if (workflow.automations.some((automation) => automation.id === id)) offerIt();
    else offerOnClose = offerIt;
  }

  function close() {
    drawerOpen = false;
    closed();
  }

  // Remove automation, in the sheet's menu on either tab.
  function removeEdited() {
    const id = editId;
    drawerOpen = false;
    sheetUndo.dismiss();
    fixing = null;
    const removal = id ? removeAutomation(workflow, id) : null;
    if (!removal || !id) return;
    workflow.automations = removal.workflow.automations;
    offerOnClose = () => offerRemoval(removal, triggerNode(id));
  }
</script>

<!-- The Try it tab shows its own notice with the fix inline, so the list is left out there (W7). -->
{#if tab !== 'test'}
  <ProblemLines
    {workflow}
    errors={shownErrors}
    onfix={fix}
    onremove={(automation, step) => {
      const id = workflow.automations[automation]?.id;
      if (id) remove(removeStep(workflow, id, step));
    }}
  />
{/if}

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
      errors={shownErrors}
      {graphActions}
      onopen={(index) => openAutomation(index, `automations[${index}].trigger`)}
      onadd={() => openRecipes(null)}
      onremove={removeById}
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

<Sheet.Root bind:open={drawerOpen} onOpenChange={(open) => !open && closed()}>
  <Sheet.Content
    side="right"
    class="w-full overflow-y-auto data-[side=right]:w-full data-[side=right]:sm:max-w-2xl"
    aria-describedby={undefined}
    {onOpenAutoFocus}
    {onCloseAutoFocus}
  >
    <!-- The name field is level with the close button. -->
    <Sheet.Header class={view === 'edit' ? 'pt-2 pr-12' : 'pr-12'}>
      <!-- The name is the title, as the field it is edited in, so the heading that names the sheet is for a screen reader. -->
      <Sheet.Title class={view === 'edit' ? 'sr-only' : undefined}>{sheetTitle}</Sheet.Title>
      {#if view === 'edit' && editIndex >= 0}
        <AutomationName bind:automation={workflow.automations[editIndex]!} index={editIndex} errors={shownErrors} suggested={suggestions.get(editId ?? '') ?? ''} />
      {/if}
    </Sheet.Header>
    <div class="@container flex flex-col gap-6 px-4 pb-4">
      {#if view === 'lane'}
        <AutomationList {workflow} lane={drawerLane} onopen={(index) => openAutomation(index, `automations[${index}].trigger`)} onremove={removeById} />
        <div><Button variant="outline" size="lg" onclick={() => openRecipes(drawerLane, drawerAny)}>Add automation</Button></div>
      {:else if view === 'recipes'}
        <AutomationRecipes bind:workflow lane={drawerLane} onwrite={writeOwn} onedit={editById} onremove={removeById} />
      {:else if editIndex >= 0}
        <!-- Its own Tabs.Root: the sheet sits inside the page's, whose tabs these are not. Each panel stays in the page for
             its tab's aria-controls, but only the shown one holds its view, so a data-focus path names one control. -->
        <Tabs.Root value={editView} onValueChange={chooseView} class="flex flex-col gap-4">
          <RecordTabs tabs={viewTabs} label="Edit the automation as" class="-mt-2 border-b border-border-subtle" />
          <Tabs.Content value="sentence">
            {#if editView === 'sentence'}
              {#if limit}
                <p data-sentence-limit class="text-body text-muted-foreground">{limit}</p>
              {:else}
                {@const id = workflow.automations[editIndex]!.id}
                <AutomationSentence bind:automation={workflow.automations[editIndex]!} index={editIndex} {workflow} errors={shownErrors} onundo={(message, restore) => offerEdit(id, message, restore)} />
              {/if}
            {/if}
          </Tabs.Content>
          <Tabs.Content value="rules">
            {#if editView === 'rules'}
              {@const id = workflow.automations[editIndex]!.id}
              <AutomationForm bind:automation={workflow.automations[editIndex]!} index={editIndex} {workflow} errors={shownErrors} onundo={(message, restore) => offerEdit(id, message, restore)} />
            {/if}
          </Tabs.Content>
        </Tabs.Root>
      {/if}
    </div>
    {#if view === 'edit'}
      <Sheet.Footer>
        <MoreMenu label={`More for ${sheetTitle}`} class="mr-auto">
          <DropdownMenu.Item onSelect={removeEdited}><Trash2Icon aria-hidden="true" />Remove automation</DropdownMenu.Item>
        </MoreMenu>
        <Button size="lg" onclick={close}>Done</Button>
      </Sheet.Footer>
    {/if}
    <UndoNotice offer={sheetUndo} />
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
    <Sheet.Footer><Button size="lg" onclick={() => (scorecardOpen = false)}>Done</Button></Sheet.Footer>
  </Sheet.Content>
</Sheet.Root>
