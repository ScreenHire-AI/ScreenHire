<script lang="ts">
  import BriefcaseIcon from '~icons/lucide/briefcase';
  import CalendarCheckIcon from '~icons/lucide/calendar-check';
  import CalendarPlusIcon from '~icons/lucide/calendar-plus';
  import InboxIcon from '~icons/lucide/inbox';
  import type { CurrentUser, JobSummary } from '@screenhire/api-client';
  import { createGetBilling, createListApplications } from '@screenhire/api-client/svelte-query';
  import OverviewStat from '#lib/components/overview-stat.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { openJobsText } from '#lib/billing.js';
  import { clock } from '#lib/clock.svelte.js';
  import { can } from '#lib/permissions.js';
  import { findSavedView } from '#lib/saved-views.js';

  // The four numbers above the table on Jobs (design system section 11, Overview stat): new applications this week, those
  // not yet screened, interviews this week and open jobs. Each opens the list it counts, and counts with the list's own
  // predicate (`saved-views.ts`), so a tile and its list agree. Open jobs reads "3 of 5" against the plan's limit for whoever
  // may manage billing, the only people the plan is shown to. A number that cannot load leaves its tile out, never a zero.
  let { jobs, user }: { jobs: readonly JobSummary[] | null | undefined; user: CurrentUser | null | undefined } = $props();

  const applications = createListApplications(() => ({ query: { staleTime: 60_000 } }));
  // The shell asks the same question for the Billing entry, so this reads its cache.
  const billing = createGetBilling(() => ({ query: { enabled: can(user, 'ManageBilling'), staleTime: 60_000 } }));

  const rows = $derived(applications.data?.data);
  const countOf = (viewId: string) => {
    const view = findSavedView(viewId);
    return rows && view ? rows.filter((application) => view.matches(application, clock.now)).length : undefined;
  };

  const openJobs = $derived(jobs?.filter((job) => job.status === 'Open').length);
  const openJobsValue = $derived.by(() => {
    if (openJobs === undefined) return undefined;
    const plan = billing.data?.data;
    return plan?.available && plan.activeJobLimit !== null ? openJobsText({ openJobs, activeJobLimit: plan.activeJobLimit }) : `${openJobs}`;
  });

  const applicationTiles = [
    { view: 'new-this-week', icon: CalendarPlusIcon },
    { view: 'not-screened', icon: InboxIcon },
    { view: 'interviews-this-week', icon: CalendarCheckIcon },
  ] as const;
</script>

<div class="@container">
  <div role="group" aria-label="Overview" class="grid grid-cols-2 gap-5 @3xl:grid-cols-4">
    {#if applications.isPending}
      {#each applicationTiles as tile (tile.view)}
        <Skeleton class="h-28 rounded-panel" />
      {/each}
    {:else}
      {#each applicationTiles as tile (tile.view)}
        {@const value = countOf(tile.view)}
        {#if value !== undefined}
          <OverviewStat icon={tile.icon} title={findSavedView(tile.view)?.label ?? ''} value={`${value}`} href={`/candidates?view=${tile.view}`} />
        {/if}
      {/each}
    {/if}
    {#if openJobsValue !== undefined}
      <OverviewStat icon={BriefcaseIcon} title="Open jobs" value={openJobsValue} href="/?status=Open" />
    {:else if jobs === undefined}
      <Skeleton class="h-28 rounded-panel" />
    {/if}
  </div>
</div>
