<script lang="ts">
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import CopyIcon from '~icons/lucide/copy';
  import LockIcon from '~icons/lucide/lock';
  import SaveIcon from '~icons/lucide/save';
  import ShieldUserIcon from '~icons/lucide/shield-user';
  import Trash2Icon from '~icons/lucide/trash-2';
  import UsersRoundIcon from '~icons/lucide/users-round';
  import type { CustomRoleDetail, HttpValidationProblemDetails, Permission, PermissionGroup, PermissionSummary, RoleSummary, TeamMember } from '@screenhire/api-client';
  import {
    ApiError,
    createDeleteRole,
    createGetRole,
    createListRoles,
    createListTeamMembers,
    createUpdateRole,
    getGetRoleHistoryQueryOptions,
  } from '@screenhire/api-client/svelte-query';
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
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import BuiltInBadge from '#lib/components/built-in-badge.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import Fact from '#lib/components/fact.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistorySheet from '#lib/components/history-sheet.svelte';
  import LastUpdated from '#lib/components/last-updated.svelte';
  import MemberJobs from '#lib/components/member-jobs.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import NewRoleSheet from '#lib/components/new-role-sheet.svelte';
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
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { createHistoryPages } from '#lib/history-pages.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { mayAssign, roleKey, settingsCrumbs } from '#lib/settings.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { lastUpdateWords } from '#lib/history.js';
  import type { PageProps } from './$types';

  // One role's page (design system section 21, ADR-0014). A custom role is edited here on the form template: its name,
  // what it may do as a checkbox per permission, then Save. Seeing jobs and candidates is in every role, Owner-only
  // permissions are never in a custom role, and nobody gives a permission their own role lacks, so those rows are locked
  // and say why (E3). Last updated and History come from change history (T6); Copy role and Delete role are in the More
  // menu (A2), and the API refuses to delete a role somebody has. A built-in role is a read-only view whose primary action
  // is Copy role (section 7); nothing can change it, so it has no History.
  let { params }: PageProps = $props();

  const rolesPath = '/v1/organization/roles';
  const listHref = '/settings/roles';
  const builtInKeys = ['owner', 'admin', 'recruiter', 'viewer'];
  const isBuiltIn = $derived(builtInKeys.includes(params.roleId));

  const queryClient = useQueryClient();
  const catalogue = createListRoles();
  const role = createGetRole(
    () => params.roleId,
    () => ({ query: { enabled: !isBuiltIn } }),
  );
  const invalidate = () => invalidatePath(queryClient, rolesPath);
  const update = createUpdateRole(() => ({ mutation: { onSettled: invalidate } }));
  const remove = createDeleteRole(() => ({ mutation: { onSettled: invalidate } }));
  const team = createListTeamMembers();
  const members = $derived(team.data?.data);
  const currentUserId = $derived(page.data.user?.id);
  const actor = $derived<Permission[]>(page.data.user?.organization?.permissions ?? []);

  const roles = $derived<RoleSummary[]>(catalogue.data?.data.roles ?? []);
  const permissions = $derived<PermissionSummary[]>(catalogue.data?.data.permissions ?? []);
  const summary = $derived(roles.find((candidate) => roleKey(candidate) === params.roleId));
  const detail = $derived<CustomRoleDetail | undefined>(isBuiltIn ? undefined : role.data?.data);
  const name = $derived(summary?.name ?? detail?.name ?? 'Role');
  const notFound = $derived(isBuiltIn ? catalogue.isSuccess && !summary : role.error instanceof ApiError && role.error.status === 404);
  const loadError = $derived(isBuiltIn ? catalogue.error : (role.error ?? catalogue.error));
  const ready = $derived(catalogue.isSuccess && (isBuiltIn ? Boolean(summary) : Boolean(detail)));
  // A role with more access than the person's own can be read here but not changed (TeamPolicy).
  const editable = $derived(!isBuiltIn && detail !== undefined && mayAssign(actor, detail.permissions));
  // Nobody copies the Owner role, and a role with more access than the person's own is not theirs to copy.
  const mayCopy = $derived(params.roleId !== 'owner' && summary !== undefined && mayAssign(actor, summary.permissions));
  const people = $derived((members ?? []).filter((member) => roleKey(member) === params.roleId));

  // The people table sorts (L2), by name first, with Team's columns for the same facts.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const peopleColumns: ColumnDef<typeof features, TeamMember, any>[] = [
    { id: 'name', header: 'Name', accessorFn: (member) => member.displayName, sortFn: 'alphanumeric' },
    { id: 'email', header: 'Email', accessorFn: (member) => member.email, sortFn: 'alphanumeric' },
    // All jobs first, then by how many jobs they are on, most first.
    { id: 'jobs', header: 'Jobs', accessorFn: (member) => (member.seesAllJobs ? Number.MAX_SAFE_INTEGER : member.jobCount), sortFn: 'basic', sortDescFirst: true },
    { id: 'joined', header: 'Joined', accessorFn: (member) => member.joinedAt, sortFn: 'basic', sortDescFirst: true },
  ];
  const peopleSort = createTableSort('role-people', { id: 'name', desc: false }, ['name', 'email', 'jobs', 'joined']);
  const peopleTable = createTable({
    ...tableSortOptions(() => peopleSort),
    features,
    columns: peopleColumns,
    get data() {
      return people;
    },
    getRowId: (member) => member.id,
  });
  const sortedPeople = $derived(peopleTable.getRowModel().rows.map((row) => row.original));
  // Team's own check for its row menu: never your own access, and only a role the person may give.
  const manageable = (member: TeamMember) => member.userId !== currentUserId && mayAssign(actor, summary?.permissions ?? []);
  const groups = $derived.by(() => {
    const byGroup = new Map<PermissionGroup, PermissionSummary[]>();
    for (const permission of permissions) byGroup.set(permission.group, [...(byGroup.get(permission.group) ?? []), permission]);
    return [...byGroup];
  });
  const assignable = $derived(permissions.filter((permission) => !permission.ownerOnly));

  /** Why a permission's row cannot be changed here, or null when its checkbox works. */
  function locked(permission: PermissionSummary): string | null {
    if (permission.everyRole) return 'Every role has it.';
    if (permission.ownerOnly) return 'Owner only. A custom role cannot have it.';
    if (!actor.includes(permission.permission)) return 'Your role does not have it, so you cannot give it.';
    return null;
  }

  let historyOpen = $state(false);
  const history = createHistoryPages(
    (before) => getGetRoleHistoryQueryOptions(params.roleId, { before }),
    () => historyOpen && !isBuiltIn,
  );

  let saved = $state('');
  const schema = z.object({
    name: z.string().trim().min(1, 'Enter a name.').max(50, 'Use at most 50 characters.'),
    permissions: z.array(z.string()),
  });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '', permissions: [] as string[] }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      saved = '';
      if (!submitted.valid) return;
      try {
        await update.mutateAsync({
          roleId: params.roleId,
          // In the catalogue's order, whichever order they were ticked in. A locked row has no checkbox, so the ones every
          // role has are added here. The role holds nothing the person lacks, or the form would not show (editable).
          data: {
            name: submitted.data.name.trim(),
            permissions: permissions
              .filter((permission) => permission.everyRole || submitted.data.permissions.includes(permission.permission))
              .map((permission) => permission.permission),
          },
        });
        saved = 'Saved.';
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the role')];
        }
      }
    },
  });

  // The form starts from the role as the API has it, and again when another role's page opens.
  let loadedFor = '';
  $effect(() => {
    if (detail && loadedFor !== detail.id) {
      loadedFor = detail.id;
      saved = '';
      reset({ data: { name: detail.name, permissions: [...detail.permissions] } });
    }
  });
  const chosen = $derived(assignable.filter((permission) => $form.permissions.includes(permission.permission)).length);

  let copying = $state(false);
  // Delete opens its dialog once the More menu has closed, so focus can go back to the menu's button.
  let menuChoice: 'copy' | 'delete' | null = null;
  let confirmOpen = $state(false);
  function onMenuClosed() {
    if (menuChoice === 'copy') copying = true;
    else if (menuChoice === 'delete') confirmOpen = true;
    menuChoice = null;
  }

  async function deleteRole() {
    await remove.mutateAsync({ roleId: params.roleId });
    await goto(listHref);
  }
</script>

<svelte:head>
  <title>{name} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={settingsCrumbs(listHref, { label: name })} back />

<main class="flex flex-col">
  {#if notFound || (loadError && !ready)}
    <HeaderBand form title="Role" />
    <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
      <Panel title={notFound ? 'Role not found' : 'Could not load the role'}>
        {#if notFound}
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>It was deleted, or it belongs to another organization.</Empty.Description>
            </Empty.Header>
            <Empty.Content>
              <Button href={listHref} variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to roles</Button>
            </Empty.Content>
          </Empty.Root>
        {:else}
          <QueryError
            message={describeError(loadError, 'Could not load the role')}
            onretry={() => (isBuiltIn ? catalogue.refetch() : Promise.all([role.refetch(), catalogue.refetch()]))}
            inset
          />
        {/if}
      </Panel>
    </div>
  {:else if !ready}
    <HeaderBand form title="Role" />
    <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
      <Panel title="What this role can do">
        <div aria-hidden="true" class="flex flex-col gap-3">{#each [0, 1, 2, 3] as row (row)}<Skeleton class="h-6 w-full" />{/each}</div>
        <p class="sr-only" role="status">Loading the role…</p>
      </Panel>
    </div>
  {:else}
    <HeaderBand form title={name}>
      {#snippet status()}{#if isBuiltIn}<BuiltInBadge />{/if}{/snippet}
      {#snippet actions()}
        {#if isBuiltIn}
          {#if mayCopy}
            <Button size="lg" onclick={() => (copying = true)}><CopyIcon aria-hidden="true" />Copy role</Button>
          {/if}
        {:else if editable}
          <MoreMenu label="More actions" onclosed={onMenuClosed}>
            <DropdownMenu.Item onSelect={() => (menuChoice = 'copy')}><CopyIcon aria-hidden="true" />Copy role</DropdownMenu.Item>
            <DropdownMenu.Item variant="destructive" onSelect={() => (menuChoice = 'delete')}><Trash2Icon aria-hidden="true" />Delete role</DropdownMenu.Item>
          </MoreMenu>
        {/if}
      {/snippet}
      {#snippet facts()}
        {#if detail}<Fact label="Copied from">{detail.copiedFrom}</Fact>{/if}
        <Fact label={lastUpdateWords(detail?.lastUpdated ?? summary!.lastUpdated).label}>
          <LastUpdated
            value={detail?.lastUpdated ?? summary!.lastUpdated}
            {members}
            {currentUserId}
            onhistory={detail ? () => (historyOpen = true) : undefined}
          />
        </Fact>
      {/snippet}
    </HeaderBand>
    {#if detail}
      <HistorySheet
        bind:open={historyOpen}
        name={detail.name}
        entries={history.entries}
        error={history.error}
        onretry={history.retry}
        hasMore={history.hasMore}
        loadingMore={history.loadingMore}
        onmore={history.more}
        {members}
        {currentUserId}
        empty="No changes yet."
      />
    {/if}

    <div class="mx-auto flex w-full max-w-form flex-col gap-5 px-4 py-6 md:px-6">
      {#if editable && detail}
        <form method="POST" use:enhance novalidate class="flex flex-col gap-5">
          <Panel title="Role">
            <Field.Field data-invalid={$errors.name ? true : undefined}>
              <Field.Label for="role-name">Name</Field.Label>
              <Input
                id="role-name"
                name="name"
                autocomplete="off"
                maxlength={50}
                required
                bind:value={$form.name}
                aria-invalid={$errors.name ? 'true' : undefined}
                aria-describedby={$errors.name ? 'role-name-error' : undefined}
              />
              <Field.Error id="role-name-error" errors={$errors.name?.map((message) => ({ message }))} />
            </Field.Field>
          </Panel>
          <Panel title="What this role can do" count={chosen} flush>
            {#each groups as [group, inGroup] (group)}
              <fieldset class="min-w-0">
                <legend class="w-full border-b border-border-subtle bg-muted px-4.5 py-2.5 text-caption font-semibold max-md:px-4">{group}</legend>
                {#each inGroup as permission (permission.permission)}
                  {@const reason = locked(permission)}
                  {#if reason}
                    <div class="flex items-start gap-3 border-b border-border-subtle px-4.5 py-3 text-muted-foreground max-md:px-4" data-permission={permission.permission}>
                      <LockIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
                      <span class="flex min-w-0 flex-col gap-0.5">
                        <span class="text-foreground">{permission.name}{#if $form.permissions.includes(permission.permission)}<span class="sr-only">, included</span>{/if}</span>
                        <span class="text-caption">{reason}</span>
                      </span>
                    </div>
                  {:else}
                    <label class="flex items-start gap-3 border-b border-border-subtle px-4.5 py-3 has-focus-visible:ring-2 has-focus-visible:ring-ring has-focus-visible:ring-inset max-md:px-4 pointer-coarse:min-h-11">
                      <input
                        type="checkbox"
                        name="permissions"
                        value={permission.permission}
                        bind:group={$form.permissions}
                        class="mt-0.5 size-4 shrink-0 accent-primary"
                      />
                      <span class="flex min-w-0 flex-col gap-0.5">
                        <span>{permission.name}</span>
                        <span class="text-caption text-muted-foreground">{permission.description}</span>
                      </span>
                    </label>
                  {/if}
                {/each}
              </fieldset>
            {/each}
            <!-- The form's actions end it, in the panel's footer, so nothing readable sits on the page (S3). -->
            {#snippet footer()}
              <div class="flex flex-col gap-2">
                <Field.Error errors={$errors.permissions?._errors?.map((message) => ({ message }))} />
                <FailureText message={$errors._errors?.[0]} />
                <div class="flex flex-wrap items-center justify-end gap-2">
                  <p role="status" class="mr-auto text-muted-foreground empty:hidden">{saved}</p>
                  <Button href={listHref} variant="outline" size="lg">Cancel</Button>
                  <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save</Button>
                </div>
              </div>
            {/snippet}
          </Panel>
        </form>
      {:else}
        {@const held = detail?.permissions ?? summary?.permissions ?? []}
        <!-- A plain count, as the form's: a fraction reads as a score (E2). -->
        <Panel title="What this role can do" count={held.length}>
          <dl class="flex flex-col gap-3">
            {#each groups as [group, inGroup] (group)}
              {@const included = inGroup.filter((permission) => held.includes(permission.permission))}
              {#if included.length > 0}
                <div class="flex flex-col gap-1">
                  <dt class="text-caption font-semibold text-muted-foreground">{group}</dt>
                  {#each included as permission (permission.permission)}<dd>{permission.name}</dd>{/each}
                </div>
              {/if}
            {/each}
          </dl>
          {#snippet footer()}
            <!-- Read only says why and what to do (E3), in the footer because no sentence sits under a panel's title (W9).
                 It offers a copy only where the Copy role button is. -->
            <p class="text-muted-foreground">
              {#if isBuiltIn}
                ScreenHire supplies this role, so it cannot be changed.{mayCopy ? ' Copy it to make one of your own.' : ''}
              {:else}
                This role has more access than yours, so you cannot change it.
              {/if}
            </p>
          {/snippet}
        </Panel>
      {/if}

      <Panel title="People with this role" count={team.isSuccess ? people.length : undefined} flush>
        {#if team.isError}
          <div class="p-4"><QueryError message={describeError(team.error, 'Could not load the team')} onretry={() => team.refetch()} inset /></div>
        {:else if team.isPending}
          <div aria-hidden="true" class="flex h-12 items-center gap-4 px-4.5 max-md:px-4"><Skeleton class="h-4 w-48" /></div>
        {:else if people.length === 0}
          <Empty.Root>
            <Empty.Header>
              <Empty.Media variant="icon"><UsersRoundIcon /></Empty.Media>
              <Empty.Description>Nobody has this role yet. <a href="/settings/team" class="underline underline-offset-2">Team</a> gives it to someone.</Empty.Description>
            </Empty.Header>
          </Empty.Root>
        {:else}
          <!-- Phones get two-line items and the Sort chip (design system section 8, L2); wider screens the table, as on Team. -->
          <div class="flex justify-end border-b border-border-subtle px-4 py-2 md:hidden"><SortMenu table={peopleTable} /></div>
          <ul class="md:hidden">
            {#each sortedPeople as member (member.id)}
              <li class="flex min-w-0 items-start gap-2 border-b border-border-subtle px-4 py-3 last:border-0">
                <div class="flex min-w-0 flex-1 flex-col gap-1.5">
                  <span class="min-w-0 font-semibold break-words">{member.displayName}</span>
                  <span class="min-w-0 break-all text-muted-foreground">{member.email}</span>
                  <span class="flex flex-wrap items-center gap-1.5"><MemberJobs {member} /><Badge variant="muted">Joined <DateText at={member.joinedAt} /></Badge></span>
                </div>
                {#if manageable(member)}{@render personMenu(member)}{/if}
              </li>
            {/each}
          </ul>
          <div class="overflow-x-auto max-md:hidden">
            <Table.Root>
              <Table.Header>
                {#each peopleTable.getHeaderGroups() as group (group.id)}
                  <Table.Row class="bg-muted hover:bg-muted">
                    {#each group.headers as header (header.id)}
                      <SortHeader {header} class={header.id === 'name' ? 'px-4.5' : undefined} />
                    {/each}
                    <Table.Head class="w-12 pr-4.5"><span class="sr-only">Actions</span></Table.Head>
                  </Table.Row>
                {/each}
              </Table.Header>
              <Table.Body>
                {#each sortedPeople as member (member.id)}
                  <Table.Row class="h-12">
                    <Table.Cell class="max-w-48 px-4.5"><TruncatedText text={member.displayName} class="font-semibold" /></Table.Cell>
                    <Table.Cell class="w-full max-w-0 min-w-32 text-muted-foreground"><TruncatedText text={member.email} /></Table.Cell>
                    <Table.Cell class="whitespace-nowrap tabular-nums"><MemberJobs {member} /></Table.Cell>
                    <Table.Cell class="whitespace-nowrap text-muted-foreground"><DateText at={member.joinedAt} /></Table.Cell>
                    <Table.Cell class="pr-4.5 text-right">{#if manageable(member)}{@render personMenu(member)}{/if}</Table.Cell>
                  </Table.Row>
                {/each}
              </Table.Body>
            </Table.Root>
          </div>
        {/if}
      </Panel>
    </div>

    <!-- A person's access is changed on Team: Edit access opens their sheet there. -->
    {#snippet personMenu(member: TeamMember)}
      <MoreMenu label={`Actions for ${member.displayName}`} size="icon" quiet>
        <DropdownMenu.Item onSelect={() => void goto(`/settings/team?access=${member.id}`)}><ShieldUserIcon aria-hidden="true" />Edit access</DropdownMenu.Item>
      </MoreMenu>
    {/snippet}

    <NewRoleSheet bind:open={copying} {roles} {actor} from={params.roleId} />

    {#if editable}
      <ConfirmDialog
        bind:open={confirmOpen}
        title={`Delete the ${name} role?`}
        description="It cannot be undone. A role somebody has cannot be deleted: give them another role first."
        cancelLabel="Keep it"
        confirmLabel="Delete role"
        failure="Could not delete the role"
        onconfirm={deleteRole}
      >
        {#snippet icon()}<Trash2Icon aria-hidden="true" />{/snippet}
      </ConfirmDialog>
    {/if}
  {/if}
</main>
