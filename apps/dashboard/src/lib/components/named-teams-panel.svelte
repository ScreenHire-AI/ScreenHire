<script lang="ts">
  import PencilIcon from '~icons/lucide/pencil';
  import PlusIcon from '~icons/lucide/plus';
  import Trash2Icon from '~icons/lucide/trash-2';
  import UsersRoundIcon from '~icons/lucide/users-round';
  import type { TeamDetail } from '@screenhire/api-client';
  import { createDeleteTeam } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import {
    createSortedRowModel,
    createTable,
    rowSortingFeature,
    sortFn_alphanumeric,
    sortFn_basic,
    tableFeatures,
    type ColumnDef,
  } from '@tanstack/svelte-table';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { peopleWords } from '#lib/hiring-team.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { rowLink } from '#lib/row-link.js';
  import { teamPath, teamsPath } from '#lib/settings.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';

  // Teams on Settings, Team (sign-in-and-tenancy.md, "Job access"): the organization's named teams on the list template, by
  // name, each with its people and when it last changed (T6). A team's name opens it in the team sheet, as Edit team in its
  // menu does; Delete team confirms (A2) and says what it leaves alone, the jobs the team was added to. The tab holds the
  // count, so the panel has none. Past about ten teams the panel has a search by name or person (S9).
  let {
    teams,
    pending,
    error,
    onretry,
    onedit,
    oncreate,
    onannounce,
  }: {
    teams: readonly TeamDetail[];
    pending: boolean;
    error?: string;
    onretry: () => void;
    /** Opens the team sheet on a team. */
    onedit: (team: TeamDetail) => void;
    /** Opens the team sheet on a new team. */
    oncreate: () => void;
    /** Says what was done, for the page's screen-reader line. */
    onannounce: (message: string) => void;
  } = $props();

  const queryClient = useQueryClient();
  const remove = createDeleteTeam(() => ({
    mutation: { onSettled: () => Promise.all([invalidatePath(queryClient, teamsPath), invalidatePath(queryClient, teamPath)]) },
  }));

  const names = (team: TeamDetail) => team.members.map((person) => person.displayName).join(', ');
  // A team nobody has changed since it was made reads as made then.
  const changedAt = (team: TeamDetail) => team.updatedAt ?? team.createdAt;
  let search = $state('');
  const visible = $derived.by(() => {
    const words = search.trim().toLowerCase();
    return words ? teams.filter((team) => [team.name, names(team)].some((text) => text.toLowerCase().includes(words))) : teams;
  });
  const features = tableFeatures({
    rowSortingFeature,
    sortedRowModel: createSortedRowModel(),
    sortFns: { alphanumeric: sortFn_alphanumeric, basic: sortFn_basic },
  });
  // `any`: one list mixes columns of different value types. People sorts by how many people a team has (L2).
  const columns: ColumnDef<typeof features, TeamDetail, any>[] = [
    { id: 'name', header: 'Name', accessorFn: (team) => team.name, sortFn: 'alphanumeric' },
    { id: 'people', header: 'People', accessorFn: (team) => team.members.length, sortFn: 'basic', sortDescFirst: true },
    { id: 'updated', header: 'Last updated', accessorFn: changedAt, sortFn: 'basic', sortDescFirst: true },
  ];
  const sort = createTableSort('team-teams', { id: 'name', desc: false }, ['name', 'people', 'updated']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return visible as TeamDetail[];
    },
    getRowId: (team) => team.id,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));

  // The menu opens the dialog once it has closed, so focus comes back to the menu.
  let deleting = $state<TeamDetail | null>(null);
  let deleteOpen = $state(false);
  let deleteChosen = false;
  let editChosen: TeamDetail | null = null;

  function onMenuClosed() {
    if (deleteChosen) deleteOpen = true;
    if (editChosen) onedit(editChosen);
    deleteChosen = false;
    editChosen = null;
  }

  async function deleteTeam() {
    if (!deleting) return;
    await remove.mutateAsync({ teamId: deleting.id });
    onannounce(`${deleting.name} was deleted.`);
  }
</script>

<Panel title="Teams" flush>
  {#snippet tools()}
    {#if teams.length > 10}<SearchField bind:value={search} label="Search teams" placeholder="Search" class="ml-auto flex-[0_1_15rem] max-md:ml-0 max-md:flex-1" />{/if}
  {/snippet}
  {#if error}
    <div class="p-4"><QueryError message={error} {onretry} inset /></div>
  {:else if pending}
    <div aria-hidden="true" class="flex flex-col">
      {#each [0, 1] as row (row)}
        <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
          <Skeleton class="h-4 w-40" />
          <Skeleton class="h-4 min-w-0 flex-1" />
        </div>
      {/each}
    </div>
    <p class="sr-only" role="status">Loading the teams…</p>
  {:else if teams.length === 0}
    <Empty.Root>
      <Empty.Header>
        <Empty.Media variant="icon"><UsersRoundIcon /></Empty.Media>
        <Empty.Title>No teams yet</Empty.Title>
        <Empty.Description>A team puts the same people on a job's hiring team in one step, such as Engineering or Kitchen.</Empty.Description>
      </Empty.Header>
      <Empty.Content><Button variant="outline" size="lg" onclick={oncreate}><PlusIcon aria-hidden="true" />New team</Button></Empty.Content>
    </Empty.Root>
  {:else}
    <div class="flex justify-end border-b border-border-subtle px-4 py-2 md:hidden"><SortMenu {table} /></div>
    {#if visible.length === 0}
      <p class="border-b border-border-subtle px-4.5 py-3 text-muted-foreground max-md:px-4">No team matches the search.</p>
    {/if}
    <ul class="md:hidden">
      {#each sorted as team (team.id)}
        <li class="flex min-w-0 items-start gap-2 border-b border-border-subtle px-4 py-3 last:border-0">
          <div class="flex min-w-0 flex-1 flex-col gap-1.5">
            <button type="button" class="min-w-0 text-left font-semibold break-words underline-offset-2 hover:underline pointer-coarse:min-h-11" onclick={() => onedit(team)}>{team.name}</button>
            <span class="text-muted-foreground">{peopleWords(team.members.length)}{#if team.members.length > 0}: {names(team)}{/if}</span>
            <span class="text-muted-foreground">Updated <DateText at={changedAt(team)} /></span>
          </div>
          {@render teamMenu(team)}
        </li>
      {/each}
    </ul>
    <div class="overflow-x-auto max-md:hidden">
      <Table.Root>
        <Table.Header>
          {#each table.getHeaderGroups() as group (group.id)}
            <Table.Row class="bg-muted hover:bg-muted">
              {#each group.headers as header (header.id)}
                <SortHeader {header} class={header.id === 'name' ? 'px-4.5' : undefined} />
              {/each}
              <Table.Head class="w-12 pr-4.5"><span class="sr-only">Actions</span></Table.Head>
            </Table.Row>
          {/each}
        </Table.Header>
        <Table.Body>
          {#each sorted as team (team.id)}
            <!-- A click anywhere on the row opens the team (L5): its name is the button. -->
            <Table.Row {@attach rowLink} class="h-12 hover:bg-muted">
              <Table.Cell class="max-w-64 px-4.5">
                <span class="flex min-w-0"
                  ><TruncatedText text={team.name} data-row-link class="font-semibold underline-offset-2 hover:underline pointer-coarse:min-h-11" onclick={() => onedit(team)} /></span
                >
              </Table.Cell>
              <Table.Cell class="w-full max-w-0 min-w-48 text-muted-foreground">
                {#if team.members.length === 0}<span class="sr-only">Nobody yet</span>{:else}<TruncatedText text={names(team)} />{/if}
              </Table.Cell>
              <Table.Cell class="whitespace-nowrap text-muted-foreground"><DateText at={changedAt(team)} /></Table.Cell>
              <Table.Cell class="pr-4.5 text-right">{@render teamMenu(team)}</Table.Cell>
            </Table.Row>
          {/each}
        </Table.Body>
      </Table.Root>
    </div>
  {/if}
</Panel>

{#snippet teamMenu(team: TeamDetail)}
  <MoreMenu label={`Actions for ${team.name}`} size="icon" quiet onclosed={onMenuClosed}>
    <DropdownMenu.Item onSelect={() => (editChosen = team)}><PencilIcon aria-hidden="true" />Edit team</DropdownMenu.Item>
    <DropdownMenu.Separator />
    <DropdownMenu.Item
      variant="destructive"
      onSelect={() => {
        deleting = team;
        deleteChosen = true;
      }}><Trash2Icon aria-hidden="true" />Delete team</DropdownMenu.Item
    >
  </MoreMenu>
{/snippet}

<ConfirmDialog
  bind:open={deleteOpen}
  title={`Delete ${deleting?.name ?? 'this team'}?`}
  description="Jobs it was added to keep their people. You can make the team again later."
  confirmLabel="Delete team"
  failure="Could not delete the team"
  onconfirm={deleteTeam}
>
  {#snippet icon()}<Trash2Icon aria-hidden="true" />{/snippet}
</ConfirmDialog>
