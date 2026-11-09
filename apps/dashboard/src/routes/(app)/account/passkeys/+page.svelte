<script lang="ts">
  import KeyRoundIcon from '~icons/lucide/key-round';
  import PlusIcon from '~icons/lucide/plus';
  import TrashIcon from '~icons/lucide/trash-2';
  import type { PasskeySummary } from '@screenhire/api-client';
  import { createListPasskeys, createRemovePasskey } from '@screenhire/api-client/svelte-query';
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
  import Notice from '#lib/components/notice.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { deviceName, passkeysSupported, runPasskeyRegistration } from '#lib/passkeys.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { cn } from '#lib/utils.js';

  // The signed-in person's passkeys (docs/architecture/sign-in-and-tenancy.md, "Passkeys"), on the list template. A
  // passkey belongs to the person, not to an organization, so this page is under their menu and not under Settings, and
  // needs no permission. Adding names the device first, then the browser's own prompt takes over; removing is confirmed,
  // and the API refuses the last way to sign in, which the dialog shows in its own words.
  const passkeysPath = '/v1/auth/passkeys';

  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const passkeys = createListPasskeys();
  const remove = createRemovePasskey(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, passkeysPath) } }));

  // Read once: whether this browser has WebAuthn does not change while the page is open.
  const supported = passkeysSupported();
  const list = $derived(passkeys.data?.data.passkeys ?? []);
  const limit = $derived(passkeys.data?.data.limit);
  const full = $derived(limit !== undefined && list.length >= limit);
  // The limit is said only when it is close (W10): two passkeys from it. At it, the sentence is Add's reason instead.
  const nearLimit = $derived(limit !== undefined && !full && limit - list.length <= 2);

  // The table sorts (L2), the passkey used most recently first; one never used counts as the oldest.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, PasskeySummary, any>[] = [
    { id: 'name', header: 'Name', accessorFn: (passkey) => passkey.name, sortFn: 'alphanumeric' },
    { id: 'added', header: 'Added', accessorFn: (passkey) => passkey.createdAt, sortFn: 'basic', sortDescFirst: true },
    { id: 'lastUsed', header: 'Last used', accessorFn: (passkey) => passkey.lastUsedAt ?? '', sortFn: 'basic', sortDescFirst: true },
  ];
  const sort = createTableSort('passkeys', { id: 'lastUsed', desc: true }, ['name', 'added', 'lastUsed']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return list;
    },
    getRowId: (passkey) => passkey.id,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));

  let announcement = $state('');

  // Adding: a name first (a device, so the list tells them apart), then the browser's prompt.
  let adding = $state(false);
  const schema = z.object({ name: z.string().trim().min(1, 'Name the passkey.').max(60, 'Use at most 60 characters.') });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      const outcome = await runPasskeyRegistration(submitted.data.name.trim());
      if (outcome.kind === 'success') {
        adding = false;
        announcement = `${outcome.value.name} was added.`;
        await invalidatePath(queryClient, passkeysPath);
      } else if (outcome.kind === 'failed') {
        submitted.valid = false;
        submitted.errors._errors = [outcome.message ?? 'We could not add a passkey. Try again, or use another device.'];
      }
      // Closing the browser's prompt leaves the dialog as it was, to try again or cancel.
    },
  });

  $effect(() => {
    if (adding) reset({ data: { name: deviceName() } });
  });

  let removing = $state<PasskeySummary | null>(null);
  let removeOpen = $state(false);
  let removeChosen = false;
  const onMenuClosed = () => {
    if (removeChosen) removeOpen = true;
    removeChosen = false;
  };

  async function removePasskey() {
    if (!removing) return;
    await remove.mutateAsync({ passkeyId: removing.id });
    announcement = `${removing.name} was removed.`;
  }
</script>

<svelte:head>
  <title>Passkeys · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Account' }, { label: 'Passkeys' }]} />

<main class={[sidebar.isMobile && supported && 'pb-28']}>
  <HeaderBand
    title="Passkeys"
    count={passkeys.isSuccess ? list.length : undefined}
    countLabel={list.length === 1 ? 'passkey' : 'passkeys'}
    actions={supported && !sidebar.isMobile ? bandActions : undefined}
  />

  <div class="flex flex-col gap-5 px-4 py-6 md:px-6">
    <p role="status" class="sr-only">{announcement}</p>
    {#if !supported}
      <Notice>This browser cannot add passkeys. Use a current version of Chrome, Edge, Safari or Firefox.</Notice>
    {/if}
    {#if passkeys.isError}
      <Panel label="Passkeys">
        <QueryError message={describeError(passkeys.error, 'Could not load your passkeys')} onretry={() => passkeys.refetch()} inset />
      </Panel>
    {:else}
      <Panel label="Passkeys" flush footer={nearLimit ? limitNote : undefined}>
        {#if passkeys.isPending}
          <div aria-hidden="true" class="flex flex-col">
            {#each [0, 1] as row (row)}
              <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
                <Skeleton class="h-4 min-w-0 flex-1" />
                <Skeleton class="h-4 w-24" />
              </div>
            {/each}
          </div>
          <p class="sr-only" role="status">Loading your passkeys…</p>
        {:else if list.length === 0}
          <!-- Add is in the header band, or the bar on a phone: the empty state does not offer it a second time. -->
          <Empty.Root>
            <Empty.Header>
              <Empty.Media variant="icon"><KeyRoundIcon /></Empty.Media>
              <Empty.Title>No passkeys yet</Empty.Title>
              <Empty.Description>A passkey signs you in with your fingerprint, face or screen lock, with no email link to wait for.</Empty.Description>
            </Empty.Header>
          </Empty.Root>
        {:else}
          <div class="flex justify-end border-b border-border-subtle px-4 py-2 md:hidden"><SortMenu {table} /></div>
          <ul class="md:hidden">
            {#each sorted as passkey (passkey.id)}
              <li class="flex min-w-0 items-start gap-2 border-b border-border-subtle px-4 py-3 last:border-0">
                <div class="flex min-w-0 flex-1 flex-col gap-1">
                  <span class="min-w-0 font-semibold break-words">{passkey.name}</span>
                  <span class="flex flex-wrap gap-x-4 text-caption text-muted-foreground">
                    <span>Added <DateText at={passkey.createdAt} /></span>
                    <span>{#if passkey.lastUsedAt}Last used <RelativeTime at={passkey.lastUsedAt} class="inline" />{:else}Never used{/if}</span>
                  </span>
                </div>
                {@render passkeyMenu(passkey)}
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
                {#each sorted as passkey (passkey.id)}
                  <Table.Row class="h-12">
                    <Table.Cell class="w-full max-w-0 px-4.5"><TruncatedText text={passkey.name} class="font-semibold" /></Table.Cell>
                    <Table.Cell class="text-muted-foreground"><DateText at={passkey.createdAt} /></Table.Cell>
                    <Table.Cell class="text-muted-foreground">
                      {#if passkey.lastUsedAt}<RelativeTime at={passkey.lastUsedAt} />{:else}<span class="sr-only">Never used</span>{/if}
                    </Table.Cell>
                    <Table.Cell class="pr-4.5 text-right">{@render passkeyMenu(passkey)}</Table.Cell>
                  </Table.Row>
                {/each}
              </Table.Body>
            </Table.Root>
          </div>
        {/if}
      </Panel>
    {/if}
  </div>

  {#if sidebar.isMobile && supported}
    <ActionBar>
      <div class="flex flex-col gap-1.5">{@render addButton()}</div>
    </ActionBar>
  {/if}
</main>

{#snippet bandActions()}
  <div class="flex flex-wrap items-center justify-end gap-x-3 gap-y-1">{@render addButton()}</div>
{/snippet}

<!-- At the limit the button is off, and the sentence beside it says why. -->
{#snippet addButton()}
  {#if full}<span class="text-caption text-muted-foreground">You have {limit} passkeys, the most you can keep. Remove one to add another.</span>{/if}
  <Button size="lg" disabled={full} onclick={() => (adding = true)}><PlusIcon aria-hidden="true" />Add a passkey</Button>
{/snippet}

{#snippet limitNote()}
  <p class="text-caption text-muted-foreground">You can keep up to {limit} passkeys.</p>
{/snippet}

{#snippet passkeyMenu(passkey: PasskeySummary)}
  <MoreMenu label={`Actions for ${passkey.name}`} size="icon" quiet onclosed={onMenuClosed}>
    <DropdownMenu.Item
      variant="destructive"
      onSelect={() => {
        removing = passkey;
        removeChosen = true;
      }}><TrashIcon aria-hidden="true" />Remove passkey</DropdownMenu.Item
    >
  </MoreMenu>
{/snippet}

<ConfirmDialog
  bind:open={removeOpen}
  title={`Remove ${removing?.name ?? 'this passkey'}?`}
  description="It can no longer sign you in. Remove it from the device too if you want it gone there."
  cancelLabel="Keep it"
  confirmLabel="Remove passkey"
  failure="Could not remove the passkey"
  onconfirm={removePasskey}
>
  {#snippet icon()}<TrashIcon aria-hidden="true" />{/snippet}
</ConfirmDialog>

<Dialog.Root bind:open={adding}>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Add a passkey</Dialog.Title>
      <Dialog.Description>Name it for this device, then your browser asks you to confirm.</Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
      <Field.Field data-invalid={$errors.name ? true : undefined}>
        <Field.Label for="passkey-name">Name</Field.Label>
        <Input
          id="passkey-name"
          name="name"
          maxlength={60}
          required
          autocomplete="off"
          bind:value={$form.name}
          aria-invalid={$errors.name ? 'true' : undefined}
          aria-describedby={$errors.name ? 'passkey-name-error' : undefined}
        />
        <Field.Error id="passkey-name-error" errors={$errors.name?.map((message) => ({ message }))} />
      </Field.Field>
      <FailureText message={$errors._errors?.[0]} />
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Dialog.Close>
        <Button type="submit" size="lg" disabled={$submitting}><KeyRoundIcon aria-hidden="true" />Add passkey</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
