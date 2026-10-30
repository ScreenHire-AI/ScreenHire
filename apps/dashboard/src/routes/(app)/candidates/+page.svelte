<script lang="ts">
  import { createListApplications, createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
  import { can } from '#lib/permissions.js';
  import { matchesSearch } from '#lib/candidate-table.js';
  import { clock } from '#lib/clock.svelte.js';
  import ApplicationTable from '#lib/components/application-table.svelte';
  import EmailSelectionButton from '#lib/components/email-selection-button.svelte';
  import EmailComposerSheet from '#lib/components/email-composer-sheet.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import IntegrityFilterMenu from '#lib/components/integrity-filter.svelte';
  import MultiFilterMenu from '#lib/components/multi-filter-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import TagFilterMenu from '#lib/components/tag-filter.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { integrityColumns } from '#lib/integrity-columns.js';
  import { isIntegrityFiltered, matchesIntegrityFilter, noIntegrityFilter, type IntegrityFilter } from '#lib/integrity.js';
  import { IsMobile } from '#lib/hooks/is-mobile.svelte.js';
  import { describeError } from '#lib/query.js';
  import { findSavedView } from '#lib/saved-views.js';
  import { matchesTag, tagsOf } from '#lib/tags.js';
  import { jobOptions, stageOptions } from '#lib/candidate-filters.js';

  // Every application in the organization, newest first; new ones, stage moves and integrity levels arrive live
  // (#lib/realtime.ts).
  const applications = createListApplications();
  const view = $derived(findSavedView(page.url.searchParams.get('view')));
  const all = $derived(applications.data?.data ?? []);
  // The sidebar counts use the same clock, so a view and its count agree.
  let integrityFilter = $state<IntegrityFilter>({ ...noIntegrityFilter });
  let tagFilter = $state<string | null>(null);
  // Several jobs and several stages at once, such as Support Engineer and Senior .NET Engineer in Screening or Interview.
  // Stages are matched by name across jobs, and the Stage menu lists the stages of the jobs picked.
  let jobFilter = $state<string[]>([]);
  let stageFilter = $state<string[]>([]);
  let search = $state('');
  const jobs = $derived(jobOptions(all));
  const stages = $derived(stageOptions(all, jobFilter));
  // A stage picked before the jobs changed counts only while one of the picked jobs has it.
  const stagesPicked = $derived(stageFilter.filter((name) => stages.some((stage) => stage.value === name)));
  // Tags people and automations put on applications; tags and removals arrive live (ApplicationTagsChanged).
  const tags = $derived(tagsOf(all));
  const shown = $derived(
    all.filter(
      (application) =>
        (!view || view.matches(application, clock.now)) &&
        matchesIntegrityFilter(application, integrityFilter) &&
        matchesTag(application, tagFilter) &&
        (jobFilter.length === 0 || jobFilter.includes(application.jobId)) &&
        (stagesPicked.length === 0 || stagesPicked.includes(application.stage.name)) &&
        matchesSearch(application, search, true),
    ),
  );
  // The saved view is the page's address; the empty state's Clear filters resets only what this page set.
  const narrowed = $derived(
    isIntegrityFiltered(integrityFilter) || tagFilter !== null || jobFilter.length > 0 || stagesPicked.length > 0 || Boolean(search.trim()),
  );
  const filtering = $derived(Boolean(view) || narrowed);
  let activeId = $state<string | null>(null);
  let selectedIds = $state<string[]>([]);
  // The table has no footer (design system K1), so a pill in the panel header says how many of the shown rows are ticked.
  const selected = $derived(shown.filter((application) => selectedIds.includes(application.id)));
  const selectedCount = $derived(selected.length);

  // Email to a selection, where this deployment can send it and the person may (A3). Not while integrity picked the rows
  // (an integrity filter or view): a "not moving forward" email to a filtered "select all" would be a rejection driven
  // by integrity signals, as a bulk move to Rejected would (ADR-0004, bulkMoveStages). Not on the Overdue view either:
  // the team's delay never counts against a candidate (workflow-engine.md section 8). Every status view has a mark. One
  // candidate at a time, from their page, is still a person's decision.
  const emailTemplates = createListEmailTemplates();
  const statusPicked = $derived(isIntegrityFiltered(integrityFilter) || Boolean(view?.status));
  const canEmail = $derived(
    Boolean(emailTemplates.data?.data.sendingEnabled) && can(page.data.user, 'EmailCandidates') && !statusPicked,
  );
  let emailOpen = $state(false);
  let emailed = $state('');
  // Phones list the rows without checkboxes, so a selection only gets there from a wider window, and the header has no
  // room for the button beside the search and the pill.
  const phone = new IsMobile();
</script>

<svelte:head>
  <title>{view?.label ?? 'Candidates'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={view ? [{ label: 'Candidates', href: '/candidates' }, { label: view.label }] : [{ label: 'Candidates' }]} />

<!-- From a tablet up the table fills the window below the header and scrolls inside itself (S6). -->
<main class={['flex min-w-0 flex-col', !phone.current && 'h-[calc(100svh-3.5rem)] min-h-160']}>
  <HeaderBand
    title={view?.label ?? 'Candidates'}
    count={applications.isSuccess ? shown.length : undefined}
    countLabel={shown.length === 1 ? 'application' : 'applications'}
  />

  <div class={['@container', !phone.current && 'flex min-h-0 flex-1 flex-col']}>
    <div class={['flex w-full flex-col px-4 py-6 md:px-6', !phone.current && 'min-h-0 flex-1']}>
      <Panel title="Applications" flush fill={!phone.current} class={phone.current ? undefined : 'min-h-0 flex-1'}>
        {#snippet tools()}
          <SearchField bind:value={search} label="Search candidates" placeholder="Search" class="flex-[0_1_15rem] @max-3xl/panel:flex-1" />
          {#if selectedCount > 0}<span data-selected-count class="flex shrink-0 items-center gap-1.5 text-muted-foreground">Selected <Badge variant="count">{selectedCount}</Badge></span>{/if}
          {#if canEmail && selectedCount > 0 && !phone.current}
            <EmailSelectionButton count={selectedCount} onclick={() => (emailOpen = true)} />
          {/if}
        {/snippet}
        <!-- The filters: one row under the header, outside the table, that scrolls inside the panel rather than wrapping
             (F5), as on a job's pipeline. -->
        <div data-filters class="flex shrink-0 items-center gap-2 overflow-x-auto border-b border-border-subtle px-4.5 py-2 max-md:px-4">
          <MultiFilterMenu label="Job" options={jobs} bind:values={jobFilter} />
          <MultiFilterMenu label="Stage" options={stages} bind:values={stageFilter} />
          <IntegrityFilterMenu bind:filter={integrityFilter} />
          <TagFilterMenu bind:tag={tagFilter} {tags} />
        </div>
        {#if applications.isError}
          <div class="p-4">
            <QueryError message={describeError(applications.error, 'Could not load candidates')} onretry={() => applications.refetch()} inset />
          </div>
        {:else if applications.isPending}
          <div aria-hidden="true" class="flex flex-col gap-1 p-3">
            {#each [0, 1, 2, 3, 4] as row (row)}<Skeleton class="h-12 w-full max-md:h-24" />{/each}
          </div>
        {:else}
          <ApplicationTable
            applications={shown}
            fill={!phone.current}
            showJob
            sortId="candidates"
            extraColumns={integrityColumns}
            bind:activeId
            bind:selectedIds
            emptyText={filtering ? 'No candidates match this view.' : 'No candidates yet. Applications appear here as they arrive.'}
            onclearfilters={narrowed
              ? () => ((search = ''), (integrityFilter = { ...noIntegrityFilter }), (tagFilter = null), (jobFilter = []), (stageFilter = []))
              : undefined}
          />
        {/if}
        <p role="status" class="px-4.5 py-2 text-body text-muted-foreground empty:hidden max-md:px-4">{emailed}</p>
      </Panel>
    </div>
  </div>
</main>

{#if canEmail}
  <EmailComposerSheet bind:open={emailOpen} recipients={selected} onsent={(message) => ((emailed = message), (selectedIds = []))} />
{/if}
