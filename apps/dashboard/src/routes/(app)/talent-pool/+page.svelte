<script lang="ts">
  import BookmarkXIcon from '@lucide/svelte/icons/bookmark-x';
  import SendIcon from '@lucide/svelte/icons/send';
  import UserPlusIcon from '@lucide/svelte/icons/user-plus';
  import type { TalentPoolMember } from '@screenhire/api-client';
  import { createListEmailTemplates, createListTalentPool, createRemoveFromTalentPool } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { createSortedRowModel, createTable, rowSortingFeature, sortFn_alphanumeric, sortFn_basic, tableFeatures, type ColumnDef } from '@tanstack/svelte-table';
  import { page } from '$app/state';
  import AddToJobSheet from '#lib/components/add-to-job-sheet.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import InviteToApplySheet from '#lib/components/invite-to-apply-sheet.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import MultiFilterMenu from '#lib/components/multi-filter-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SearchField from '#lib/components/search-field.svelte';
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
  import { formatDate } from '#lib/dates.js';
  import { can } from '#lib/permissions.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath, talentPoolPath } from '#lib/realtime.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { tagsOf } from '#lib/tags.js';

  // The talent pool (docs/architecture/talent-pool.md) on the list template: the candidates the team kept in mind, with
  // the note saying why, their tags, the job they were kept from and when their permission runs out. From here the team
  // invites someone to apply for another job or adds them to one. No Verification column or filter: the pool never
  // becomes a list picked by level (ADR-0004). TalentPoolChanged refetches it live.
  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const pool = createListTalentPool();
  const people = $derived<TalentPoolMember[]>(pool.data?.data.people ?? []);
  const emailTemplates = createListEmailTemplates();
  const mayInvite = $derived(Boolean(emailTemplates.data?.data.sendingEnabled) && can(page.data.user, 'EmailCandidates'));
  const mayAdd = $derived(can(page.data.user, 'AddCandidates'));
  const mayRemove = $derived(can(page.data.user, 'MoveCandidates'));
  const remove = createRemoveFromTalentPool(() => ({
    mutation: { onSettled: () => Promise.all([talentPoolPath, applicationsPath].map((path) => invalidatePath(queryClient, path))) },
  }));

  let search = $state('');
  let tagFilter = $state<string[]>([]);
  let jobFilter = $state<string[]>([]);
  const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
  const tagOptions = $derived(
    tagsOf(people).map((tag) => ({ value: tag, label: tag, count: people.filter((person) => person.tags.some((each) => same(each, tag))).length })),
  );
  const jobOptions = $derived(
    [...new Map(people.map((person) => [person.jobId, person.jobTitle])).entries()]
      .map(([value, label]) => ({ value, label, count: people.filter((person) => person.jobId === value).length }))
      .toSorted((a, b) => a.label.localeCompare(b.label)),
  );
  const matches = (person: TalentPoolMember, text: string) => {
    const query = text.trim().toLowerCase();
    return !query || [person.candidateName, person.email, person.jobTitle, person.note ?? ''].some((value) => value.toLowerCase().includes(query));
  };
  const shown = $derived(
    people.filter(
      (person) =>
        matches(person, search) &&
        (tagFilter.length === 0 || tagFilter.some((tag) => person.tags.some((each) => same(each, tag)))) &&
        (jobFilter.length === 0 || jobFilter.includes(person.jobId)),
    ),
  );
  const filtering = $derived(Boolean(search.trim()) || tagFilter.length > 0 || jobFilter.length > 0);

  // The table sorts (L2) and opens on people by name; Permission ends shows who leaves the pool soonest.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, TalentPoolMember, any>[] = [
    { id: 'candidate', header: 'Candidate', accessorFn: (person) => person.candidateName, sortFn: 'alphanumeric' },
    { id: 'why', header: 'Why', accessorFn: (person) => person.note ?? '', sortFn: 'alphanumeric' },
    { id: 'tags', header: 'Tags', accessorFn: (person) => person.tags.join(', '), sortFn: 'alphanumeric' },
    { id: 'job', header: 'Kept from', accessorFn: (person) => person.jobTitle, sortFn: 'alphanumeric' },
    { id: 'kept', header: 'Kept', accessorFn: (person) => person.keptAt, sortFn: 'basic', sortDescFirst: true },
    { id: 'ends', header: 'Permission ends', accessorFn: (person) => person.permissionEndsAt, sortFn: 'basic' },
  ];
  const sort = createTableSort('talent-pool', { id: 'candidate', desc: false }, ['candidate', 'why', 'tags', 'job', 'kept', 'ends']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return shown;
    },
    getRowId: (person) => person.candidateId,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));
  const href = (person: TalentPoolMember) => `/applications/${person.applicationId}`;

  // A selection, for inviting or adding several at once. Only the rows on screen count.
  let selectedIds = $state<string[]>([]);
  const selected = $derived(shown.filter((person) => selectedIds.includes(person.candidateId)));
  const allSelected = $derived(shown.length > 0 && selected.length === shown.length);
  const toggle = (id: string) => (selectedIds = selectedIds.includes(id) ? selectedIds.filter((each) => each !== id) : [...selectedIds, id]);
  const toggleAll = () => (selectedIds = allSelected ? [] : shown.map((person) => person.candidateId));

  // A row's menu opens its sheet or dialog once the menu has closed, so focus can go back to the menu.
  let chosen = $state<{ person: TalentPoolMember; action: 'invite' | 'add' | 'remove' } | null>(null);
  let acting = $state<TalentPoolMember[]>([]);
  let inviteOpen = $state(false);
  let addOpen = $state(false);
  let removeOpen = $state(false);
  let removing = $state<TalentPoolMember | null>(null);
  let candidateAsked = $state(false);
  let announcement = $state('');
  function onMenuClosed() {
    if (chosen) open(chosen.action, [chosen.person]);
    chosen = null;
  }
  function open(action: 'invite' | 'add' | 'remove', who: TalentPoolMember[]) {
    acting = who;
    if (action === 'invite') inviteOpen = true;
    else if (action === 'add') addOpen = true;
    else {
      removing = who[0] ?? null;
      candidateAsked = false;
      removeOpen = true;
    }
  }
  function done(message: string) {
    announcement = message;
    selectedIds = [];
  }

  async function removePerson() {
    if (!removing) return;
    await remove.mutateAsync({ candidateId: removing.candidateId, data: { candidateAsked } });
    announcement = `${removing.candidateName} left the talent pool.`;
  }
</script>

<svelte:head>
  <title>Talent pool · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Talent pool' }]} />

<main class="flex min-w-0 flex-col">
  <HeaderBand title="Talent pool" count={pool.isSuccess ? shown.length : undefined} countLabel={shown.length === 1 ? 'person' : 'people'} />

  <div class="@container w-full px-4 py-6 md:px-6">
    <p role="status" class="sr-only">{announcement}</p>
    <Panel title="People" flush>
      {#snippet tools()}
        <SearchField bind:value={search} label="Search the talent pool" placeholder="Search" class="flex-[0_1_15rem] @max-3xl/panel:flex-1" />
      {/snippet}
      {#if jobOptions.length > 0}
        <div data-filters class="flex shrink-0 items-center gap-2 overflow-x-auto border-b border-border-subtle px-4.5 py-2 max-md:px-4">
          <MultiFilterMenu label="Kept from" options={jobOptions} bind:values={jobFilter} />
          {#if tagOptions.length > 0}<MultiFilterMenu label="Tag" options={tagOptions} bind:values={tagFilter} />{/if}
        </div>
      {/if}
      {#if selected.length > 0 && (mayInvite || mayAdd)}
        <div role="region" aria-label="Selected people" class="flex min-w-0 items-center gap-2 overflow-x-auto border-b border-border-subtle bg-row-selected px-4.5 py-2 max-md:px-4">
          <span class="shrink-0 font-semibold whitespace-nowrap text-accent-foreground">{selected.length} selected</span>
          {#if mayInvite}
            <Button variant="outline" size="lg" class="shrink-0" aria-label="Invite {selected.length} selected to apply" onclick={() => open('invite', selected)}>
              <SendIcon aria-hidden="true" />Invite to apply
            </Button>
          {/if}
          {#if mayAdd}
            <Button variant="outline" size="lg" class="shrink-0" aria-label="Add {selected.length} selected to a job" onclick={() => open('add', selected)}>
              <UserPlusIcon aria-hidden="true" />Add to a job
            </Button>
          {/if}
          <Button variant="outline" size="lg" class="ml-auto shrink-0" onclick={() => (selectedIds = [])}>Clear selection</Button>
        </div>
      {/if}

      {#if pool.isError}
        <div class="p-4"><QueryError message={describeError(pool.error, 'Could not load the talent pool')} onretry={() => pool.refetch()} inset /></div>
      {:else if pool.isPending}
        <div aria-hidden="true" class="flex flex-col gap-1 p-3">
          {#each [0, 1, 2, 3] as row (row)}<Skeleton class="h-12 w-full max-md:h-24" />{/each}
        </div>
        <p class="sr-only" role="status">Loading the talent pool…</p>
      {:else if shown.length === 0}
        <Empty.Root class="gap-3 p-6">
          <Empty.Description>
            {filtering
              ? 'Nobody in the talent pool matches these filters.'
              : 'Nobody has been kept in mind yet.'}
          </Empty.Description>
          {#if filtering}
            <Button variant="outline" size="lg" onclick={() => ((search = ''), (tagFilter = []), (jobFilter = []))}>Clear filters</Button>
          {/if}
        </Empty.Root>
      {:else if sidebar.isMobile}
        <div class="flex justify-end border-b border-border-subtle px-4 py-2"><SortMenu {table} /></div>
        <ul>
          {#each sorted as person (person.candidateId)}
            <ListItem href={href(person)} title={person.candidateName}>
              {#snippet facts()}
                <Badge variant="muted">{person.jobTitle}</Badge>
                {#each person.tags as tag (tag)}<Badge variant="muted">{tag}</Badge>{/each}
              {/snippet}
              {#snippet meta()}Until {formatDate(person.permissionEndsAt)}{/snippet}
            </ListItem>
          {/each}
        </ul>
      {:else}
        <div class="overflow-x-auto">
          <Table.Root aria-label="Talent pool">
            <Table.Header>
              {#each table.getHeaderGroups() as group (group.id)}
                <Table.Row class="bg-muted hover:bg-muted">
                  <Table.Head class="w-10 px-2">
                    <label class="inline-grid size-6 cursor-pointer place-items-center pointer-coarse:size-11">
                      <input type="checkbox" class="size-4 accent-primary" aria-label="Select everyone shown" checked={allSelected} onchange={toggleAll} />
                    </label>
                  </Table.Head>
                  {#each group.headers as header (header.id)}<SortHeader {header} />{/each}
                  <Table.Head class="w-12 pr-4.5"><span class="sr-only">Actions</span></Table.Head>
                </Table.Row>
              {/each}
            </Table.Header>
            <Table.Body>
              {#each sorted as person (person.candidateId)}
                <!-- The whole row is the link to the application they were kept from (design system section 9). -->
                <Table.Row class="relative h-12">
                  <Table.Cell class="relative z-10 w-10 px-2">
                    <label class="inline-grid size-6 cursor-pointer place-items-center pointer-coarse:size-11">
                      <input
                        type="checkbox"
                        class="size-4 accent-primary"
                        aria-label={`Select ${person.candidateName}`}
                        checked={selectedIds.includes(person.candidateId)}
                        onchange={() => toggle(person.candidateId)}
                      />
                    </label>
                  </Table.Cell>
                  <Table.Cell class="max-w-56">
                    <span class="flex min-w-0">
                      <TruncatedText text={person.candidateName} href={href(person)} class="font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline" />
                    </span>
                  </Table.Cell>
                  <Table.Cell class="w-full max-w-0 min-w-48">
                    <span class="flex min-w-0"><TruncatedText text={person.note ?? ''} /></span>
                    {#if !person.note}<span class="sr-only">No note</span>{/if}
                  </Table.Cell>
                  <Table.Cell class="max-w-40"><span class="flex min-w-0"><TruncatedText text={person.tags.join(', ')} /></span></Table.Cell>
                  <Table.Cell class="max-w-48"><span class="flex min-w-0"><TruncatedText text={person.jobTitle} /></span></Table.Cell>
                  <Table.Cell class="whitespace-nowrap text-muted-foreground"><span class="relative"><DateText at={person.keptAt} /></span></Table.Cell>
                  <Table.Cell class="whitespace-nowrap"><span class="relative"><DateText at={person.permissionEndsAt} /></span></Table.Cell>
                  <Table.Cell class="pr-4.5 text-right">
                    {#if mayInvite || mayAdd || mayRemove}<span class="relative">{@render personMenu(person)}</span>{/if}
                  </Table.Cell>
                </Table.Row>
              {/each}
            </Table.Body>
          </Table.Root>
        </div>
      {/if}
    </Panel>
  </div>
</main>

{#snippet personMenu(person: TalentPoolMember)}
  <MoreMenu label={`Actions for ${person.candidateName}`} size="icon" quiet onclosed={onMenuClosed}>
    {#if mayInvite}
      <DropdownMenu.Item onSelect={() => (chosen = { person, action: 'invite' })}><SendIcon aria-hidden="true" />Invite to apply</DropdownMenu.Item>
    {/if}
    {#if mayAdd}
      <DropdownMenu.Item onSelect={() => (chosen = { person, action: 'add' })}><UserPlusIcon aria-hidden="true" />Add to a job</DropdownMenu.Item>
    {/if}
    {#if mayRemove}
      <DropdownMenu.Item variant="destructive" onSelect={() => (chosen = { person, action: 'remove' })}>
        <BookmarkXIcon aria-hidden="true" />Remove from talent pool
      </DropdownMenu.Item>
    {/if}
  </MoreMenu>
{/snippet}

{#if mayInvite}<InviteToApplySheet bind:open={inviteOpen} people={acting} ondone={done} />{/if}
{#if mayAdd}<AddToJobSheet bind:open={addOpen} people={acting} ondone={done} />{/if}

<ConfirmDialog
  bind:open={removeOpen}
  title={`Remove ${removing?.candidateName ?? ''} from the talent pool?`}
  description="Their application stays as it is. You cannot keep them in mind again on it unless they still allow it."
  cancelLabel="Keep them"
  confirmLabel="Remove"
  failure="Could not remove them"
  onconfirm={removePerson}
>
  {#snippet icon()}<BookmarkXIcon aria-hidden="true" />{/snippet}
  <label class="flex items-center gap-2 pointer-coarse:min-h-11">
    <input type="checkbox" bind:checked={candidateAsked} class="size-4 shrink-0 accent-primary" />
    They asked to be removed: stop keeping their details
  </label>
</ConfirmDialog>
