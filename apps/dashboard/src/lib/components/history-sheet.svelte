<script lang="ts">
  import KeyRoundIcon from '~icons/lucide/key-round';
  import type { HistoryDetail, HistoryEntry, TeamMember } from '@screenhire/api-client';
  import { SvelteSet } from 'svelte/reactivity';
  import LogoMark from '#lib/components/logo-mark.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { clock } from '#lib/clock.svelte.js';
  import { formatDateTime } from '#lib/dates.js';
  import { initials } from '#lib/format.js';
  import { actorName, groupByDay, sentenceStart } from '#lib/history.js';

  // Every change to a template or a job (design system T6, section 11): newest first and grouped by day, who and when,
  // each change in words, with its values before and after on request. Plain data in, so a page passes its loaded pages
  // and the style guide passes examples.
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

  const days = $derived(entries ? groupByDay(entries, clock.now) : undefined);
  const shown = new SvelteSet<string>();

  const hasDetails = (entry: HistoryEntry) => entry.lines.some((line) => line.details.length > 0);

  function toggle(id: string) {
    if (shown.has(id)) shown.delete(id);
    else shown.add(id);
  }

  const show = (detail: HistoryDetail, value: string) => (detail.kind === 'Instant' ? formatDateTime(value) : value);
</script>

{#snippet value(detail: HistoryDetail)}
  {@const paragraphs = detail.kind === 'Paragraphs' ? 'whitespace-pre-line' : ''}
  {#if detail.before !== null && detail.before === detail.after}
    <span class={paragraphs}>{show(detail, detail.before)}</span> <span class="text-muted-foreground">Unchanged</span>
  {:else if detail.before === null && detail.after !== null}
    <span class={['font-semibold', paragraphs]}>{show(detail, detail.after)}</span>
  {:else if detail.after === null && detail.before !== null}
    <span class="sr-only">Removed: </span><s class={['text-muted-foreground', paragraphs]}>{show(detail, detail.before)}</s>
  {:else if detail.before !== null && detail.after !== null}
    <s class={['text-muted-foreground', paragraphs]}>{show(detail, detail.before)}</s>
    to <span class={['font-semibold', paragraphs]}>{show(detail, detail.after)}</span>
  {/if}
{/snippet}

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="@container w-full gap-0 overflow-y-auto data-[side=right]:sm:max-w-xl">
    <Sheet.Header class="border-b border-border-subtle">
      <Sheet.Title>History</Sheet.Title>
      <Sheet.Description>{name}</Sheet.Description>
    </Sheet.Header>

    {#if error && !entries}
      <div class="p-4"><QueryError message={error} {onretry} /></div>
    {:else if !days}
      <div aria-hidden="true" data-history-skeleton class="flex flex-col gap-4 p-4">
        {#each [0, 1, 2] as row (row)}
          <div class="flex gap-3">
            <Skeleton class="size-7 shrink-0 rounded-full" />
            <div class="flex min-w-0 flex-1 flex-col gap-1.5 pt-1"><Skeleton class="h-4 w-3/4" /><Skeleton class="h-3 w-24" /></div>
          </div>
        {/each}
      </div>
      <p class="sr-only" role="status">Loading the history…</p>
    {:else if days.length === 0}
      <Empty.Root>
        <Empty.Header><Empty.Description>{empty}</Empty.Description></Empty.Header>
      </Empty.Root>
    {:else}
      {#each days as group (group.key)}
        <section aria-labelledby={`history-day-${group.key}`} class="border-border-subtle not-first:border-t">
          <h3 id={`history-day-${group.key}`} class="border-b border-border-subtle bg-sidebar px-4 py-2 text-caption font-semibold text-muted-foreground">
            {group.label}
          </h3>
          <ol class="flex flex-col">
            {#each group.entries as entry (entry.id)}
              {@const who = actorName(entry, members, currentUserId) ?? 'someone'}
              {@const detailed = entry.lines.filter((line) => line.details.length > 0)}
              <li class="flex gap-3 border-b border-border-subtle px-4 py-3 last:border-b-0">
                {#if entry.by === 'ScreenHire'}
                  <LogoMark />
                {:else}
                  <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-caption font-semibold text-muted-foreground">
                    {#if entry.by === 'ApiKey'}<KeyRoundIcon />{:else}{initials(who === 'you' ? 'You' : who)}{/if}
                  </span>
                {/if}
                <div class="flex min-w-0 flex-1 flex-col gap-1">
                  <!-- In a narrow sheet the time goes under the change, so the words keep the width (F2). -->
                  <div class="flex flex-col gap-0.5 @sm:flex-row @sm:items-baseline @sm:gap-3">
                    <div class="flex min-w-0 flex-1 flex-col gap-0.5 break-words">
                      {#each entry.lines as line, index (index)}
                        <p>
                          {#if index === 0}<span class="font-semibold">{sentenceStart(who)}</span>{' '}{/if}{#each line.parts as part, at (at)}{#if part.strong}<span
                                class="font-semibold">{part.text}</span
                              >{:else}{part.text}{/if}{/each}
                        </p>
                      {/each}
                    </div>
                    <RelativeTime at={entry.at} class="shrink-0 text-caption text-muted-foreground" />
                  </div>
                  {#if hasDetails(entry)}
                    {#if shown.has(entry.id)}
                      <div id={`change-${entry.id}`} class="mt-1 flex flex-col gap-2 rounded-lg border border-border-subtle bg-sidebar p-3 text-caption">
                        {#each detailed as line, index (index)}
                          {#if detailed.length > 1}<p class="font-semibold">{sentenceStart(line.text)}</p>{/if}
                          <dl class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1">
                            {#each line.details as detail, at (at)}
                              <dt class="text-muted-foreground">{detail.label}</dt>
                              <dd class="min-w-0 break-words">{@render value(detail)}</dd>
                            {/each}
                          </dl>
                        {/each}
                      </div>
                    {/if}
                    <Button
                      variant="outline"
                      size="xs"
                      class="mt-1 self-start"
                      aria-expanded={shown.has(entry.id)}
                      aria-controls={shown.has(entry.id) ? `change-${entry.id}` : undefined}
                      onclick={() => toggle(entry.id)}>{shown.has(entry.id) ? 'Hide the change' : 'Show the change'}</Button
                    >
                  {/if}
                </div>
              </li>
            {/each}
          </ol>
        </section>
      {/each}
      {#if hasMore}
        <div class="p-4">
          <Button variant="outline" size="sm" disabled={loadingMore} onclick={onmore}>Show older changes</Button>
        </div>
      {/if}
    {/if}

    {#if note}<p class="mt-auto border-t border-border-subtle px-4 py-3 text-caption text-muted-foreground">{note}</p>{/if}
  </Sheet.Content>
</Sheet.Root>
