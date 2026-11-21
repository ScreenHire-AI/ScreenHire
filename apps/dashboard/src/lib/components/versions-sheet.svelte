<script lang="ts" module>
  /** One version of a hiring template, as the Versions tab lists it. */
  export interface VersionRow {
    id: string;
    version: number;
    status: 'Draft' | 'Published';
    publishedAt?: string | null;
  }

  export type VersionsTab = 'versions' | 'changes';
</script>

<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import type { HistoryEntry, TeamMember } from '@screenhire/api-client';
  import HistoryEntries from '#lib/components/history-entries.svelte';
  import RecordTabs from '#lib/components/record-tabs.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { formatDate } from '#lib/dates.js';

  // A hiring template's versions and its history in one sheet (design system section 11, Versions sheet): Versions lists
  // every version, newest first, each a link to it (A5) with its status and when it was published, the live one and the
  // one shown marked in words; All changes is the History list. The header's Versions button opens the first tab and
  // Last updated's History the second.
  let {
    open = $bindable(false),
    tab = $bindable('versions'),
    name,
    versions,
    shown,
    live,
    hrefOf,
    entries,
    error,
    onretry,
    hasMore,
    loadingMore,
    onmore,
    members,
    currentUserId,
    note,
    changes = true,
  }: {
    open?: boolean;
    tab?: VersionsTab;
    /** The template's name: the sheet's one line of description. */
    name: string;
    versions: readonly VersionRow[];
    /** The version the page shows. */
    shown: number | undefined;
    /** The newest published version, the one new jobs get. */
    live: number | null | undefined;
    /** The page that shows a version. */
    hrefOf: (version: number) => string;
    /** The history, as `HistoryEntries` takes it. */
    entries: readonly HistoryEntry[] | undefined;
    error?: string;
    onretry?: () => void;
    hasMore?: boolean;
    loadingMore?: boolean;
    onmore?: () => void;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    /** One line under All changes, about what a change here reaches. */
    note?: string;
    /** Whether it has All changes: a ScreenHire template has no history, because nobody can change it. */
    changes?: boolean;
  } = $props();

  const tabs = $derived([
    { value: 'versions', label: 'Versions' },
    ...(changes ? [{ value: 'changes', label: 'All changes' }] : []),
  ]);
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="@container w-full gap-0 overflow-y-auto data-[side=right]:sm:max-w-xl">
    <Sheet.Header class="border-b border-border-subtle">
      <Sheet.Title>{changes ? 'Versions and changes' : 'Versions'}</Sheet.Title>
      <Sheet.Description>{name}</Sheet.Description>
    </Sheet.Header>

    <Tabs.Root bind:value={() => tab, (value) => (tab = value as VersionsTab)} class="flex min-h-0 flex-1 flex-col gap-0">
      {#if changes}<div class="border-b border-border-subtle px-4"><RecordTabs label="Versions and changes" {tabs} /></div>{/if}
      <Tabs.Content value="versions">
        <ol aria-label="Versions" class="flex flex-col">
          {#each versions.toReversed() as row (row.id)}
            <li class="border-b border-border-subtle last:border-b-0">
              <a
                href={hrefOf(row.version)}
                aria-current={row.version === shown ? 'page' : undefined}
                onclick={() => (open = false)}
                class="flex items-center gap-3 px-4 py-3 hover:bg-muted focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring pointer-coarse:min-h-11"
              >
                <span class="flex min-w-0 flex-1 flex-col gap-0.5">
                  <span class="font-semibold tabular-nums">Version {row.version}{#if row.version === live}{' '}<span class="font-normal text-muted-foreground">(live)</span>{/if}</span>
                  {#if row.status === 'Published' && row.publishedAt}
                    <span class="text-caption text-muted-foreground">Published {formatDate(row.publishedAt)}</span>
                  {/if}
                </span>
                <StatusBadge status={row.status === 'Draft' ? 'draft' : 'published'} label={row.status === 'Draft' ? 'Draft' : 'Published'} quiet />
                {#if row.version === shown}<CheckIcon aria-hidden="true" class="shrink-0" /><span class="sr-only">(shown)</span>{/if}
              </a>
            </li>
          {/each}
        </ol>
      </Tabs.Content>
      {#if changes}
        <Tabs.Content value="changes" class="flex min-h-0 flex-1 flex-col">
          <HistoryEntries {entries} {error} {onretry} {hasMore} {loadingMore} {onmore} {members} {currentUserId} />
          {#if note}<p class="mt-auto border-t border-border-subtle px-4 py-3 text-caption text-muted-foreground">{note}</p>{/if}
        </Tabs.Content>
      {/if}
    </Tabs.Root>
  </Sheet.Content>
</Sheet.Root>
