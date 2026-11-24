<script lang="ts" module>
  // Space picks a card up for a keyboard drag; Enter opens it, as it does everywhere else in the dashboard.
  import '#lib/dnd.js';
</script>

<script lang="ts">
  import type { ApplicationSummary, StageKind, WorkflowStage } from '@screenhire/api-client';
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import { tick } from 'svelte';
  import { SvelteSet } from 'svelte/reactivity';
  import { dndzone, TRIGGERS, type DndEvent } from 'svelte-dnd-action';
  import { goto } from '$app/navigation';
  import AiMark from '#lib/components/ai-mark.svelte';
  import VerificationChip from '#lib/components/verification-chip.svelte';
  import ApplicationMenu from '#lib/components/application-menu.svelte';
  import OverdueBadge from '#lib/components/overdue-badge.svelte';
  import Rating from '#lib/components/rating.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { clock } from '#lib/clock.svelte.js';
  import { initials } from '#lib/format.js';
  import { timeSince } from '#lib/dates.js';
  import { aiMarkOf, stateOf } from '#lib/verification.js';
  import type { Moves } from '#lib/moves.svelte.js';
  import { hasModifier } from '#lib/shortcuts.js';
  import { isOverdue } from '#lib/sla.js';
  import StatusIcon from '#lib/components/status-icon.svelte';
  import type { StatusName } from '#lib/status.js';
  import { keptInMindLabel } from '#lib/talent-pool.js';

  // A job's candidates by stage (design system sections 5 and 21). Every stage column shares the board's width equally, at
  // most 384px, so a board with two busy stages does not stretch their cards across half the screen (the owner's
  // walk-through, 2026-11-21). When the stages do not fit, a column narrows to its least: 264px with candidates, 188px
  // empty, so the empty ones give way first. The stages that end a candidacy share one 188px Closed column that counts
  // each of them; each of its rows opens in place to list those candidates (A9), and the table has them too, under the
  // Stage filter. Where the columns need more than the window, the board scrolls inside itself, sideways and down each
  // column, and never moves the page. A card is at most three rows: the name with any status that needs a look and the
  // menu; the rating and tags; when they applied and, once they have moved on, how long they have been in this stage.
  let {
    applications,
    stages,
    moves,
  }: {
    applications: readonly ApplicationSummary[];
    stages: readonly WorkflowStage[];
    moves: Moves;
  } = $props();

  type Card = ApplicationSummary & { isDndShadowItem?: boolean };
  type Column = { stage: WorkflowStage; cards: Card[] };

  const terminalKinds = new Set<StageKind>(['Hired', 'Rejected', 'Withdrawn']);
  const openStages = $derived(stages.filter((stage) => !terminalKinds.has(stage.kind)));
  const closedStages = $derived(stages.filter((stage) => terminalKinds.has(stage.kind)));
  // A saved candidate has left the job (docs/architecture/talent-pool.md): their stage is where they stood, so
  // they count in Closed, never in that stage's column.
  const stillIn = $derived(applications.filter((application) => !application.keptInMindAt));
  const countIn = (stageId: string) => stillIn.filter((application) => application.stage.id === stageId).length;
  const closedCount = $derived(closedStages.reduce((sum, stage) => sum + countIn(stage.id), 0) + (applications.length - stillIn.length));

  // Closed's rows start collapsed; the open ones are kept by key (a stage's id, or `kept` for Saved), so a move
  // that redraws the board leaves them as the person set them.
  const keptKey = 'kept';
  const expandedClosed = new SvelteSet<string>();
  const toggleClosed = (key: string) => (expandedClosed.has(key) ? expandedClosed.delete(key) : expandedClosed.add(key));

  function build(): Column[] {
    return openStages.map((stage) => ({ stage, cards: stillIn.filter((application) => application.stage.id === stage.id) }));
  }

  // Rebuilt whenever the applications change; a drag reassigns it while the card is in flight.
  let columns = $derived<Column[]>(build());
  // Bumped when a drop is cancelled, so the columns are drawn afresh: the drag library keeps its own record of the DOM it
  // moved, and a card put back in its old column through state alone can be left out of it.
  let drawn = $state(0);
  // A closed stage's drop zone holds only the card in flight; its candidates are listed under its row when that is open.
  let landing = $derived<Record<string, Card[]>>(Object.fromEntries(closedStages.map((stage) => [stage.id, []])));

  function replaceCards(stageId: string, cards: Card[]) {
    columns = columns.map((column) => (column.stage.id === stageId ? { ...column, cards } : column));
  }

  // A drop asks first when the stage has something to do (the move confirmation): the card waits in its new column for the
  // answer, and Cancel, or a move that did not happen, rebuilds the columns from the applications, which puts it back.
  async function dropped(stage: WorkflowStage, event: CustomEvent<DndEvent<Card>>) {
    if (event.detail.info.trigger !== TRIGGERS.DROPPED_INTO_ZONE) return;
    const card = event.detail.items.find((item) => item.id === event.detail.info.id);
    if (!card || card.stage.id === stage.id || !moves.allowed) return;
    if (await moves.move(card, stage)) return;
    columns = build();
    drawn++;
    // Redrawing the columns takes the focus with the old cards; it goes to the card back in its place.
    await tick();
    const active = document.activeElement;
    if (!active || active === document.body) document.querySelector<HTMLElement>(`[data-card-id="${card.id}"]`)?.focus();
  }

  function onfinalize(stage: WorkflowStage, event: CustomEvent<DndEvent<Card>>) {
    replaceCards(stage.id, event.detail.items);
    void dropped(stage, event);
  }

  function onfinalizeClosed(stage: WorkflowStage, event: CustomEvent<DndEvent<Card>>) {
    landing = { ...landing, [stage.id]: [] };
    void dropped(stage, event);
  }

  // The keyboard alternative to dragging: Shift + Left / Right moves the focused card one stage, and focus follows it,
  // to the closed stage's row when the card leaves the board for one.
  async function moveBy(card: Card, delta: number) {
    const index = stages.findIndex((stage) => stage.id === card.stage.id);
    const target = stages[index + delta];
    if (!target || !moves.allowed) return;
    await moves.move(card, target);
    await tick();
    // Cancelled, the card is where it was and keeps the focus.
    const focusable = document.querySelector<HTMLElement>(`[data-card-id="${card.id}"]`) ?? document.querySelector<HTMLElement>(`[data-closed-stage="${target.id}"]`);
    focusable?.focus();
  }

  function onkeydown(card: Card, event: KeyboardEvent) {
    if (event.target !== event.currentTarget || hasModifier(event)) return;
    if (event.shiftKey && (event.key === 'ArrowRight' || event.key === 'ArrowLeft')) {
      event.preventDefault();
      void moveBy(card, event.key === 'ArrowRight' ? 1 : -1);
    } else if (event.key === 'Enter') {
      event.preventDefault();
      void goto(`/applications/${card.id}`);
    }
  }

  const zone = {
    type: 'pipeline',
    flipDurationMs: 150,
    dropTargetClasses: ['outline-2', 'outline-ring/50'],
    // The card in hand is the raised layer (design system section 6).
    transformDraggedElement: (element: HTMLElement | undefined) => element?.classList.add('shadow-raised'),
  };
</script>

<!-- One of Closed's rows (A9): the whole row is the button that opens its candidates in place, with the chevron that says so.
     A row with nobody in it has nothing to open, so it has no chevron and is not a button. Opening shows plain cards that
     link to the candidate, as the open columns' cards do; they are not draggable, because the menu on an open column's
     card is the way to move someone. -->
{#snippet closedRow(key: string, name: string, cards: readonly ApplicationSummary[], status?: StatusName)}
  {@const open = expandedClosed.has(key)}
  {#if cards.length === 0}
    <span class="flex min-w-0 items-center gap-2 px-2 py-1.5 text-body text-muted-foreground">
      {#if status}<StatusIcon {status} />{/if}
      <span class="min-w-0 flex-1 break-words">{name}</span>
      <span class="tabular-nums">0</span>
    </span>
  {:else}
    <button
      type="button"
      data-closed-row={key}
      aria-expanded={open}
      aria-controls={open ? `closed-${key}` : undefined}
      class="flex min-h-9 w-full min-w-0 items-center gap-2 rounded-md px-2 py-1.5 text-left text-body text-muted-foreground hover:bg-card focus-visible:outline-2 focus-visible:outline-ring pointer-coarse:min-h-11"
      onclick={() => toggleClosed(key)}
    >
      {#if status}<StatusIcon {status} />{/if}
      <span class="min-w-0 break-words">{name}</span>
      <ChevronDownIcon aria-hidden="true" class={['size-4 shrink-0 transition-transform', open && 'rotate-180']} />
      <span class="ml-auto tabular-nums">{cards.length}</span>
    </button>
    {#if open}
      <ul id={`closed-${key}`} aria-label={`${name} candidates`} class="flex flex-col gap-2 pt-1 pb-1">
        {#each cards as card (card.id)}
          <li
            data-closed-card-id={card.id}
            aria-label={`${card.candidateName}, ${name}`}
            class="relative flex flex-col gap-2 rounded-lg border border-border-subtle bg-card p-3 shadow-surface"
          >
            <span class="flex min-w-0 items-center gap-2">
              <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-caption font-bold text-secondary-foreground"
                >{initials(card.candidateName)}</span
              >
              <!-- This link is the card's only control, so its target is the whole card (F4). -->
              <TruncatedText
                href={`/applications/${card.id}`}
                text={card.candidateName}
                class="font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline"
              />
            </span>
            <span class="truncate text-caption text-muted-foreground"><RelativeTime at={card.submittedAt} /></span>
          </li>
        {/each}
      </ul>
    {/if}
  {/if}
{/snippet}

<!-- Each column is an inset (design system section 6). relative, here and on each column's list: the chips' sr-only
     labels are absolute, and without a positioned scroller they escape it, widen the page (F1) and give the board a
     second vertical scroll. -->
<div data-pipeline-board class="relative min-h-0 flex-1 overflow-auto">
  <div class="flex h-full w-full min-w-fit items-start gap-3 p-3">
    {#key drawn}
    {#each columns as column (column.stage.id)}
      {@const count = countIn(column.stage.id)}
      <section
        aria-labelledby={`stage-${column.stage.id}`}
        data-stage-column={column.stage.id}
        class={['flex max-h-full max-w-96 flex-1 basis-0 flex-col rounded-lg bg-secondary', count > 0 ? 'min-w-66' : 'min-w-47']}
      >
        <h3 id={`stage-${column.stage.id}`} class="flex min-w-0 items-center gap-2 px-3 pt-3 pb-2 text-body font-semibold">
          <TruncatedText text={column.stage.name} />
          <Badge variant="count" class="bg-card">{count}</Badge>
        </h3>
        <ul
          aria-label={`${column.stage.name} stage`}
          class="relative flex min-h-24 flex-1 flex-col gap-2 overflow-y-auto px-2 pb-2"
          use:dndzone={{ ...zone, items: column.cards, dragDisabled: !moves.allowed }}
          onconsider={(event) => replaceCards(column.stage.id, event.detail.items)}
          onfinalize={(event) => onfinalize(column.stage, event)}
        >
          {#each column.cards as card (card.id)}
            {@const state = stateOf(card.verification)}
            {@const ai = aiMarkOf(card.verification)}
            <!-- Calm says nothing (section 21): the name shows a status only when it needs a look or is still waiting. -->
            {@const showStatus = isOverdue(card, clock.now) || state.kind !== 'clear' || ai !== undefined}
            {@const rated = card.rating?.state === 'Rated' || card.rating?.state === 'AfterYourScorecard'}
            <!-- svelte-ignore a11y_no_noninteractive_element_interactions: svelte-dnd-action makes each card focusable (tabindex 0) and draggable; these keys are its non-drag alternative -->
            <li
              data-card-id={card.id}
              aria-label={`${card.candidateName}, ${column.stage.name}`}
              class="flex flex-col gap-2 rounded-lg border border-border-subtle bg-card p-3 shadow-surface outline-offset-2"
              onkeydown={(event) => onkeydown(card, event)}
            >
              <!-- Row 1: who, then any status that needs a look (T2; calm says nothing, section 21), as the application's own
                   header reads; a long name pushes the status to the next line. The menu sits top right, always visible and
                   muted (F4), because it moves the card without a drag. -->
              <span class="flex min-w-0 items-start gap-2">
                <span
                  aria-hidden="true"
                  class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-caption font-bold text-secondary-foreground"
                  >{initials(card.candidateName)}</span
                >
                <span class="flex min-h-7 min-w-0 flex-1 flex-wrap items-center gap-x-2 gap-y-1">
                  <span class="min-w-0 max-w-full">
                    <TruncatedText
                      href={`/applications/${card.id}`}
                      tabindex={-1}
                      text={card.candidateName}
                      class="font-semibold text-foreground no-underline hover:underline"
                    />
                  </span>
                  {#if showStatus}
                    <!-- Overdue is the team's status and leads. -->
                    <OverdueBadge dueAt={card.slaDueAt} />
                    {#if state.kind !== 'clear'}
                      <VerificationChip {state} reasons={card.verification?.authenticityReasons.length ?? 0} />
                    {/if}
                    <AiMark level={ai} />
                  {/if}
                </span>
                {#if moves.allowed}
                  <span
                    class="-my-1 shrink-0 text-muted-foreground"
                  >
                    <ApplicationMenu candidateName={card.candidateName} currentStageId={card.stage.id} {stages} onmove={(stage) => moves.move(card, stage)} />
                  </span>
                {/if}
              </span>
              {#if rated || card.tags.length > 0}
                <!-- Row 2: the rating, then the first three tags as facts and how many more (W3, never links). -->
                <span class="flex flex-wrap items-center gap-1.5">
                  {#if rated}<span class="relative shrink-0"><Rating rating={card.rating!} /></span>{/if}
                  {#each card.tags.slice(0, 3) as tag (tag)}<Badge variant="muted" class="max-w-full"><TruncatedText text={tag} /></Badge>{/each}
                  {#if card.tags.length > 3}<Badge variant="muted">+{card.tags.length - 3}</Badge>{/if}
                </span>
              {/if}
              <!-- Row 3: when they applied and, only once they have moved on from where they started, how long they have
                   been in this stage; until then it would say the same. -->
              <span class="flex min-w-0 flex-wrap gap-x-3 text-caption text-muted-foreground">
                <span>Applied <RelativeTime at={card.submittedAt} class="inline" /></span>
                {#if Date.parse(card.stageEnteredAt) - Date.parse(card.submittedAt) > 60_000}
                  <span>In this stage {timeSince(card.stageEnteredAt, clock.now)}</span>
                {/if}
              </span>
            </li>
          {/each}
        </ul>
      </section>
    {/each}

    {#if closedStages.length > 0}
      <section aria-labelledby="stage-closed" data-stage-column="closed" class="flex max-h-full w-47 shrink-0 flex-col rounded-lg bg-secondary">
        <h3 id="stage-closed" class="flex items-center gap-2 px-3 pt-3 pb-2 text-body font-semibold">
          Closed
          <Badge variant="count" class="bg-card">{closedCount}</Badge>
        </h3>
        <div class="flex min-h-0 flex-col gap-1 overflow-y-auto px-2 pb-2">
          {#each closedStages as stage (stage.id)}
            <!-- A drop target for a card leaving the pipeline; the menu on each card is the non-drag way (section 17). -->
            <div data-closed-stage={stage.id} tabindex="-1" class="flex flex-col rounded-md outline-offset-2">
              {@render closedRow(stage.id, stage.name, stillIn.filter((application) => application.stage.id === stage.id))}
              <ul
                aria-label={`${stage.name} stage`}
                class="flex min-h-2 flex-col gap-2"
                use:dndzone={{ ...zone, items: landing[stage.id] ?? [], dragDisabled: true }}
                onconsider={(event) => (landing = { ...landing, [stage.id]: event.detail.items })}
                onfinalize={(event) => onfinalizeClosed(stage, event)}
              >
                {#each landing[stage.id] ?? [] as card (card.id)}
                  <li class="h-10 rounded-lg border border-dashed border-input"><span class="sr-only">{card.candidateName}</span></li>
                {/each}
              </ul>
            </div>
          {/each}
          <!-- Saved is a way out of the job too (talent-pool.md), so Closed's rows add up to its count. It takes no
               drop: saving someone needs their permission and a note, which the candidate's menu asks for. -->
          {#if applications.length > stillIn.length}
            <div data-kept-in-mind class="flex flex-col rounded-md">
              {@render closedRow(keptKey, keptInMindLabel, applications.filter((application) => application.keptInMindAt), 'saved')}
            </div>
          {/if}
        </div>
      </section>
    {/if}
    {/key}
  </div>
</div>
