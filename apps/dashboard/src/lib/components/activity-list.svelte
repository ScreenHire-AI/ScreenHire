<script lang="ts" module>
  import type { Component } from 'svelte';

  export interface Activity {
    id: string;
    /** A Lucide icon for the kind of event. */
    icon: Component;
    /** What happened: "Moved to Screening". */
    event: string;
    /** Who did it, shown muted after the event. */
    actor?: string;
    /** A note left with the event, quoted below it. */
    note?: string;
    /** A note of the team's own (a NoteAdded entry): `body` draws it, `actions` its menu, and the item is its anchor. */
    noteId?: string;
    /** When, as an ISO instant. */
    at: string;
  }
</script>

<script lang="ts">
  import type { Snippet } from 'svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';

  // Events on a record, newest first (design system section 11): an icon in a circle, the event, who did it and time
  // ago. A team note draws its own text and menu through `body` and `actions`, and is the anchor `#note-{id}` that a
  // mention email links to. While `items` is undefined (still loading) it shows skeleton rows of the same shape (E1).
  let {
    items,
    label,
    body,
    actions,
  }: {
    items: readonly Activity[] | undefined;
    label: string;
    /** A team note's text, or its editor, in place of a quoted note. */
    body?: Snippet<[Activity]>;
    /** A team note's menu, on the right of its first line. */
    actions?: Snippet<[Activity]>;
  } = $props();
</script>

{#if items === undefined}
  <div aria-hidden="true" data-activity-skeleton class="flex flex-col gap-4">
    {#each [0, 1] as row (row)}
      <div class="flex gap-3">
        <Skeleton class="size-7 shrink-0 rounded-full" />
        <div class="flex min-w-0 flex-1 flex-col gap-1.5 pt-1">
          <Skeleton class="h-4 w-3/4" />
          <Skeleton class="h-3 w-16" />
        </div>
      </div>
    {/each}
  </div>
{:else}
  <ol aria-label={label} class="flex flex-col">
    {#each items as item (item.id)}
      <li
        id={item.noteId ? `note-${item.noteId}` : undefined}
        class="relative flex scroll-mt-24 gap-3 pb-4 last:pb-0 not-last:before:absolute not-last:before:top-8 not-last:before:bottom-1 not-last:before:left-3.5 not-last:before:w-px not-last:before:bg-border-subtle"
      >
        <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground">
          <item.icon />
        </span>
        <div class="flex min-w-0 flex-1 flex-col gap-0.5 pt-1">
          <div class="flex items-start gap-2">
            <p class="min-w-0 flex-1 break-words">
              <span class="font-semibold">{item.event}</span>
              {#if item.actor}<span class="text-muted-foreground">{item.actor}</span>{/if}
            </p>
            {#if item.noteId && actions}<div class="-mt-1.5 shrink-0">{@render actions(item)}</div>{/if}
          </div>
          {#if item.noteId && body}{@render body(item)}{:else if item.note}<p class="max-w-measure break-words">"{item.note}"</p>{/if}
          <RelativeTime at={item.at} class="text-caption text-muted-foreground" />
        </div>
      </li>
    {/each}
  </ol>
{/if}
