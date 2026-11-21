<script lang="ts">
  import type { HistoryEntry, TeamMember } from '@screenhire/api-client';
  import HistoryEntries from '#lib/components/history-entries.svelte';
  import * as Sheet from '#lib/components/ui/sheet/index.js';

  // Every change to a template or a job (design system T6, section 11): the list is `HistoryEntries`, newest first and
  // grouped by day. Plain data in, so a page passes its loaded pages and the style guide passes examples.
  let {
    open = $bindable(false),
    name,
    entries,
    error,
    onretry = () => {},
    hasMore = false,
    loadingMore = false,
    onmore,
    members,
    currentUserId,
    empty = 'No changes yet.',
    note,
  }: {
    open?: boolean;
    /** The object's name: the sheet's one line of description. */
    name: string;
    /** The loaded entries, newest first; undefined while the first page loads. */
    entries: readonly HistoryEntry[] | undefined;
    /** Why the history could not load; shown when nothing loaded. */
    error?: string;
    onretry?: () => void;
    /** Whether older changes are left to load. */
    hasMore?: boolean;
    loadingMore?: boolean;
    onmore?: () => void;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    /** The empty state's line. */
    empty?: string;
    /** One line under the list, about what a change here reaches. */
    note?: string;
  } = $props();
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="@container w-full gap-0 overflow-y-auto data-[side=right]:sm:max-w-xl">
    <Sheet.Header class="border-b border-border-subtle">
      <Sheet.Title>History</Sheet.Title>
      <Sheet.Description>{name}</Sheet.Description>
    </Sheet.Header>

    <HistoryEntries {entries} {error} {onretry} {hasMore} {loadingMore} {onmore} {members} {currentUserId} {empty} />

    {#if note}<p class="mt-auto border-t border-border-subtle px-4 py-3 text-caption text-muted-foreground">{note}</p>{/if}
  </Sheet.Content>
</Sheet.Root>
