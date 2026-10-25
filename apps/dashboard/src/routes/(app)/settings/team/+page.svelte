<script lang="ts">
  import SendIcon from '@lucide/svelte/icons/send';
  import UserMinusIcon from '@lucide/svelte/icons/user-minus';
  import UserPlusIcon from '@lucide/svelte/icons/user-plus';
  import UsersRoundIcon from '@lucide/svelte/icons/users-round';
  import XIcon from '@lucide/svelte/icons/x';
  import type { HttpValidationProblemDetails, OrganizationRole, PendingInvitation, RoleSummary, TeamMember } from '@screenhire/api-client';
  import {
    ApiError,
    createChangeMemberRole,
    createCreateInvitation,
    createListApiKeys,
    createListInvitations,
    createListRoles,
    createListTeamMembers,
    createRemoveMember,
    createRevokeApiKey,
    createRevokeInvitation,
  } from '@screenhire/api-client/svelte-query';
  import { CreateInvitationBody } from '@screenhire/api-client/zod';
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
  import ActionBar from '#lib/components/action-bar.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { can } from '#lib/permissions.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { assignableRoles, mayAssign, roleFromKey, roleKey } from '#lib/settings.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { cn } from '#lib/utils.js';
  import type { PageProps } from './$types';

  // The team (sign-in-and-tenancy.md, "Team members and invitations"): members with their roles, built-in or custom, and
  // the invitations nobody has answered yet, on the list template. A role shows by its name alone, with no Custom label
  // (W6). Someone whose role has Manage the team gives only roles that hold nothing their own does not (TeamPolicy), so
  // Owners manage everyone and Admins everyone but Owners. Your own row has no actions: handing over is done by another
  // owner. Invite opens a sheet that describes each role from the API's catalogue and starts at Viewer, the least access
  // (design system section 22).
  let { data }: PageProps = $props();

  const membersPath = '/v1/organization/members';
  const invitationsPath = '/v1/organization/invitations';

  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const members = createListTeamMembers();
  const invitations = createListInvitations();
  const changeRole = createChangeMemberRole(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, membersPath) } }));
  const remove = createRemoveMember(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, membersPath) } }));
  const revoke = createRevokeInvitation(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, invitationsPath) } }));
  // Removing a member offers to revoke the API keys they created (ADR-0011): a key outlives its creator otherwise. Only
  // for a role that manages API keys; anyone else's removal leaves the keys to someone who does.
  const mayRevokeKeys = $derived(can(data.user, 'ManageApiKeys'));
  const keys = createListApiKeys(() => ({ query: { enabled: mayRevokeKeys } }));
  const revokeKey = createRevokeApiKey(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, '/v1/organization/api-keys') } }));
  const catalogue = createListRoles();
  const invite = createCreateInvitation(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, invitationsPath) } }));

  const actor = $derived(data.user?.organization?.permissions ?? []);
  const myId = $derived(data.user?.id);
  const memberList = $derived(members.data?.data ?? []);
  const invitationList = $derived(invitations.data?.data ?? []);
  const roleList = $derived<RoleSummary[]>(catalogue.data?.data.roles ?? []);
  const offered = $derived(assignableRoles(actor, roleList));
  // A role is one key everywhere on the page: `viewer`, or a custom role's id (roleKey).
  const roleOf = (held: { role: OrganizationRole; customRoleId?: string | null }) => roleList.find((role) => roleKey(role) === roleKey(held));
  const mayHandle = (held: { role: OrganizationRole; customRoleId?: string | null }) => {
    const role = roleOf(held);
    return role !== undefined && mayAssign(actor, role.permissions);
  };
  const manageable = (member: TeamMember) => member.userId !== myId && mayHandle(member);
  // How a role reads in a sentence: a built-in one in lower case, a custom one as its name is written.
  const roleWords = (role: RoleSummary) => (role.role === 'Custom' ? role.name : role.name.toLowerCase());
  const article = (word: string) => (/^[aeiou]/i.test(word) ? 'an' : 'a');

  // Both tables sort (L2): people by name, roles by access (Owner first), dates newest first on the first click.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // The catalogue's order: built-in roles by access, then custom roles by name.
  const rank = (held: { role: OrganizationRole; customRoleId?: string | null }) => {
    const index = roleList.findIndex((role) => roleKey(role) === roleKey(held));
    return index === -1 ? roleList.length : index;
  };
  // `any`: one list mixes columns of different value types.
  const memberColumns: ColumnDef<typeof features, TeamMember, any>[] = [
    { id: 'name', header: 'Name', accessorFn: (member) => member.displayName, sortFn: 'alphanumeric' },
    { id: 'email', header: 'Email', accessorFn: (member) => member.email, sortFn: 'alphanumeric' },
    { id: 'role', header: 'Role', accessorFn: (member) => rank(member), sortFn: 'basic', sortDescFirst: false },
    { id: 'joined', header: 'Joined', accessorFn: (member) => member.joinedAt, sortFn: 'basic', sortDescFirst: true },
  ];
  const invitationColumns: ColumnDef<typeof features, PendingInvitation, any>[] = [
    { id: 'email', header: 'Email', accessorFn: (invitation) => invitation.email, sortFn: 'alphanumeric' },
    { id: 'role', header: 'Role', accessorFn: (invitation) => rank(invitation), sortFn: 'basic', sortDescFirst: false },
    { id: 'sent', header: 'Sent', accessorFn: (invitation) => invitation.createdAt, sortFn: 'basic', sortDescFirst: true },
    { id: 'expires', header: 'Expires', accessorFn: (invitation) => invitation.expiresAt, sortFn: 'basic' },
  ];
  const memberSort = createTableSort('team-members', { id: 'name', desc: false }, ['name', 'email', 'role', 'joined']);
  const invitationSort = createTableSort('team-invitations', { id: 'email', desc: false }, ['email', 'role', 'sent', 'expires']);
  const memberTable = createTable({
    ...tableSortOptions(() => memberSort),
    features,
    columns: memberColumns,
    get data() {
      return memberList;
    },
    getRowId: (member) => member.id,
  });
  const invitationTable = createTable({
    ...tableSortOptions(() => invitationSort),
    features,
    columns: invitationColumns,
    get data() {
      return invitationList;
    },
    getRowId: (invitation) => invitation.id,
  });
  const sortedMembers = $derived(memberTable.getRowModel().rows.map((row) => row.original));
  const sortedInvitations = $derived(invitationTable.getRowModel().rows.map((row) => row.original));

  // A role change or removal that failed, in one line under the members.
  let memberFailure = $state('');
  // What the last action did, for screen readers; the lists show it to everyone else.
  let announcement = $state('');

  async function setRole(member: TeamMember, key: string) {
    const role = roleList.find((candidate) => roleKey(candidate) === key);
    if (!role || key === roleKey(member)) return;
    memberFailure = '';
    try {
      await changeRole.mutateAsync({ memberId: member.id, data: { role: role.role, customRoleId: role.customRoleId } });
      announcement = `${member.displayName} is now ${article(roleWords(role))} ${roleWords(role)}.`;
    } catch (error) {
      memberFailure = describeError(error, `Could not change ${member.displayName}'s role`);
    }
  }

  // Remove and revoke confirm in a dialog (A2). The menu opens it once it has closed, so focus comes back to the menu.
  let removing = $state<TeamMember | null>(null);
  let removeOpen = $state(false);
  let revoking = $state<PendingInvitation | null>(null);
  let revokeOpen = $state(false);
  let confirmChosen = false;

  function onMenuClosed() {
    if (confirmChosen) {
      if (removing) removeOpen = true;
      else if (revoking) revokeOpen = true;
    }
    confirmChosen = false;
  }

  function askToRemove(member: TeamMember) {
    revoking = null;
    removing = member;
    confirmChosen = true;
  }

  function askToRevoke(invitation: PendingInvitation) {
    removing = null;
    revoking = invitation;
    confirmChosen = true;
  }

  // The active keys the member being removed created, and whether to revoke them too (on unless the person unticks it).
  const removingKeys = $derived(removing && mayRevokeKeys ? (keys.data?.data ?? []).filter((key) => !key.revokedAt && key.createdByUserId === removing!.userId) : []);
  let revokeTheirKeys = $state(true);
  $effect(() => {
    if (removeOpen) revokeTheirKeys = true;
  });

  async function removeMember() {
    if (!removing) return;
    const theirKeys = revokeTheirKeys ? removingKeys : [];
    memberFailure = '';
    await remove.mutateAsync({ memberId: removing.id });
    announcement = `${removing.displayName} was removed from the team.`;
    // The removal stands even when a revocation fails, so the dialog closes and the members panel says what is left.
    const failed = (await Promise.allSettled(theirKeys.map((key) => revokeKey.mutateAsync({ keyId: key.id })))).filter((result) => result.status === 'rejected');
    if (failed.length > 0) {
      memberFailure = `${failed.length === 1 ? '1 of their API keys' : `${failed.length} of their API keys`} could not be revoked. Revoke it in API keys.`;
    } else if (theirKeys.length > 0) {
      announcement += ` ${theirKeys.length === 1 ? 'Their API key was' : 'Their API keys were'} revoked.`;
    }
  }

  async function revokeInvitation() {
    if (!revoking) return;
    await revoke.mutateAsync({ invitationId: revoking.id });
    announcement = `The invitation to ${revoking.email} was revoked.`;
  }

  let inviting = $state(false);
  const schema = CreateInvitationBody.extend({
    email: z.string().trim().min(1, 'Enter an email address.').max(256, 'Keep the address to 256 characters or fewer.'),
    // The role's key (roleKey), which becomes the request's role and custom role id.
    role: z.string().min(1, 'Choose a role.'),
  });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ email: '', role: 'viewer' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      try {
        const email = submitted.data.email.trim();
        const role = roleFromKey(roleList, submitted.data.role);
        if (!role) {
          submitted.valid = false;
          submitted.errors.role = ['Choose a role.'];
          return;
        }
        await invite.mutateAsync({ data: { email, ...role } });
        inviting = false;
        announcement = `Invitation sent to ${email}.`;
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          // 404: the deployment has no email sender, so an invitation could not reach anyone.
          submitted.errors._errors = [
            error instanceof ApiError && error.status === 404
              ? 'Email is not set up here, so invitations cannot be sent.'
              : describeError(error, 'Could not send the invitation'),
          ];
        }
      }
    },
  });

  $effect(() => {
    if (inviting) reset({ data: { email: '', role: 'viewer' } });
  });
</script>

<svelte:head>
  <title>Team · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Team' }]} />

<main class={[sidebar.isMobile && 'pb-28']}>
  <HeaderBand
    title="Team"
    count={members.isSuccess ? memberList.length : undefined}
    countLabel={memberList.length === 1 ? 'member' : 'members'}
    actions={sidebar.isMobile ? undefined : inviteButton}
  />

  <SettingsMenu>
    <p role="status" class="sr-only">{announcement}</p>

    <Panel title="Members" flush footer={memberFailure ? memberFooter : undefined}>
      {#if members.isError}
        <div class="p-4"><QueryError message={describeError(members.error, 'Could not load the team')} onretry={() => members.refetch()} inset /></div>
      {:else if members.isPending}
        <div aria-hidden="true" class="flex flex-col">
          {#each [0, 1, 2] as row (row)}
            <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
              <Skeleton class="h-4 w-40" />
              <Skeleton class="h-4 min-w-0 flex-1" />
              <Skeleton class="h-4 w-24" />
            </div>
          {/each}
        </div>
        <p class="sr-only" role="status">Loading the team…</p>
      {:else}
        <!-- Phones get two-line items and the Sort chip (design system section 8, L2); wider screens the table. -->
        <div class="flex justify-end border-b border-border-subtle px-4 py-2 md:hidden"><SortMenu table={memberTable} /></div>
        <ul class="md:hidden">
          {#each sortedMembers as member (member.id)}
            <li class="flex min-w-0 items-start gap-2 border-b border-border-subtle px-4 py-3 last:border-0">
              <div class="flex min-w-0 flex-1 flex-col gap-1.5">
                <span class="flex min-w-0 flex-wrap items-center gap-2">
                  <span class="min-w-0 font-semibold break-words">{member.displayName}</span>
                  {#if member.userId === myId}<Badge variant="muted">You</Badge>{/if}
                </span>
                <span class="min-w-0 break-all text-muted-foreground">{member.email}</span>
                <span class="flex flex-wrap gap-1.5"><Badge variant="muted">{member.roleName}</Badge></span>
              </div>
              {#if manageable(member)}{@render memberMenu(member)}{/if}
            </li>
          {/each}
        </ul>
        <div class="overflow-x-auto max-md:hidden">
          <Table.Root>
            <Table.Header>
              {#each memberTable.getHeaderGroups() as group (group.id)}
                <Table.Row class="bg-muted hover:bg-muted">
                  {#each group.headers as header (header.id)}
                    <SortHeader {header} class={header.id === 'name' ? 'px-4.5' : undefined} />
                  {/each}
                  <Table.Head class="w-12 pr-4.5"><span class="sr-only">Actions</span></Table.Head>
                </Table.Row>
              {/each}
            </Table.Header>
            <Table.Body>
              {#each sortedMembers as member (member.id)}
                <Table.Row class="h-12">
                  <Table.Cell class="max-w-64 px-4.5">
                    <span class="flex min-w-0 items-center gap-2">
                      <TruncatedText text={member.displayName} class="font-semibold" />
                      {#if member.userId === myId}<Badge variant="muted" class="shrink-0">You</Badge>{/if}
                    </span>
                  </Table.Cell>
                  <Table.Cell class="w-full max-w-0 text-muted-foreground"><TruncatedText text={member.email} /></Table.Cell>
                  <Table.Cell class="max-w-48 pr-4"><TruncatedText text={member.roleName} /></Table.Cell>
                  <Table.Cell class="text-muted-foreground"><DateText at={member.joinedAt} /></Table.Cell>
                  <Table.Cell class="pr-4.5 text-right">{#if manageable(member)}{@render memberMenu(member)}{/if}</Table.Cell>
                </Table.Row>
              {/each}
            </Table.Body>
          </Table.Root>
        </div>
      {/if}
    </Panel>

    <Panel title="Invitations" count={invitations.isSuccess ? invitationList.length : undefined} flush>
      {#if invitations.isError}
        <div class="p-4">
          <QueryError message={describeError(invitations.error, 'Could not load the invitations')} onretry={() => invitations.refetch()} inset />
        </div>
      {:else if invitations.isPending}
        <div aria-hidden="true" class="flex h-12 items-center gap-4 px-4.5 max-md:px-4"><Skeleton class="h-4 w-48" /><Skeleton class="h-4 w-24" /></div>
      {:else if invitationList.length === 0}
        <Empty.Root>
          <Empty.Header>
            <Empty.Media variant="icon"><UsersRoundIcon /></Empty.Media>
            <Empty.Description>No open invitations.</Empty.Description>
          </Empty.Header>
          <Empty.Content><Button variant="outline" size="lg" onclick={() => (inviting = true)}><UserPlusIcon aria-hidden="true" />Invite</Button></Empty.Content>
        </Empty.Root>
      {:else}
        <div class="flex justify-end border-b border-border-subtle px-4 py-2 md:hidden"><SortMenu table={invitationTable} /></div>
        <ul class="md:hidden">
          {#each sortedInvitations as invitation (invitation.id)}
            <li class="flex min-w-0 items-start gap-2 border-b border-border-subtle px-4 py-3 last:border-0">
              <div class="flex min-w-0 flex-1 flex-col gap-1.5">
                <span class="min-w-0 font-semibold break-all">{invitation.email}</span>
                <span class="flex flex-wrap items-center gap-1.5">
                  <Badge variant="muted">{invitation.roleName}</Badge>
                  <Badge variant="muted">Expires <DateText at={invitation.expiresAt} /></Badge>
                </span>
              </div>
              {#if mayHandle(invitation)}{@render invitationMenu(invitation)}{/if}
            </li>
          {/each}
        </ul>
        <div class="overflow-x-auto max-md:hidden">
          <Table.Root>
            <Table.Header>
              {#each invitationTable.getHeaderGroups() as group (group.id)}
                <Table.Row class="bg-muted hover:bg-muted">
                  {#each group.headers as header (header.id)}
                    <SortHeader {header} class={header.id === 'email' ? 'px-4.5' : undefined} />
                  {/each}
                  <Table.Head class="w-12 pr-4.5"><span class="sr-only">Actions</span></Table.Head>
                </Table.Row>
              {/each}
            </Table.Header>
            <Table.Body>
              {#each sortedInvitations as invitation (invitation.id)}
                <Table.Row class="h-12">
                  <Table.Cell class="w-full max-w-0 px-4.5"><TruncatedText text={invitation.email} class="font-semibold" /></Table.Cell>
                  <Table.Cell class="max-w-48 pr-4"><TruncatedText text={invitation.roleName} /></Table.Cell>
                  <Table.Cell class="text-muted-foreground"><DateText at={invitation.createdAt} /></Table.Cell>
                  <Table.Cell class="text-muted-foreground"><DateText at={invitation.expiresAt} /></Table.Cell>
                  <Table.Cell class="pr-4.5 text-right">{#if mayHandle(invitation)}{@render invitationMenu(invitation)}{/if}</Table.Cell>
                </Table.Row>
              {/each}
            </Table.Body>
          </Table.Root>
        </div>
      {/if}
    </Panel>
  </SettingsMenu>

  {#if sidebar.isMobile}
    <ActionBar>{@render inviteButton()}</ActionBar>
  {/if}
</main>

{#snippet inviteButton()}
  <Button size="lg" onclick={() => (inviting = true)}><UserPlusIcon aria-hidden="true" />Invite</Button>
{/snippet}

{#snippet memberFooter()}
  <FailureText message={memberFailure} />
{/snippet}

{#snippet memberMenu(member: TeamMember)}
  <MoreMenu label={`Actions for ${member.displayName}`} size="icon" quiet onclosed={onMenuClosed}>
    <DropdownMenu.Label class="text-caption text-muted-foreground">Role</DropdownMenu.Label>
    <DropdownMenu.RadioGroup value={roleKey(member)} onValueChange={(key) => setRole(member, key)}>
      {#each offered as role (roleKey(role))}
        <DropdownMenu.RadioItem value={roleKey(role)} closeOnSelect>{role.name}</DropdownMenu.RadioItem>
      {/each}
    </DropdownMenu.RadioGroup>
    <DropdownMenu.Separator />
    <DropdownMenu.Item variant="destructive" onSelect={() => askToRemove(member)}><UserMinusIcon aria-hidden="true" />Remove from team</DropdownMenu.Item>
  </MoreMenu>
{/snippet}

{#snippet invitationMenu(invitation: PendingInvitation)}
  <MoreMenu label={`Actions for the invitation to ${invitation.email}`} size="icon" quiet onclosed={onMenuClosed}>
    <DropdownMenu.Item variant="destructive" onSelect={() => askToRevoke(invitation)}><XIcon aria-hidden="true" />Revoke invitation</DropdownMenu.Item>
  </MoreMenu>
{/snippet}

<ConfirmDialog
  bind:open={removeOpen}
  title={`Remove ${removing?.displayName ?? 'this member'} from the team?`}
  description="They lose access to this organization at once. Their past work stays."
  confirmLabel="Remove from team"
  failure="Could not remove the member"
  onconfirm={removeMember}
>
  {#snippet icon()}<UserMinusIcon aria-hidden="true" />{/snippet}
  {#if removingKeys.length > 0}
    <label class="flex items-center gap-2 pointer-coarse:min-h-11">
      <input type="checkbox" bind:checked={revokeTheirKeys} class="size-4 shrink-0 accent-primary" />
      Also revoke the {removingKeys.length === 1 ? 'API key' : `${removingKeys.length} API keys`} they created
    </label>
  {:else if keys.isError}
    <p class="text-muted-foreground">Their API keys could not be checked. Keys they created keep working until you revoke them in API keys.</p>
  {/if}
</ConfirmDialog>

<ConfirmDialog
  bind:open={revokeOpen}
  title={`Revoke the invitation to ${revoking?.email ?? 'this address'}?`}
  description="The emailed link stops working. You can invite them again later."
  cancelLabel="Keep it"
  confirmLabel="Revoke invitation"
  failure="Could not revoke the invitation"
  onconfirm={revokeInvitation}
>
  {#snippet icon()}<XIcon aria-hidden="true" />{/snippet}
</ConfirmDialog>

<Sheet.Root bind:open={inviting}>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Invite a colleague</Sheet.Title>
      <Sheet.Description>They get an email with a link to join. It works for 7 days.</Sheet.Description>
    </Sheet.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-5 px-4 pb-4">
      <Field.Field data-invalid={$errors.email ? true : undefined}>
        <Field.Label for="invite-email">Email</Field.Label>
        <Input
          id="invite-email"
          name="email"
          type="email"
          autocomplete="off"
          maxlength={256}
          required
          bind:value={$form.email}
          aria-invalid={$errors.email ? 'true' : undefined}
          aria-describedby={$errors.email ? 'invite-email-error' : undefined}
        />
        <Field.Error id="invite-email-error" errors={$errors.email?.map((message) => ({ message }))} />
      </Field.Field>
      <div class="flex flex-col gap-2">
        <div class="mb-1 flex items-baseline justify-between gap-3">
          <span id="invite-role-label" class="text-body font-medium">Role</span>
          <a href="/settings/roles/compare" class="text-caption underline underline-offset-2 pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center">Compare roles</a>
        </div>
        <div role="radiogroup" aria-labelledby="invite-role-label" class="flex flex-col gap-2">
          {#each offered as role (roleKey(role))}
            <label
              class="flex cursor-pointer items-start gap-3 rounded-lg border border-input p-3 has-checked:border-primary has-checked:bg-accent has-focus-visible:ring-2 has-focus-visible:ring-ring"
            >
              <input type="radio" name="role" value={roleKey(role)} bind:group={$form.role} class="mt-0.5 size-4 shrink-0 accent-primary" />
              <span class="flex min-w-0 flex-col gap-0.5">
                <span class="font-semibold break-words">{role.name}</span>
                <span class="text-muted-foreground">{role.description}</span>
              </span>
            </label>
          {/each}
        </div>
        <Field.Error errors={$errors.role?.map((message) => ({ message }))} />
      </div>
      <FailureText message={$errors._errors?.[0]} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={$submitting}><SendIcon aria-hidden="true" />Send invitation</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
