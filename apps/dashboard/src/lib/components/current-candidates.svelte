<script lang="ts">
  import UserPlusIcon from '~icons/lucide/user-plus';
  import XIcon from '~icons/lucide/x';
  import {
    createGetFunnelApplications,
    createListApplications,
    createListEmailTemplates,
    createListJobs,
    createListTalentPool,
    createListViews,
  } from '@screenhire/api-client/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { can } from '#lib/permissions.js';
  import { matchesSearch, sortableColumnIds } from '#lib/candidate-table.js';
  import { clock } from '#lib/clock.svelte.js';
  import ActionBar from '#lib/components/action-bar.svelte';
  import AddCandidateSheet, { type AddedCandidate } from '#lib/components/add-candidate-sheet.svelte';
  import ApplicationTable from '#lib/components/application-table.svelte';
  import CandidatesListSwitch from '#lib/components/candidates-list-switch.svelte';
  import EmailSelectionButton from '#lib/components/email-selection-button.svelte';
  import EmailComposerSheet from '#lib/components/email-composer-sheet.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import VerificationFilterMenu from '#lib/components/verification-filter.svelte';
  import MultiFilterMenu from '#lib/components/multi-filter-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SaveViewButton from '#lib/components/save-view-button.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import TagFilterMenu from '#lib/components/tag-filter.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { verificationColumns } from '#lib/verification-columns.js';
  import { isVerificationFiltered, matchesVerificationFilter, noVerificationFilter, type VerificationFilter } from '#lib/verification.js';
  import { IsMobile } from '#lib/hooks/is-mobile.svelte.js';
  import { describeError } from '#lib/query.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { findSavedView } from '#lib/saved-views.js';
  import { createTableSort } from '#lib/table-sort.svelte.js';
  import { matchesTag, tagsOf } from '#lib/tags.js';
  import { jobOptions, readReached, reachedLabel, stageOptions } from '#lib/candidate-filters.js';
  import { stageLabel } from '#lib/talent-pool.js';
  import { closedJobIds, isCurrentApplication, listCounts } from '#lib/candidate-lists.js';
  import { defaultSorts, differsFromView, filtersOf, sortOf, verificationOf } from '#lib/view-filters.js';

  // Every application in the organization, newest first; new ones, stage moves and verification levels arrive live
  // (#lib/realtime.ts).
  // Current (the default) or All; the Saved list is its own component.
  let { scope = 'current' }: { scope?: 'current' | 'all' } = $props();

  const applications = createListApplications();
  // Only for the Saved choice's count.
  const savedPeople = createListTalentPool();
  // Which jobs are closed, from the shell's job list (one query): their applications are not current.
  const jobList = createListJobs();
  const closedJobs = $derived(closedJobIds(jobList.data?.data));
  // The address names a view: a built-in one (code), or one that a person saved (the server's list, docs/architecture/saved-views.md).
  const viewId = $derived(page.url.searchParams.get('view'));
  const builtIn = $derived(findSavedView(viewId));
  const savedViews = createListViews();
  const saved = $derived(
    builtIn ? undefined : savedViews.data?.data.views.find((candidate) => candidate.id === viewId && candidate.scope === 'Candidates'),
  );
  // What the page shows as the view: its name, and for a built-in status view the status that marks it.
  const view = $derived(builtIn ?? (saved ? { label: saved.name, status: undefined } : undefined));
  // A view that is gone (deleted, or made private by someone else) shows every candidate and says so.
  const viewGone = $derived(Boolean(viewId) && !builtIn && !saved && savedViews.isSuccess);
  const viewLoading = $derived(Boolean(viewId) && !builtIn && !saved && savedViews.isPending);
  const all = $derived(applications.data?.data ?? []);
  // Current is the people in a hiring process now: on a job that is not closed, neither saved nor at an end stage. All is every
  // application, for finding someone whose process ended.
  // The sidebar counts use the same clock, so a view and its count agree.
  let verificationFilter = $state<VerificationFilter>({ ...noVerificationFilter });
  let tagFilter = $state<string | null>(null);
  // Several jobs and several stages at once, such as Support Engineer and Senior .NET Engineer in Screening or Interview.
  // Stages are matched by name across jobs, and the Stage menu lists the stages of the jobs picked.
  // A report's funnel link arrives with its jobs (`?job=`) and the stage its applications reached (`?reached=`, with the
  // dates they were submitted in); the ids come from the same query the report counts with, so the list matches its count.
  let jobFilter = $state<string[]>(page.url.searchParams.getAll('job'));
  const reached = $derived(readReached(page.url.searchParams));
  // A view keeps its own rules, and a report's funnel link shows everyone the report counted (hired and rejected
  // included), so neither is narrowed to Current.
  const pool = $derived(view || reached || scope === 'all' ? all : all.filter((application) => isCurrentApplication(application, closedJobs)));
  const reachedApplications = createGetFunnelApplications(
    () => reached?.kind ?? 'Applied',
    () => ({ from: reached?.from ?? '', to: reached?.to ?? '' }),
    // `notifyOnChangeProps: 'all'`: the ids are read only when the list is drawn, after the query settles, and query's
    // tracking of which fields were read can miss that read, which would leave the list empty.
    () => ({ query: { enabled: reached !== null, notifyOnChangeProps: 'all' } }),
  );
  const reachedIds = $derived.by(() => {
    const result = reachedApplications.data;
    return new Set(result?.status === 200 ? result.data : []);
  });
  function clearReached() {
    const url = new URL(page.url.href);
    for (const name of ['reached', 'from', 'to']) url.searchParams.delete(name);
    void goto(url, { replace: true, reset: false });
  }
  let stageFilter = $state<string[]>([]);
  let search = $state('');
  const jobs = $derived(jobOptions(pool));
  const stages = $derived(stageOptions(pool, jobFilter));
  // A stage picked before the jobs changed counts only while one of the picked jobs has it.
  const stagesPicked = $derived(stageFilter.filter((name) => stages.some((stage) => stage.value === name)));
  // Tags people and automations put on applications; tags and removals arrive live (ApplicationTagsChanged).
  const tags = $derived(tagsOf(pool));
  // A saved view narrows a built-in view when it was saved from one; the built-in view is code, so it is looked up by id.
  const base = $derived(builtIn ?? (saved?.filters.baseView ? findSavedView(saved.filters.baseView) : undefined));
  const shown = $derived(
    pool.filter(
      (application) =>
        (!base || base.matches(application, clock.now)) &&
        (!reached || reachedIds.has(application.id)) &&
        matchesVerificationFilter(application, verificationFilter) &&
        matchesTag(application, tagFilter) &&
        (jobFilter.length === 0 || jobFilter.includes(application.jobId)) &&
        (stagesPicked.length === 0 || stagesPicked.includes(stageLabel(application))) &&
        matchesSearch(application, search, true),
    ),
  );
  // The saved view is the page's address; the empty state's Clear filters resets only what this page set.
  const narrowed = $derived(
    reached !== null || isVerificationFiltered(verificationFilter) || tagFilter !== null || jobFilter.length > 0 || stagesPicked.length > 0 || Boolean(search.trim()),
  );
  const filtering = $derived(Boolean(view) || narrowed);

  // The table's sort belongs to the page, so a saved view can set it. A view sets it without changing the sort the person
  // remembers for this table; choosing a column afterwards does remember it.
  const sort = createTableSort('candidates', defaultSorts.Candidates, sortableColumnIds(verificationColumns, true));
  // Opening a saved view puts its filters and sort on the page; leaving one clears them again. `applied` is the view whose
  // filters are on the page, so the Save view button waits for them.
  let applied = $state<string | null>(null);
  $effect(() => {
    const id = saved?.id ?? null;
    if (id === applied) return;
    if (saved) {
      const filters = saved.filters;
      jobFilter = [...filters.jobIds];
      stageFilter = [...filters.stages];
      verificationFilter = verificationOf(filters);
      tagFilter = filters.tag ?? null;
      search = '';
      sort.show(sortOf(filters, 'Candidates'));
    } else if (applied !== null) {
      jobFilter = [];
      stageFilter = [];
      verificationFilter = { ...noVerificationFilter };
      tagFilter = null;
      sort.show(preferences.sortFor('candidates'));
    }
    applied = id;
  });
  const filtersNow = $derived(
    filtersOf({
      baseView: base?.id ?? null,
      jobIds: jobFilter,
      stages: stagesPicked,
      verification: verificationFilter,
      tag: tagFilter,
      sort: sort.sorting[0] ?? defaultSorts.Candidates,
    }),
  );
  // The Save view button: only once the page's filters or sort differ from the view it is on.
  const canSave = $derived(
    !viewLoading &&
      !viewGone &&
      (!saved || applied === saved.id) &&
      differsFromView(filtersNow, 'Candidates', saved ? { filters: saved.filters } : builtIn ? { baseView: builtIn.id } : null),
  );
  let activeId = $state<string | null>(null);
  let selectedIds = $state<string[]>([]);
  // The table has no footer (design system K1), so a pill in the panel header says how many of the shown rows are ticked.
  const selected = $derived(shown.filter((application) => selectedIds.includes(application.id)));
  const selectedCount = $derived(selected.length);

  // Email to a selection, where this deployment can send it and the person may (A3). Not while verification picked the rows
  // (a verification filter or view): a "not moving forward" email to a filtered "select all" would be a rejection driven
  // by verification signals, as a bulk move to Rejected would (ADR-0004, bulkMoveStages). Not on the Overdue view either:
  // the team's delay never counts against a candidate (workflow-engine.md section 8). Every status view has a mark. One
  // candidate at a time, from their page, is still a person's decision.
  const emailTemplates = createListEmailTemplates();
  const statusPicked = $derived(isVerificationFiltered(verificationFilter) || Boolean(base?.status));
  const canEmail = $derived(
    Boolean(emailTemplates.data?.data.sendingEnabled) && can(page.data.user, 'EmailCandidates') && !statusPicked,
  );
  let emailOpen = $state(false);
  let emailed = $state('');
  // Phones list the rows without checkboxes, so a selection only gets there from a wider window, and the header has no
  // room for the button beside the search and the pill.
  const phone = new IsMobile();

  // Add candidate (design system section 11): the page's one primary action, for a role that adds candidates (A3). The new
  // row arrives live (ApplicationSubmitted); the status line names them, with a link to their page.
  const canAdd = $derived(can(page.data.user, 'AddCandidates'));
  let adding = $state(false);
  let added = $state<AddedCandidate | null>(null);
</script>

<svelte:head>
  <title>{view?.label ?? 'Candidates'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={view ? [{ label: 'Candidates', href: '/candidates' }, { label: view.label }] : [{ label: 'Candidates' }]} />

<!-- From a tablet up the table fills the window below the header and scrolls inside itself (S6). -->
<main class={['flex min-w-0 flex-col', !phone.current && 'h-[calc(100svh-3.5rem)] min-h-160', phone.current && canAdd && 'pb-28']}>
  <HeaderBand
    title={view?.label ?? 'Candidates'}
    status={listSwitch}
    actions={canAdd && !phone.current ? addButton : undefined}
  />

  <div class={['@container', !phone.current && 'flex min-h-0 flex-1 flex-col']}>
    <div class={['flex w-full flex-col px-4 py-6 md:px-6', !phone.current && 'min-h-0 flex-1']}>
      <Panel title="Candidates" flush fill={!phone.current} class={phone.current ? undefined : 'min-h-0 flex-1'}>
        {#snippet tools()}
          <SearchField bind:value={search} label="Search candidates" placeholder="Search" class="flex-[0_1_15rem] @max-3xl/panel:flex-1" />
          {#if selectedCount > 0}<span data-selected-count class="flex shrink-0 items-center gap-1.5 text-muted-foreground">Selected <Badge variant="count">{selectedCount}</Badge></span>{/if}
          {#if canEmail && selectedCount > 0 && !phone.current}
            <EmailSelectionButton count={selectedCount} onclick={() => (emailOpen = true)} />
          {/if}
        {/snippet}
        <!-- The filters: one row under the header, outside the table, that scrolls inside the panel rather than wrapping
             (F5), as on a job's pipeline. Save view stays in view at the end. On a phone the table draws them in its own
             row, with the Sort chip at the end, so the filters and Sort share one row. -->
        {#if !phone.current}
          <div data-filters class="flex shrink-0 items-center gap-2 border-b border-border-subtle px-4.5 py-2">
            <div class="-my-1 flex min-w-0 flex-1 items-center gap-2 overflow-x-auto py-1 [scrollbar-width:none]">{@render filterMenus()}</div>
            {@render saveView()}
          </div>
        {/if}
        <p role="status" class="border-b border-border-subtle px-4.5 py-2 text-body text-muted-foreground empty:hidden max-md:px-4">{viewGone ? 'That view is no longer there, so this shows every candidate.' : ''}</p>
        {#if applications.isError}
          <div class="p-4">
            <QueryError message={describeError(applications.error, 'Could not load candidates')} onretry={() => applications.refetch()} inset />
          </div>
        {:else if reached && reachedApplications.isError}
          <div class="p-4">
            <QueryError message={describeError(reachedApplications.error, 'Could not load these candidates')} onretry={() => reachedApplications.refetch()} inset />
          </div>
        {:else if applications.isPending || viewLoading || (reached && reachedApplications.isPending)}
          <div aria-hidden="true" class="flex flex-col gap-1 p-3">
            {#each [0, 1, 2, 3, 4] as row (row)}<Skeleton class="h-12 w-full max-md:h-24" />{/each}
          </div>
        {:else}
          <ApplicationTable
            applications={shown}
            fill={!phone.current}
            showJob
            sortId="candidates"
            {sort}
            extraColumns={verificationColumns}
            filters={filterMenus}
            filterAction={saveView}
            bind:activeId
            bind:selectedIds
            emptyTitle={filtering ? undefined : 'No candidates yet'}
            emptyText={filtering ? 'No candidates match this view.' : 'Applications appear here as they arrive.'}
            onclearfilters={narrowed
              ? () => ((search = ''), (verificationFilter = { ...noVerificationFilter }), (tagFilter = null), (jobFilter = []), (stageFilter = []), clearReached())
              : undefined}
          />
        {/if}
        <p role="status" class="px-4.5 py-2 text-body text-muted-foreground empty:hidden max-md:px-4">{emailed}</p>
        <!-- Erasing a candidate returns here, so this is where it says it worked. -->
        <p role="status" class="px-4.5 py-2 text-body text-muted-foreground empty:hidden max-md:px-4" data-erased>{page.state.erased ? `Erased ${page.state.erased}.` : ''}</p>
        <p role="status" class="px-4.5 py-2 text-body text-muted-foreground empty:hidden max-md:px-4" data-added>
          {#if added}Added <a href={`/applications/${added.applicationId}`} class="text-foreground underline underline-offset-2">{added.name}</a> to {added.jobTitle}.{/if}
        </p>
      </Panel>
    </div>
  </div>

  {#if canAdd && phone.current}
    <ActionBar>{@render addButton()}</ActionBar>
  {/if}
</main>

{#snippet filterMenus()}
  {#if reached}
    <Button variant="outline" onclick={clearReached} aria-label={`Clear filter: ${reachedLabel(reached)}`}>
      {reachedLabel(reached)}<XIcon aria-hidden="true" />
    </Button>
  {/if}
  <MultiFilterMenu label="Job" options={jobs} bind:values={jobFilter} />
  <MultiFilterMenu label="Stage" options={stages} bind:values={stageFilter} />
  <VerificationFilterMenu bind:filter={verificationFilter} />
  <TagFilterMenu bind:tag={tagFilter} {tags} />
{/snippet}

{#snippet saveView()}
  <SaveViewButton scope="Candidates" filters={filtersNow} show={canSave} />
{/snippet}

{#snippet listSwitch()}
  <CandidatesListSwitch list={scope} counts={listCounts(applications.data?.data, savedPeople.data?.data.people.length, closedJobs)} />
{/snippet}

{#snippet addButton()}
  <Button size="lg" aria-haspopup="dialog" onclick={() => (adding = true)}><UserPlusIcon aria-hidden="true" />Add candidate</Button>
{/snippet}

{#if canAdd}
  <AddCandidateSheet bind:open={adding} onadded={(candidate) => (added = candidate)} />
{/if}

{#if canEmail}
  <EmailComposerSheet bind:open={emailOpen} recipients={selected} onsent={(message) => ((emailed = message), (selectedIds = []))} />
{/if}
