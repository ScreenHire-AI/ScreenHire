<script lang="ts" module>
  import { setKeyboardDragTrigger } from 'svelte-dnd-action';

  // Space picks a card up for a keyboard drag; Enter opens it, as it does everywhere else in the dashboard.
  setKeyboardDragTrigger('space');
</script>

<script lang="ts">
  import type { ApplicationSummary, WorkflowStage } from '@screenhire/api-client';
  import { tick } from 'svelte';
  import { dndzone, TRIGGERS, type DndEvent } from 'svelte-dnd-action';
  import { goto } from '$app/navigation';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import { formatDateTime, timeAgo } from '#lib/dates.js';
  import { initials, sourceLabel } from '#lib/format.js';
  import type { Moves } from '#lib/moves.svelte.js';
  import { hasModifier } from '#lib/shortcuts.js';

  let {
    applications,
    stages,
    moves,
    now = Date.now(),
  }: {
    applications: readonly ApplicationSummary[];
    stages: readonly WorkflowStage[];
    moves: Moves;
    /** The clock for "applied" ages; tests fix it. */
    now?: number;
  } = $props();

  type Card = ApplicationSummary & { isDndShadowItem?: boolean };
  type Column = { stage: WorkflowStage; cards: Card[] };

  function build(): Column[] {
    return stages.map((stage) => ({ stage, cards: applications.filter((application) => application.stage.id === stage.id) }));
  }

  // Rebuilt whenever the applications change; a drag reassigns it while the card is in flight.
  let columns = $derived<Column[]>(build());

  function replaceCards(stageId: string, cards: Card[]) {
    columns = columns.map((column) => (column.stage.id === stageId ? { ...column, cards } : column));
  }

  function onconsider(stageId: string, event: CustomEvent<DndEvent<Card>>) {
    replaceCards(stageId, event.detail.items);
  }

  function onfinalize(stage: WorkflowStage, event: CustomEvent<DndEvent<Card>>) {
    replaceCards(stage.id, event.detail.items);
    if (event.detail.info.trigger !== TRIGGERS.DROPPED_INTO_ZONE) return;
    const dropped = event.detail.items.find((card) => card.id === event.detail.info.id);
    if (dropped && dropped.stage.id !== stage.id) {
      void moves.move(dropped, stage);
    }
  }

  // The keyboard alternative to dragging: Shift + Left / Right moves the focused card one stage, and focus follows it.
  async function moveBy(card: Card, delta: number) {
    const index = stages.findIndex((stage) => stage.id === card.stage.id);
    const target = stages[index + delta];
    if (!target) return;
    await moves.move(card, target);
    await tick();
    document.querySelector<HTMLElement>(`[data-card-id="${card.id}"]`)?.focus();
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
</script>

<div class="overflow-x-auto">
  <div class="flex min-w-max gap-3 p-3">
    {#each columns as column (column.stage.id)}
      <section aria-labelledby={`stage-${column.stage.id}`} class="flex w-60 flex-col rounded-lg bg-secondary">
        <h3 id={`stage-${column.stage.id}`} class="flex items-center justify-between px-3 pt-2.5 pb-1.5 text-body font-bold">
          {column.stage.name}
          <span class="text-caption text-muted-foreground tabular-nums">{column.cards.filter((card) => !card.isDndShadowItem).length}</span>
        </h3>
        <ul
          aria-label={`${column.stage.name} stage`}
          class="flex min-h-24 flex-1 flex-col gap-2 px-2 pb-2"
          use:dndzone={{ items: column.cards, type: 'pipeline', flipDurationMs: 150, dropTargetClasses: ['outline-2', 'outline-ring/50'] }}
          onconsider={(event) => onconsider(column.stage.id, event)}
          onfinalize={(event) => onfinalize(column.stage, event)}
        >
          {#each column.cards as card (card.id)}
            <!-- svelte-ignore a11y_no_noninteractive_element_interactions: svelte-dnd-action makes each card focusable (tabindex 0) and draggable; these keys are its non-drag alternative -->
            <li
              data-card-id={card.id}
              aria-label={`${card.candidateName}, ${column.stage.name}`}
              class="flex flex-col gap-1.5 rounded-lg border border-border bg-card p-2.5 outline-offset-2"
              onkeydown={(event) => onkeydown(card, event)}
            >
              <span class="flex items-center gap-2">
                <span
                  aria-hidden="true"
                  class="grid size-6 shrink-0 place-items-center rounded-full bg-secondary text-caption font-bold text-secondary-foreground"
                  >{initials(card.candidateName)}</span
                >
                <a href={`/applications/${card.id}`} tabindex="-1" class="truncate font-bold text-foreground no-underline hover:underline"
                  >{card.candidateName}</a
                >
              </span>
              <span class="flex items-center justify-between gap-2">
                <span class="text-caption text-muted-foreground">{sourceLabel(card.source)} · <time datetime={card.submittedAt} title={formatDateTime(card.submittedAt)}>{timeAgo(card.submittedAt, now)}</time></span>
                <MoveToStage compact candidateName={card.candidateName} currentStageId={card.stage.id} {stages} onmove={(stage) => moves.move(card, stage)} />
              </span>
            </li>
          {/each}
        </ul>
      </section>
    {/each}
  </div>
</div>
