<script lang="ts" module>
  import '#lib/dnd.js';
</script>

<script lang="ts" generics="T">
  import type { Snippet } from 'svelte';
  import type { ActionReturn } from 'svelte/action';
  import { flip } from 'svelte/animate';
  import { alertToScreenReader, dndzone, SHADOW_ITEM_MARKER_PROPERTY_NAME, SOURCES, TRIGGERS, type DndEvent, type Options } from 'svelte-dnd-action';
  import ReorderGrip from '#lib/components/reorder-grip.svelte';
  import { cn } from '#lib/utils.js';

  // A list people reorder (design system A7): each item renders its `grip`, which drags, opens the Move menu on click, and
  // picks the item up with Space for the arrow keys. svelte-dnd-action does the dragging; each list has its own drag
  // state and its own zone type, so lists on one page (questions, and the options inside a question) never trade items
  // or wake each other's grips. The list does not change `items` itself: it calls `onreorder` with the new order.
  let {
    items,
    key,
    label,
    itemLabel,
    onreorder,
    item,
    class: className,
  }: {
    items: readonly T[];
    key: (item: T) => string;
    /** The list's accessible name, also spoken when an item moves. */
    label: string;
    /** An item's name, for its grip ("Reorder Your CV") and what is spoken when it moves. */
    itemLabel: (item: T) => string;
    /** The new order. Set `items` to it: the list shows the move at once and keeps it until `items` changes. */
    onreorder: (next: T[]) => void;
    /** One item. Render `grip` where its handle goes. */
    item: Snippet<[value: T, grip: Snippet]>;
    class?: string;
  } = $props();

  type Entry = { id: string; value: T; [SHADOW_ITEM_MARKER_PROPERTY_NAME]?: boolean };

  // The zone's own copy, so a drag can rearrange it before the drop.
  let entries = $derived<Entry[]>(items.map((value) => ({ id: key(value), value })));
  const type = $props.id();
  // Items slide into place, unless the person asked for less motion (section 17).
  const flipDurationMs = globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches ? 0 : 150;
  let list = $state<HTMLOListElement | null>(null);

  // The drag starts only from a grip. A grip switches it on for the press under way, synchronously, so the same mousedown
  // or key press reaches the zone's freshly added listeners on its way up, the way svelte-dnd-action's own drag handles
  // do; but here the switch is this list's alone.
  let gripActive = false;
  let zone: { update: (options: Options<Entry>) => void; destroy: () => void } | undefined;
  let zoneOptions: Options<Entry>;

  // No drop-zone outline: the library's default is a yellow off the palette, and the moving item already shows where it is.
  const withGrips = (options: Options<Entry>): Options<Entry> => ({
    ...options,
    type,
    dragDisabled: !gripActive,
    zoneItemTabIndex: -1,
    dropTargetStyle: {},
  });

  type ZoneEvents = { onconsider: (event: CustomEvent<DndEvent<Entry>>) => void; onfinalize: (event: CustomEvent<DndEvent<Entry>>) => void };

  function reorderZone(node: HTMLElement, options: Options<Entry>): ActionReturn<Options<Entry>, ZoneEvents> {
    zoneOptions = options;
    zone = dndzone(node, withGrips(options)) as typeof zone;
    return {
      update(next: Options<Entry>) {
        zoneOptions = next;
        zone?.update(withGrips(next));
      },
      destroy() {
        zone?.destroy();
        zone = undefined;
      },
    };
  }

  function setGripActive(active: boolean) {
    if (gripActive === active) return;
    gripActive = active;
    zone?.update(withGrips(zoneOptions));
  }

  function activate(from: 'pointer' | 'keyboard') {
    setGripActive(true);
    // A press that never became a drag ends here; a drag ends in `onfinalize` or `onconsider`.
    if (from === 'pointer') {
      const release = () => {
        removeEventListener('mouseup', release);
        removeEventListener('touchend', release);
        setGripActive(false);
      };
      addEventListener('mouseup', release);
      addEventListener('touchend', release);
    }
  }

  const order = (list: readonly Entry[]) => list.map((entry) => entry.id).join('\n');

  function commit(next: Entry[]) {
    if (order(next) !== order(items.map((value) => ({ id: key(value), value })))) {
      onreorder(next.map((entry) => entry.value));
    }
  }

  function gripOf(id: string): HTMLElement | null {
    const row = [...(list?.children ?? [])].find((child) => (child as HTMLElement).dataset.reorderId === id);
    return row?.querySelector<HTMLElement>('[data-reorder-grip]') ?? null;
  }

  function onconsider(event: CustomEvent<DndEvent<Entry>>) {
    entries = event.detail.items;
    const { source, trigger, id } = event.detail.info;
    if (source === SOURCES.KEYBOARD && trigger === TRIGGERS.DRAG_STOPPED) {
      setGripActive(false);
      // The keyboard drag focused the item itself; the grip is where the person was.
      gripOf(id)?.focus();
    }
  }

  function onfinalize(event: CustomEvent<DndEvent<Entry>>) {
    entries = event.detail.items;
    if (event.detail.info.source === SOURCES.POINTER) setGripActive(false);
    commit(entries.filter((entry) => !entry[SHADOW_ITEM_MARKER_PROPERTY_NAME]));
  }

  function move(from: number, to: number) {
    const next = [...entries];
    const [moved] = next.splice(from, 1);
    if (!moved) return;
    next.splice(to, 0, moved);
    entries = next;
    commit(next);
    alertToScreenReader(`Moved item ${itemLabel(moved.value)} to position ${to + 1} in the list ${label}`);
  }
</script>

<ol
  bind:this={list}
  aria-label={label}
  class={cn('flex flex-col', className)}
  use:reorderZone={{ items: entries, flipDurationMs, transformDraggedElement: (element) => element?.classList.add('shadow-raised') }}
  {onconsider}
  {onfinalize}
>
  {#each entries as entry, index (entry.id)}
    <li
      data-reorder-id={entry.id}
      aria-label={itemLabel(entry.value)}
      class={['min-w-0 rounded-lg', entry[SHADOW_ITEM_MARKER_PROPERTY_NAME] && 'opacity-40']}
      animate:flip={{ duration: flipDurationMs }}
    >
      {#snippet grip()}
        {#if entries.length > 1}
          <ReorderGrip label={itemLabel(entry.value)} {index} count={entries.length} onactivate={activate} onmove={(to) => move(index, to)} />
        {:else}
          <!-- One item cannot move: no grip, but its place is kept so the item does not shift (A3). -->
          <span class="size-8 shrink-0 pointer-coarse:size-11"></span>
        {/if}
      {/snippet}
      {@render item(entry.value, grip)}
    </li>
  {/each}
</ol>
