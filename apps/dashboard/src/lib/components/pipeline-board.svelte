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
  import IntegrityChip from '#lib/components/integrity-chip.svelte';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import OverdueBadge from '#lib/components/overdue-badge.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { initials, sourceLabel } from '#lib/format.js';
  import { chipOf } from '#lib/integrity.js';
  import type { Moves } from '#lib/moves.svelte.js';
  import { hasModifier } from '#lib/shortcuts.js';

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

<!-- The board scrolls sideways inside its panel; each column is an inset (design system section 6). relative: the
     chips' sr-only labels are absolute, and without a positioned scroller they escape it and widen the page (F1). -->
<div class="relative overflow-x-auto">
  <div class="flex min-w-max gap-3 p-3">
    {#each columns as column (column.stage.id)}
      <section aria-labelledby={`stage-${column.stage.id}`} class="flex w-64 flex-col rounded-lg bg-secondary">
        <h3 id={`stage-${column.stage.id}`} class="flex items-center gap-2 px-3 pt-3 pb-2 text-body font-semibold">
          {column.stage.name}
          <Badge variant="count" class="bg-card">{column.cards.filter((card) => !card.isDndShadowItem).length}</Badge>
        </h3>
        <ul
          aria-label={`${column.stage.name} stage`}
          class="flex min-h-24 flex-1 flex-col gap-2 px-2 pb-2"
          use:dndzone={{
            items: column.cards,
            type: 'pipeline',
            flipDurationMs: 150,
            dropTargetClasses: ['outline-2', 'outline-ring/50'],
            // The card in hand is the raised layer (design system section 6).
            transformDraggedElement: (element) => element?.classList.add('shadow-raised'),
          }}
          onconsider={(event) => onconsider(column.stage.id, event)}
          onfinalize={(event) => onfinalize(column.stage, event)}
        >
          {#each column.cards as card (card.id)}
            {@const authenticity = chipOf(card.integrity, 'authenticity')}
            {@const ai = chipOf(card.integrity, 'aiAssistance')}
            <!-- svelte-ignore a11y_no_noninteractive_element_interactions: svelte-dnd-action makes each card focusable (tabindex 0) and draggable; these keys are its non-drag alternative -->
            <li
              data-card-id={card.id}
              aria-label={`${card.candidateName}, ${column.stage.name}`}
              class="flex flex-col gap-2 rounded-lg border border-border-subtle bg-card p-3 outline-offset-2"
              onkeydown={(event) => onkeydown(card, event)}
            >
              <span class="flex min-w-0 items-center gap-2">
                <span
                  aria-hidden="true"
                  class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-caption font-bold text-secondary-foreground"
                  >{initials(card.candidateName)}</span
                >
                <TruncatedText
                  href={`/applications/${card.id}`}
                  tabindex={-1}
                  text={card.candidateName}
                  class="font-semibold text-foreground no-underline hover:underline"
                />
              </span>
              <!-- Status first, calm states quiet (T2, T3); the column already says the stage. Overdue is the team's
                   status and needs attention, so it leads. -->
              <span class="flex flex-wrap items-center gap-1.5">
                <OverdueBadge dueAt={card.slaDueAt} />
                <IntegrityChip
                  kind="authenticity"
                  compact
                  level={authenticity?.level ?? null}
                  reasons={authenticity?.reasons ?? 0}
                  dismissed={authenticity?.dismissed ?? false}
                />
                {#if ai && ai.level.rank > 0}
                  <IntegrityChip kind="aiAssistance" compact level={ai.level} reasons={ai.reasons} dismissed={ai.dismissed} />
                {/if}
              </span>
              <!-- Wraps by content in a narrow column (F2); Move keeps to the right edge. -->
              <span class="flex flex-wrap items-center gap-2">
                <Badge variant="muted">{sourceLabel(card.source)}</Badge>
                <span class="shrink-0 text-caption whitespace-nowrap text-muted-foreground"><RelativeTime at={card.submittedAt} /></span>
                <span class="ml-auto shrink-0">
                  <MoveToStage compact candidateName={card.candidateName} currentStageId={card.stage.id} {stages} onmove={(stage) => moves.move(card, stage)} />
                </span>
              </span>
            </li>
          {/each}
        </ul>
      </section>
    {/each}
  </div>
</div>
