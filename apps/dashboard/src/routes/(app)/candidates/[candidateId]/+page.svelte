<script lang="ts">
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import MailIcon from '~icons/lucide/mail';
  import PhoneIcon from '~icons/lucide/phone';
  import Trash2Icon from '~icons/lucide/trash-2';
  import {
    ApiError,
    createGetCandidate,
    createGetCandidateHistory,
    createListJobs,
    createListTeamMembers,
  } from '@screenhire/api-client/svelte-query';
  import type { ApplicationSummary } from '@screenhire/api-client';
  import { createSortedRowModel, createTable, rowSortingFeature, sortFn_alphanumeric, sortFn_basic, tableFeatures, type ColumnDef } from '@tanstack/svelte-table';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActivityList, { type Activity } from '#lib/components/activity-list.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import EraseCandidateDialog from '#lib/components/erase-candidate-dialog.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import NoteText from '#lib/components/note-text.svelte';
  import Notice from '#lib/components/notice.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { askedToBeRemovedAt, historyItems, progressOf } from '#lib/candidate-profile.js';
  import { formatDate } from '#lib/dates.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { can, seesAllJobs } from '#lib/permissions.js';
  import { describeError } from '#lib/query.js';
  import type { PageProps } from './$types';

  let { params }: PageProps = $props();

  // A candidate's profile (design system section 7, a detail page): the header band holds who they are, then every
  // application they made in the order they made it and their history across all of them. A person who applies to
  // several jobs, perhaps a year apart, is one candidate (CLAUDE.md: matched by email within the organization). The API
  // lists only the jobs the reader sees (ADR-0015). Erasing is here, in the More menu, for people who may.
  const candidate = createGetCandidate(() => params.candidateId);
  const history = createGetCandidateHistory(() => params.candidateId);
  const team = createListTeamMembers();
  const jobs = createListJobs();
  const detail = $derived(candidate.data?.data);
  // The job is named on History's lines only when the candidate has more than one application, so a lone application's job
  // is not repeated on every line (W7).
  const severalJobs = $derived((detail?.applications.length ?? 0) > 1);
  const currentUserId = $derived(page.data.user?.id);
  const jobName = (id: string | null | undefined) => jobs.data?.data.find((job) => job.id === id)?.title ?? 'another job';
  const items = $derived(history.data ? historyItems(history.data.data, team.data?.data, currentUserId, jobName, severalJobs) : undefined);
  const asked = $derived(askedToBeRemovedAt(history.data?.data));
  // The API needs the permission and a person who sees every job, because erasing reaches every application.
  const mayErase = $derived(can(page.data.user, 'EraseCandidates') && seesAllJobs(page.data.user));
  const notFound = $derived(candidate.error instanceof ApiError && candidate.error.status === 404);

  // The applications are a table (L1, L2): one row each, with the dates in their own columns. It opens oldest first, the
  // order they were made in; a column header sorts it. A phone gets list items with a Sort chip.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  const progress = (application: ApplicationSummary) => progressOf(application, history.data?.data);
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, ApplicationSummary, any>[] = [
    { id: 'job', header: 'Job', accessorFn: (application) => application.jobTitle, sortFn: 'alphanumeric' },
    { id: 'stage', header: 'Stage', accessorFn: (application) => progress(application).outcome, sortFn: 'alphanumeric' },
    { id: 'applied', header: 'Applied', accessorFn: (application) => Date.parse(application.submittedAt), sortFn: 'basic' },
    { id: 'reached', header: 'Got as far as', accessorFn: (application) => progress(application).reached ?? '', sortFn: 'alphanumeric' },
    { id: 'ended', header: 'Ended', accessorFn: (application) => { const ended = progress(application).endedAt; return ended ? Date.parse(ended) : undefined; }, sortFn: 'basic', sortDescFirst: true, sortUndefined: 'last' },
  ];
  const sort = createTableSort('candidate-applications', { id: 'applied', desc: false }, ['job', 'stage', 'applied', 'reached', 'ended']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return detail?.applications ?? [];
    },
    getRowId: (application) => application.id,
  });
  const sortedApplications = $derived(table.getRowModel().rows.map((row) => row.original));
  const sidebar = Sidebar.useSidebar();

  let eraseOpen = $state(false);
  // The dialog opens once the More menu has closed, so focus can go back to the menu's button.
  let eraseChosen = false;
</script>

<svelte:head>
  <title>{detail?.fullName ?? 'Candidate'} · ScreenHire</title>
</svelte:head>

{#snippet noteBody(item: Activity)}
  <NoteText text={item.note ?? ''} members={team.data?.data} />
{/snippet}

<TopBar crumbs={[{ label: 'Candidates', href: '/candidates' }, { label: detail?.fullName ?? 'Candidate' }]} back />

<main class="flex flex-col">
  {#if candidate.isError}
    {#if notFound}
      <HeaderBand title="Candidate not found" />
      <div class="@container w-full px-4 py-5 md:px-6">
        <Panel title="Details">
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>They do not exist, or they belong to another organization.</Empty.Description>
            </Empty.Header>
            <Empty.Content><Button href="/candidates" variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to candidates</Button></Empty.Content>
          </Empty.Root>
        </Panel>
      </div>
    {:else}
      <HeaderBand title="Candidate" />
      <div class="@container w-full px-4 py-5 md:px-6">
        <Panel title="Could not load the candidate"><QueryError message={describeError(candidate.error, 'Could not load this candidate')} onretry={() => candidate.refetch()} inset /></Panel>
      </div>
    {/if}
  {:else if !detail}
    <div aria-hidden="true" class="@container border-b border-border bg-card">
      <div class="flex w-full flex-col gap-4 px-4 py-5 md:px-6">
        <Skeleton class="h-8 w-64 max-w-full" />
        <Skeleton class="h-5 w-96 max-w-full" />
      </div>
    </div>
    <div aria-hidden="true" class="@container grid w-full items-start gap-5 px-4 py-5 md:px-6 @5xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
      <Skeleton class="h-64 rounded-panel" />
      <Skeleton class="h-64 rounded-panel" />
    </div>
    <p class="sr-only" role="status">Loading the candidate…</p>
  {:else}
    <HeaderBand title={detail.fullName}>
      {#snippet details()}
        <TruncatedText text={detail.email} href={`mailto:${detail.email}`} icon={MailIcon} underlined class="pointer-coarse:-my-3.5 pointer-coarse:py-3.5" />
        {#if detail.phone}
          <TruncatedText text={detail.phone} href={`tel:${detail.phone}`} icon={PhoneIcon} underlined class="pointer-coarse:-my-3.5 pointer-coarse:py-3.5" />
        {/if}
      {/snippet}
      {#snippet actions()}
        {#if mayErase}
          <MoreMenu label="More actions" onclosed={() => eraseChosen && ((eraseChosen = false), (eraseOpen = true))}>
            <DropdownMenu.Item variant="destructive" onSelect={() => (eraseChosen = true)}><Trash2Icon aria-hidden="true" />Erase candidate</DropdownMenu.Item>
          </MoreMenu>
        {/if}
      {/snippet}
    </HeaderBand>

    <div class="@container flex w-full flex-col gap-5 px-4 py-5 md:px-6 md:py-6">
      {#if asked && mayErase}
        <!-- They used the stop link in an invitation from Saved candidates. Erasing their data as well is a separate,
             deliberate step (A2), named here because it is what they may be asking for. -->
        <Notice>
          They asked to be removed from Saved candidates on {formatDate(asked)}. To erase their data as well, use Erase candidate in the More menu.
        </Notice>
      {/if}

      <div class="grid items-start gap-5 @5xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <Panel title="Applications" count={detail.applications.length} flush>
          {#if sidebar.isMobile}
            <div class="flex justify-end border-b border-border-subtle px-4 py-2"><SortMenu {table} /></div>
            <ul aria-label="Applications">
              {#each sortedApplications as application (application.id)}
                {@const how = progress(application)}
                <ListItem href={`/applications/${application.id}`} title={application.jobTitle}>
                  {#snippet meta()}<DateText at={application.submittedAt} />{/snippet}
                  {#snippet facts()}
                    <Badge variant="muted">{how.outcome}</Badge>
                    {#if how.reached}<span class="text-muted-foreground">Got as far as {how.reached}</span>{/if}
                  {/snippet}
                </ListItem>
              {/each}
            </ul>
          {:else}
            <div class="overflow-x-auto">
              <Table.Root aria-label="Applications">
                <Table.Header>
                  {#each table.getHeaderGroups() as group (group.id)}
                    <Table.Row class="bg-muted hover:bg-muted">
                      {#each group.headers as header (header.id)}<SortHeader {header} class="px-3 first:pl-4.5" />{/each}
                    </Table.Row>
                  {/each}
                </Table.Header>
                <Table.Body>
                  {#each sortedApplications as application (application.id)}
                    {@const how = progress(application)}
                    <!-- The whole row opens the application (design system section 9). -->
                    <Table.Row class="group relative h-12 hover:bg-muted">
                      <Table.Cell class="max-w-64 pl-4.5">
                        <span class="flex min-w-0"><TruncatedText text={application.jobTitle} href={`/applications/${application.id}`} class="font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline" /></span>
                      </Table.Cell>
                      <Table.Cell class="whitespace-nowrap">{how.outcome}</Table.Cell>
                      <Table.Cell class="whitespace-nowrap text-muted-foreground"><span class="relative"><DateText at={application.submittedAt} /></span></Table.Cell>
                      <Table.Cell class="max-w-48"><span class="flex min-w-0"><TruncatedText text={how.reached ?? ''} /></span></Table.Cell>
                      <Table.Cell class="whitespace-nowrap text-muted-foreground"><span class="relative">{#if how.endedAt}<DateText at={how.endedAt} />{/if}</span></Table.Cell>
                    </Table.Row>
                  {/each}
                </Table.Body>
              </Table.Root>
            </div>
          {/if}
        </Panel>

        <Panel title="History" count={items?.length}>
          {#if history.isError && !items}
            <QueryError message={describeError(history.error, 'Could not load the history')} onretry={() => history.refetch()} inset />
          {:else if items && items.length === 0}
            <p class="text-muted-foreground">Nothing has happened yet.</p>
          {:else}
            <ActivityList label="History across all applications" {items} body={noteBody} />
          {/if}
        </Panel>
      </div>
    </div>

    <EraseCandidateDialog bind:open={eraseOpen} candidateId={detail.id} candidateName={detail.fullName} ondone={() => goto('/candidates')} />
  {/if}
</main>
