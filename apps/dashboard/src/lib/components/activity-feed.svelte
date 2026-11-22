<script lang="ts">
  import BriefcaseIcon from '~icons/lucide/briefcase';
  import type { ActivityScope, TeamMember } from '@screenhire/api-client';
  import MultiFilterMenu from '#lib/components/multi-filter-menu.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
  import { eventIcons, feedLine } from '#lib/activity.js';
  import { feedKinds, unseenText, type ActivityFeed } from '#lib/activity-feed.svelte.js';

  // The workspace's Activity, the bell's second tab (design system section 11, Notification bell): what changed across the
  // jobs the person may see, newest first. Each row says who did what, with the record as a link to exactly where the change
  // is, the job on a second line and how long ago, which ticks every minute. "Since you were last here" counts the changes
  // that came after the person last opened this tab, other than their own, and a line marks where those end. The filters,
  // My jobs or Everything I can see and the kinds, stay as the person leaves them in this browser.
  let {
    feed,
    members,
    currentUserId,
    since,
    onnavigate,
  }: {
    feed: ActivityFeed;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    /** When the person last looked and how many changes came since, as they were when this tab opened. */
    since: { at: string; count: number } | undefined;
    /** Called when a link is followed, so the panel closes. */
    onnavigate: () => void;
  } = $props();

  const scopeLabels: Record<ActivityScope, string> = { MyJobs: 'My jobs', Everything: 'Everything I can see' };
  const entries = $derived(feed.entries);
  // The first entry the person had already seen, where the "earlier" line goes: only when something came after it.
  const firstSeen = $derived.by(() => {
    if (!since || since.count === 0 || !entries) return undefined;
    const at = Date.parse(since.at);
    const index = entries.findIndex((entry) => Date.parse(entry.occurredAt) <= at);
    return index > 0 ? entries[index]?.id : undefined;
  });
</script>

<div class="flex flex-col">
  <div class="flex flex-wrap items-center gap-2 border-b border-border-subtle px-4 py-3">
    <ToggleGroup.Root
      type="single"
      variant="outline"
      size="lg"
      spacing={0}
      aria-label="Whose jobs"
      bind:value={() => feed.scope ?? '', (next) => next && feed.setScope(next as ActivityScope)}
      class="max-w-full"
    >
      {#each Object.entries(scopeLabels) as [value, label] (value)}
        <ToggleGroup.Item {value} class="text-foreground data-[state=on]:inset-ring-2 data-[state=on]:inset-ring-accent-foreground">{label}</ToggleGroup.Item>
      {/each}
    </ToggleGroup.Root>
    <MultiFilterMenu label="Kinds" options={feedKinds} bind:values={() => [...feed.kinds], (next) => feed.setKinds(next)} />
  </div>

  {#if since && since.count > 0}
    <p class="border-b border-border-subtle px-4 py-2.5 text-body">
      Since you were last here: {unseenText(since.count)}
      {since.count === 1 ? 'change' : 'changes'}
    </p>
  {/if}

  {#if feed.error}
    <div class="p-4"><QueryError message={feed.error} onretry={() => feed.refetch()} inset /></div>
  {:else if entries === undefined}
    <div class="flex flex-col gap-3 p-4" aria-busy="true">
      <Skeleton class="h-5 w-4/5" />
      <Skeleton class="h-5 w-3/5" />
    </div>
  {:else if entries.length === 0}
    <p class="px-4 py-6 text-body text-muted-foreground">
      {feed.kinds.length > 0 ? 'Nothing of these kinds yet.' : feed.scope === 'MyJobs' ? 'Nothing has happened on your jobs yet.' : 'Nothing has happened yet.'}
    </p>
  {:else}
    <ol aria-label="Activity" class="flex flex-col">
      {#each entries as entry (entry.id)}
        {@const line = feedLine(entry, members, currentUserId)}
        {@const Icon = entry.event ? eventIcons[entry.event.type] : BriefcaseIcon}
        {#if entry.id === firstSeen}
          <li class="border-b border-border-subtle bg-muted px-4 py-1.5 text-caption text-muted-foreground">Seen before</li>
        {/if}
        <li class="flex min-w-0 gap-3 border-b border-border-subtle px-4 py-2.5 last:border-b-0">
          <span aria-hidden="true" class="mt-0.5 grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground"><Icon /></span>
          <div class="flex min-w-0 flex-1 flex-col gap-0.5">
            <p class="min-w-0 break-words">
              {line.before}<a href={line.href} onclick={onnavigate} class="font-semibold text-foreground underline underline-offset-2 pointer-coarse:py-3">{line.link}</a
              >{line.after}{#if line.by}{' '}<span class="text-muted-foreground">{line.by}</span>{/if}
            </p>
            <p class="flex min-w-0 flex-wrap gap-x-2 text-caption text-muted-foreground">
              {#if entry.applicationId}<span class="min-w-0 break-words">{entry.jobTitle}</span>{/if}
              <RelativeTime at={entry.occurredAt} class="shrink-0" />
            </p>
          </div>
        </li>
      {/each}
    </ol>
    {#if feed.hasMore}
      <div class="border-t border-border-subtle px-4 py-3">
        <Button variant="outline" class="w-full" onclick={() => feed.more()} disabled={feed.loadingMore}>Show more</Button>
      </div>
    {/if}
  {/if}
</div>
