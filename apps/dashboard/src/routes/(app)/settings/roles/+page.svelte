<script lang="ts">
  import CopyIcon from '@lucide/svelte/icons/copy';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import Trash2Icon from '@lucide/svelte/icons/trash-2';
  import type { RoleSummary } from '@screenhire/api-client';
  import { createDeleteRole, createListRoles } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { createSortedRowModel, createTable, rowSortingFeature, sortFn_alphanumeric, sortFn_basic, tableFeatures, type ColumnDef } from '@tanstack/svelte-table';
  import ActionBar from '#lib/components/action-bar.svelte';
  import BuiltInBadge from '#lib/components/built-in-badge.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import NewRoleSheet from '#lib/components/new-role-sheet.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { mayAssign, roleKey } from '#lib/settings.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import type { PageProps } from './$types';

  // The organization's roles (design system section 21, ADR-0014) on the list template: the four ScreenHire supplies,
  // marked Built in, then the ones the team made, which nothing marks (W6). Each row opens the role's page. New role
  // copies a role in a sheet and opens the copy; Compare roles is the table of what each role can do.
  let { data }: PageProps = $props();

  const rolesPath = '/v1/organization/roles';
  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const catalogue = createListRoles();
  const remove = createDeleteRole(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, rolesPath) } }));
  const roles = $derived<RoleSummary[]>(catalogue.data?.data.roles ?? []);
  const actor = $derived(data.user?.organization?.permissions ?? []);
  const href = (role: RoleSummary) => `/settings/roles/${roleKey(role)}`;
  const builtIn = (role: RoleSummary) => role.role !== 'Custom';
  // Copy role is offered where New role would offer it: never Owner, and only a role the person could give.
  const copyable = (role: RoleSummary) => role.role !== 'Owner' && mayAssign(actor, role.permissions);
  const deletable = (role: RoleSummary) => !builtIn(role) && mayAssign(actor, role.permissions);

  // The table sorts (L2) and opens in its natural order, built-in roles by access and then the team's by name.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, RoleSummary, any>[] = [
    { id: 'role', header: 'Role', accessorFn: (role) => roles.indexOf(role), sortFn: 'basic', sortDescFirst: false },
    { id: 'can', header: 'What it can do', accessorFn: (role) => role.description, sortFn: 'alphanumeric' },
    { id: 'people', header: 'People', accessorFn: (role) => role.people, sortFn: 'basic', sortDescFirst: true },
    { id: 'updated', header: 'Last updated', accessorFn: (role) => role.lastUpdated.at ?? '', sortFn: 'basic', sortDescFirst: true },
  ];
  // No default sort: the natural order shows with no arrow until someone sorts (L2).
  const sort = createTableSort('roles', undefined, ['role', 'can', 'people', 'updated']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return roles;
    },
    getRowId: (role) => roleKey(role),
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));

  let creating = $state(false);
  let from = $state('viewer');
  function newRole(source = 'viewer') {
    from = source;
    creating = true;
  }

  // Copy and Delete open their sheet or dialog once the row's menu has closed, so focus can go back to the menu.
  let chosen = $state<{ role: RoleSummary; action: 'copy' | 'delete' } | null>(null);
  let deleting = $state<RoleSummary | null>(null);
  let deleteOpen = $state(false);
  function onMenuClosed() {
    if (chosen?.action === 'copy') newRole(roleKey(chosen.role));
    else if (chosen?.action === 'delete') {
      deleting = chosen.role;
      deleteOpen = true;
    }
    chosen = null;
  }
  let announcement = $state('');

  async function deleteRole() {
    if (!deleting?.customRoleId) return;
    await remove.mutateAsync({ roleId: deleting.customRoleId });
    announcement = `The ${deleting.name} role was deleted.`;
  }
</script>

<svelte:head>
  <title>Roles · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Roles' }]} />

<main class={[sidebar.isMobile && 'pb-28']}>
  <HeaderBand
    title="Roles"
    count={catalogue.isSuccess ? roles.length : undefined}
    countLabel={roles.length === 1 ? 'role' : 'roles'}
    actions={sidebar.isMobile ? compareLink : bandActions}
  >
    {#snippet details()}<span>Four ready-made roles work with no setup; make your own by copying one.</span>{/snippet}
  </HeaderBand>

  <SettingsMenu>
    <p role="status" class="sr-only">{announcement}</p>
    {#if catalogue.isError}
      <Panel title="Roles">
        <QueryError message={describeError(catalogue.error, 'Could not load the roles')} onretry={() => catalogue.refetch()} inset />
      </Panel>
    {:else}
      <Panel title="Roles" flush>
        {#if catalogue.isPending}
          <div aria-hidden="true" class="flex flex-col">
            {#each [0, 1, 2, 3] as row (row)}
              <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
                <Skeleton class="h-4 w-32" />
                <Skeleton class="h-4 min-w-0 flex-1" />
                <Skeleton class="h-4 w-16" />
              </div>
            {/each}
          </div>
          <p class="sr-only" role="status">Loading the roles…</p>
        {:else}
          <div class="flex justify-end border-b border-border-subtle px-4 py-2 md:hidden"><SortMenu {table} /></div>
          <ul class="md:hidden">
            {#each sorted as role (roleKey(role))}
              <ListItem href={href(role)} title={role.name}>
                {#snippet facts()}
                  {#if builtIn(role)}<BuiltInBadge />{/if}
                  <Badge variant="muted">{role.people === 1 ? '1 person' : `${role.people} people`}</Badge>
                {/snippet}
              </ListItem>
            {/each}
          </ul>
          <div class="overflow-x-auto max-md:hidden">
            <Table.Root>
              <Table.Header>
                {#each table.getHeaderGroups() as group (group.id)}
                  <Table.Row class="bg-muted hover:bg-muted">
                    {#each group.headers as header (header.id)}
                      <SortHeader {header} class={header.id === 'role' ? 'px-4.5' : undefined} />
                    {/each}
                    <Table.Head class="w-12 pr-4.5"><span class="sr-only">Actions</span></Table.Head>
                  </Table.Row>
                {/each}
              </Table.Header>
              <Table.Body>
                {#each sorted as role (roleKey(role))}
                  <!-- The whole row is the link (design system section 9): the role's name covers it. -->
                  <Table.Row class="relative h-12">
                    <Table.Cell class="max-w-64 px-4.5">
                      <span class="flex min-w-0 items-center gap-2">
                        <TruncatedText text={role.name} href={href(role)} class="font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline" />
                        {#if builtIn(role)}<BuiltInBadge class="shrink-0" />{/if}
                      </span>
                    </Table.Cell>
                    <Table.Cell class="w-full max-w-0 text-muted-foreground"><span class="flex min-w-0"><TruncatedText text={role.description} /></span></Table.Cell>
                    <Table.Cell class="tabular-nums">{role.people}</Table.Cell>
                    <Table.Cell class="whitespace-nowrap text-muted-foreground">
                      {#if role.lastUpdated.at}<span class="relative"><DateText at={role.lastUpdated.at} /></span>{:else}By ScreenHire{/if}
                    </Table.Cell>
                    <Table.Cell class="pr-4.5 text-right">
                      {#if copyable(role) || deletable(role)}<span class="relative">{@render roleMenu(role)}</span>{/if}
                    </Table.Cell>
                  </Table.Row>
                {/each}
              </Table.Body>
            </Table.Root>
          </div>
        {/if}
      </Panel>
    {/if}
  </SettingsMenu>

  {#if sidebar.isMobile}
    <ActionBar>{@render newRoleButton()}</ActionBar>
  {/if}
</main>

{#snippet compareLink()}
  <Button href="/settings/roles/compare" variant="outline" size="lg">Compare roles</Button>
{/snippet}

{#snippet newRoleButton()}
  <Button size="lg" onclick={() => newRole()}><PlusIcon aria-hidden="true" />New role</Button>
{/snippet}

{#snippet bandActions()}
  {@render compareLink()}
  {@render newRoleButton()}
{/snippet}

{#snippet roleMenu(role: RoleSummary)}
  <MoreMenu label={`Actions for ${role.name}`} size="icon" quiet onclosed={onMenuClosed}>
    {#if copyable(role)}
      <DropdownMenu.Item onSelect={() => (chosen = { role, action: 'copy' })}><CopyIcon aria-hidden="true" />Copy role</DropdownMenu.Item>
    {/if}
    {#if deletable(role)}
      <DropdownMenu.Item variant="destructive" onSelect={() => (chosen = { role, action: 'delete' })}><Trash2Icon aria-hidden="true" />Delete role</DropdownMenu.Item>
    {/if}
  </MoreMenu>
{/snippet}

<NewRoleSheet bind:open={creating} {roles} {actor} {from} />

<ConfirmDialog
  bind:open={deleteOpen}
  title={`Delete the ${deleting?.name ?? ''} role?`}
  description="It cannot be undone. A role somebody has cannot be deleted: give them another role first."
  cancelLabel="Keep it"
  confirmLabel="Delete role"
  failure="Could not delete the role"
  onconfirm={deleteRole}
>
  {#snippet icon()}<Trash2Icon aria-hidden="true" />{/snippet}
</ConfirmDialog>
