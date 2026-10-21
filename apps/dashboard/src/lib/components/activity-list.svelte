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
    /** When, as an ISO instant. */
    at: string;
  }
</script>

<script lang="ts">
  import RelativeTime from '#lib/components/relative-time.svelte';

  // Events on a record, newest first (design system section 11): an icon in a circle, the event, and time ago.
  let { items, label }: { items: readonly Activity[]; label: string } = $props();
</script>

<ol aria-label={label} class="flex flex-col">
  {#each items as item (item.id)}
    <li
      class="relative flex gap-3 pb-4 last:pb-0 not-last:before:absolute not-last:before:top-8 not-last:before:bottom-1 not-last:before:left-3.5 not-last:before:w-px not-last:before:bg-border-subtle"
    >
      <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground">
        <item.icon />
      </span>
      <div class="flex min-w-0 flex-col gap-0.5 pt-1">
        <p class="break-words">
          <span class="font-semibold">{item.event}</span>
          {#if item.actor}<span class="text-muted-foreground">{item.actor}</span>{/if}
        </p>
        {#if item.note}<p class="break-words">"{item.note}"</p>{/if}
        <RelativeTime at={item.at} class="text-caption text-muted-foreground" />
      </div>
    </li>
  {/each}
</ol>
