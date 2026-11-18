<script lang="ts">
  import type { SavedViewSummary } from '@screenhire/api-client';
  import { createListApplications } from '@screenhire/api-client/svelte-query';
  import CheckIcon from '~icons/lucide/check';
  import PlusIcon from '~icons/lucide/plus';
  import { clock } from '#lib/clock.svelte.js';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { describeFilters, viewMatcher } from '#lib/view-filters.js';
  import { viewHref } from '#lib/sidebar-views.js';
  import type { ViewActions } from '#lib/views.svelte.js';

  // The views the team shared (docs/architecture/saved-views.md): a list to browse, preview and add from. Sharing a view
  // only offers it, so one is in a person's sidebar once they add it here. Preview opens the view's page without adding
  // it. A view shared since the person last opened this sheet carries a New mark; opening it counts as the visit, but the
  // marks stay until it closes. The count is the candidates the view shows now, read from the same list the pages filter
  // (design system I2: the sidebar's own rows carry no count).
  let {
    open = $bindable(false),
    views,
    seenAt,
    jobTitles,
    actions,
    onnavigate,
  }: {
    open?: boolean;
    /** Every view in the person's list; this sheet shows the shared ones that are a teammate's. */
    views: readonly SavedViewSummary[];
    /** When the person last opened this sheet (their own start before the first time). */
    seenAt: string;
    jobTitles: Readonly<Record<string, string>>;
    actions: ViewActions;
    /** A Preview was chosen: the phone's sidebar sheet closes behind it. */
    onnavigate?: () => void;
  } = $props();

  // Only while the sheet is open does it read every candidate, and the list is shared with the pages that read it.
  const applications = createListApplications(() => ({ query: { enabled: open, staleTime: 60_000 } }));

  const offered = $derived(
    views
      .filter((view) => view.shared && !view.mine)
      .toSorted((a, b) => Date.parse(b.sharedAt ?? b.updatedAt) - Date.parse(a.sharedAt ?? a.updatedAt)),
  );
  const rows = $derived(applications.data?.data);
  const stageNames = $derived(Object.fromEntries((rows ?? []).map((row) => [row.stage.id, row.stage.name])));

  // The date the person had when they opened the sheet: opening saves a new one, which must not clear the marks at once.
  let seenOnOpen = $state('');
  // The views already in the sidebar when it opened: one added during the visit keeps its mark until the sheet closes.
  let inSidebarOnOpen = $state(new Set<string>());
  let wasOpen = false;
  let failure = $state('');
  $effect(() => {
    if (open && !wasOpen) {
      seenOnOpen = seenAt;
      inSidebarOnOpen = new Set(views.filter((view) => view.inSidebar).map((view) => view.id));
      failure = '';
      void actions.markTeamViewsOpened();
    }
    wasOpen = open;
  });

  // A view already in the sidebar is not news: the migration kept every view shared before it in every sidebar, and those
  // would otherwise read New beside "In your sidebar" on the first visit.
  const isNew = (view: SavedViewSummary) =>
    !inSidebarOnOpen.has(view.id) && Boolean(view.sharedAt) && Date.parse(view.sharedAt!) > Date.parse(seenOnOpen);

  function countOf(view: SavedViewSummary): number | null {
    if (!rows) return null;
    const scoped = view.scope === 'Job' && view.jobId ? rows.filter((row) => row.jobId === view.jobId) : rows;
    return scoped.filter(viewMatcher(view.filters, view.scope, scoped, clock.now)).length;
  }

  const countText = (count: number) => `Shows ${count} ${count === 1 ? 'candidate' : 'candidates'} now`;

  async function change(work: Promise<string | null>) {
    failure = (await work) ?? '';
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full gap-0 overflow-y-auto data-[side=right]:sm:max-w-xl">
    <Sheet.Header class="border-b border-border-subtle">
      <Sheet.Title>Team views</Sheet.Title>
      <Sheet.Description>Views your teammates shared.</Sheet.Description>
    </Sheet.Header>

    <FailureText message={failure} class="px-4 pt-3" />
    {#if offered.length === 0}
      <Empty.Root>
        <Empty.Header><Empty.Description>Nobody has shared a view yet. A view someone shares with the team shows up here.</Empty.Description></Empty.Header>
      </Empty.Root>
    {:else}
      <ul class="flex flex-col">
        {#each offered as view (view.id)}
          {@const count = countOf(view)}
          <li class="flex flex-col gap-3 border-b border-border-subtle px-4 py-3 last:border-b-0">
            <div class="flex min-w-0 flex-col gap-1">
              <div class="flex min-w-0 items-start gap-2">
                <!-- The whole name wraps: a sheet row has room, and a truncated name could not be read here. -->
                <h3 class="min-w-0 font-semibold break-words">{view.name}</h3>
                {#if isNew(view)}<Badge variant="muted">New</Badge>{/if}
              </div>
              <p class="text-caption text-muted-foreground">Shared by {view.ownerName}</p>
              <p class="text-caption text-muted-foreground">{describeFilters(view.filters, { jobTitles, stageNames })}</p>
              <p class="text-caption text-muted-foreground">
                <!-- While the count loads the line keeps its height, so the row does not jump when it arrives. -->
                {#if count === null}<span class="sr-only">Counting candidates…</span><span aria-hidden="true">&nbsp;</span>{:else}{countText(count)}{/if}
              </p>
            </div>
            <div class="flex flex-wrap items-center gap-2">
              <Button variant="outline" size="sm" href={viewHref(view)} onclick={() => { open = false; onnavigate?.(); }}>
                Preview <span class="sr-only">{view.name}</span>
              </Button>
              {#if view.inSidebar}
                <span class="inline-flex items-center gap-1.5 text-caption text-muted-foreground"><CheckIcon aria-hidden="true" class="size-3.5" />In your sidebar</span>
                <Button variant="outline" size="sm" onclick={() => void change(actions.removeFromSidebar(view))}>
                  Remove <span class="sr-only">{view.name} from your sidebar</span>
                </Button>
              {:else}
                <!-- Outline like Preview: every row has an Add, and a sheet of rows has no one primary action (A1). -->
                <Button variant="outline" size="sm" onclick={() => void change(actions.addToSidebar(view))}>
                  <PlusIcon aria-hidden="true" />Add <span class="sr-only">{view.name} to your sidebar</span>
                </Button>
              {/if}
            </div>
          </li>
        {/each}
      </ul>
    {/if}
  </Sheet.Content>
</Sheet.Root>
