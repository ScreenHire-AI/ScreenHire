<script lang="ts">
  import BanIcon from '~icons/lucide/ban';
  import ExternalLinkIcon from '~icons/lucide/external-link';
  import KeyRoundIcon from '~icons/lucide/key-round';
  import PlusIcon from '~icons/lucide/plus';
  import type { ApiKeyScope, ApiKeySummary, HttpValidationProblemDetails } from '@screenhire/api-client';
  import {
    ApiError,
    createCreateApiKey,
    createGetApiKeysLastUpdate,
    createGetMeta,
    createListApiKeys,
    createRevokeApiKey,
    getGetApiKeysHistoryQueryOptions,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { page } from '$app/state';
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
  import ChoiceCard from '#lib/components/choice-card.svelte';
  import CodeBlock from '#lib/components/code-block.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistoryFact from '#lib/components/history-fact.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import SecretDialog from '#lib/components/secret-dialog.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import { settingsCrumbs } from '#lib/settings.js';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { can } from '#lib/permissions.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { cn } from '#lib/utils.js';

  // Organization API keys for the hiring API (ADR-0011), on the list template. A new key is shown once, in a dialog,
  // and kept nowhere else; the list shows only its first characters. Revoking is for good. Under the keys, a panel says
  // what the API is for, how to call it and where its reference is (design system section 21). The API's public origin
  // comes from the API itself (ScreenHire:Domains:ApiHost through /v1/meta), never from a host written here. The title's
  // row has Last updated and History: every key created and revoked, by whom (design system T6, S8). Invalidating the
  // keys' path refreshes both, since they live under it.
  const keysPath = '/v1/organization/api-keys';

  // Only an owner can give a key the Verification read scope (ADR-0021); the API refuses anyone else, so they get no
  // switch they cannot use, only a line saying who can (design system A3, E3).
  const mayGrantVerification = $derived(can(page.data.user, 'GrantVerificationAccess'));

  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const keys = createListApiKeys();
  const lastUpdate = createGetApiKeysLastUpdate();
  // gcTime 0: the response holds the token, so the mutation cache drops it as soon as the page goes, dialog open or not.
  const create = createCreateApiKey(() => ({ mutation: { gcTime: 0, onSettled: () => invalidatePath(queryClient, keysPath) } }));
  const revoke = createRevokeApiKey(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, keysPath) } }));

  const meta = createGetMeta();
  const apiOrigin = $derived(meta.data?.data.apiOrigin);
  const all = $derived(keys.data?.data ?? []);
  let search = $state('');
  const list = $derived.by(() => {
    const words = search.trim().toLowerCase();
    return words ? all.filter((key) => key.name.toLowerCase().includes(words)) : all;
  });
  const access = (scopes: readonly ApiKeyScope[]) => (scopes.includes('Write') ? 'Read and write' : 'Read only');
  // What the Verification read scope opens, in the create dialog (ADR-0021).
  const verificationOpens = 'Opens verification levels, reasons and evidence.';
  // The Access column holds everything a key can do, so a row's name is the only line of text in it (L1). A revoked key
  // keeps the words it had, for anyone auditing what it could read; its Revoked status says it no longer can.
  const accessText = (key: ApiKeySummary) => (key.scopes.includes('VerificationRead') ? `${access(key.scopes)} and Verification read` : access(key.scopes));

  // The table sorts (L2), keys by name first.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, ApiKeySummary, any>[] = [
    { id: 'status', header: 'Status', accessorFn: (key) => (key.revokedAt ? 'Revoked' : 'Active'), sortFn: 'alphanumeric' },
    { id: 'name', header: 'Name', accessorFn: (key) => key.name, sortFn: 'alphanumeric' },
    { id: 'key', header: 'Key', accessorFn: (key) => key.prefix, sortFn: 'alphanumeric' },
    { id: 'access', header: 'Access', accessorFn: (key) => accessText(key), sortFn: 'alphanumeric' },
    { id: 'lastUsed', header: 'Last used', accessorFn: (key) => key.lastUsedAt ?? '', sortFn: 'basic', sortDescFirst: true },
    { id: 'created', header: 'Created', accessorFn: (key) => key.createdAt, sortFn: 'basic', sortDescFirst: true },
  ];
  const sort = createTableSort('api-keys', { id: 'name', desc: false }, ['status', 'name', 'key', 'access', 'lastUsed', 'created']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return list;
    },
    getRowId: (key) => key.id,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));

  let announcement = $state('');

  let revoking = $state<ApiKeySummary | null>(null);
  let revokeOpen = $state(false);
  let revokeChosen = false;
  const onMenuClosed = () => {
    if (revokeChosen) revokeOpen = true;
    revokeChosen = false;
  };

  async function revokeKey() {
    if (!revoking) return;
    await revoke.mutateAsync({ keyId: revoking.id });
    announcement = `${revoking.name} was revoked.`;
  }

  // The new key's token, only while its dialog is open.
  let token = $state('');
  let tokenOpen = $state(false);

  let creating = $state(false);
  const schema = z.object({
    name: z.string().trim().min(1, 'Give the key a name.').max(100, 'Keep the name to 100 characters or fewer.'),
    access: z.enum(['Read', 'Write']),
    verification: z.boolean(),
  });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '', access: 'Read' as 'Read' | 'Write', verification: false }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      try {
        const scopes: ApiKeyScope[] = submitted.data.access === 'Write' ? ['Read', 'Write'] : ['Read'];
        if (submitted.data.verification && mayGrantVerification) scopes.push('VerificationRead');
        const response = await create.mutateAsync({ data: { name: submitted.data.name.trim(), scopes } });
        creating = false;
        token = response.data.token;
        tokenOpen = true;
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not create the key')];
        }
      }
    },
  });

  $effect(() => {
    if (creating) reset({ data: { name: '', access: 'Read', verification: false } });
  });
</script>

<svelte:head>
  <title>API keys · ScreenHire</title>
</svelte:head>

<TopBar crumbs={settingsCrumbs('/settings/api-keys')} back />

<main class={[sidebar.isMobile && 'pb-28']}>
  <HeaderBand
    title="API keys"
    oneRow
    count={keys.isSuccess ? all.length : undefined}
    countLabel={all.length === 1 ? 'key' : 'keys'}
    actions={bandActions}
  />

  <SettingsMenu>
    <p role="status" class="sr-only">{announcement}</p>
    {#if keys.isError}
      <Panel title="Keys"><QueryError message={describeError(keys.error, 'Could not load the API keys')} onretry={() => keys.refetch()} inset /></Panel>
    {:else}
      <Panel title="Keys" flush>
        {#snippet tools()}
          <!-- Past about ten, a list has a search, so adding one never changes the layout (S9). -->
          {#if all.length > 10}<SearchField bind:value={search} label="Search keys" class="ml-auto flex-[0_1_15rem]" />{/if}
        {/snippet}
        {#if keys.isPending}
          <div aria-hidden="true" class="flex flex-col">
            {#each [0, 1] as row (row)}
              <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
                <Skeleton class="h-4 w-20" />
                <Skeleton class="h-4 min-w-0 flex-1" />
                <Skeleton class="h-4 w-24" />
              </div>
            {/each}
          </div>
          <p class="sr-only" role="status">Loading the API keys…</p>
        {:else if all.length === 0}
          <Empty.Root>
            <Empty.Header>
              <Empty.Media variant="icon"><KeyRoundIcon /></Empty.Media>
              <Empty.Title>No API keys yet</Empty.Title>
            </Empty.Header>
            <Empty.Content><Button variant="outline" size="lg" onclick={() => (creating = true)}><PlusIcon aria-hidden="true" />Create key</Button></Empty.Content>
          </Empty.Root>
        {:else}
          <div class="flex justify-end border-b border-border-subtle px-4 py-2 md:hidden"><SortMenu {table} /></div>
          {#if list.length === 0}
            <p class="border-b border-border-subtle px-4.5 py-3 text-muted-foreground max-md:px-4">No key matches the search.</p>
          {/if}
          <ul class="md:hidden">
            {#each sorted as key (key.id)}
              <li class="flex min-w-0 items-start gap-2 border-b border-border-subtle px-4 py-3 last:border-0">
                <div class="flex min-w-0 flex-1 flex-col gap-1.5">
                  <span class="flex min-w-0 flex-wrap items-center gap-2">
                    {@render status(key)}
                    <span class="min-w-0 font-semibold break-words">{key.name}</span>
                  </span>
                  <span class="flex flex-wrap gap-1.5">
                    <Badge variant="muted" class="font-mono">{key.prefix}…</Badge>
                    <Badge variant="muted">{accessText(key)}</Badge>
                  </span>
                </div>
                {#if !key.revokedAt}{@render keyMenu(key)}{/if}
              </li>
            {/each}
          </ul>
          <div class="overflow-x-auto max-md:hidden">
            <Table.Root>
              <Table.Header>
                {#each table.getHeaderGroups() as group (group.id)}
                  <Table.Row class="bg-muted hover:bg-muted">
                    {#each group.headers as header (header.id)}
                      <SortHeader {header} class={header.id === 'status' ? 'px-4.5' : undefined} />
                    {/each}
                    <Table.Head class="w-12 pr-4.5"><span class="sr-only">Actions</span></Table.Head>
                  </Table.Row>
                {/each}
              </Table.Header>
              <Table.Body>
                {#each sorted as key (key.id)}
                  <Table.Row class="h-12">
                    <Table.Cell class="px-4.5">{@render status(key)}</Table.Cell>
                    <Table.Cell class="w-full max-w-0">
                      <TruncatedText text={key.name} class="font-semibold" />
                    </Table.Cell>
                    <Table.Cell class="text-muted-foreground">{key.prefix}…</Table.Cell>
                    <Table.Cell><TruncatedText text={accessText(key)} /></Table.Cell>
                    <Table.Cell class="text-muted-foreground">{#if key.lastUsedAt}<DateText at={key.lastUsedAt} />{:else}Never{/if}</Table.Cell>
                    <Table.Cell class="text-muted-foreground"><DateText at={key.createdAt} /></Table.Cell>
                    <Table.Cell class="pr-4.5 text-right">{#if !key.revokedAt}{@render keyMenu(key)}{/if}</Table.Cell>
                  </Table.Row>
                {/each}
              </Table.Body>
            </Table.Root>
          </div>
        {/if}
      </Panel>
    {/if}

    <Panel title="Use the ScreenHire API">
      <div class="flex flex-col gap-4">
        <SummaryList>
          <SummaryItem label="Base URL">
            {#if apiOrigin}<span class="font-mono text-code break-all">{apiOrigin}/v1</span>{:else}<Skeleton class="h-4 w-48" />{/if}
          </SummaryItem>
          <SummaryItem label="Header"><span class="font-mono text-code break-all">Authorization: Bearer shk_…</span></SummaryItem>
          <SummaryItem label="Access">Read only, or read and write, which can do what a Recruiter can</SummaryItem>
          <SummaryItem label="Verification read">
            Levels, reasons and evidence, and candidates by level. Only an Owner can give it, and every read goes in the application's review log. A level is never a reason to reject on its own.
          </SummaryItem>
          <SummaryItem label="Reference">
            {#if apiOrigin}
              <span class="flex flex-wrap gap-x-4 gap-y-1">
                <a href={`${apiOrigin}/scalar`} target="_blank" rel="noreferrer" class="inline-flex items-center gap-1.5 text-primary">
                  <ExternalLinkIcon aria-hidden="true" class="shrink-0" /><span class="underline underline-offset-2">API reference</span> <span class="sr-only">(opens in a new tab)</span>
                </a>
                <a href={`${apiOrigin}/openapi/v1.json`} target="_blank" rel="noreferrer" class="inline-flex items-center gap-1.5 text-primary">
                  <ExternalLinkIcon aria-hidden="true" class="shrink-0" /><span class="underline underline-offset-2">OpenAPI file</span> <span class="sr-only">(opens in a new tab)</span>
                </a>
              </span>
            {:else if meta.isError}
              <FailureText message={describeError(meta.error, "Could not find the API's address")} />
            {:else}<Skeleton class="h-4 w-48" />{/if}
          </SummaryItem>
        </SummaryList>
        {#if apiOrigin}
          <CodeBlock code={`curl ${apiOrigin}/v1/jobs \\\n  -H "Authorization: Bearer $SCREENHIRE_KEY"`} label="Example request" />
        {/if}
      </div>
    </Panel>
  </SettingsMenu>

  {#if sidebar.isMobile}
    <ActionBar>{@render createButton()}</ActionBar>
  {/if}
</main>

<!-- On a phone, Create key moves to the bar at the bottom and the title's row keeps Last updated (section 8). -->
{#snippet bandActions()}
  {#if lastUpdate.data}<HistoryFact value={lastUpdate.data.data} name="API keys" page={(before) => getGetApiKeysHistoryQueryOptions({ before })} />{/if}
  {#if !sidebar.isMobile}{@render createButton()}{/if}
{/snippet}

{#snippet createButton()}
  <Button size="lg" onclick={() => (creating = true)}><PlusIcon aria-hidden="true" />Create key</Button>
{/snippet}

{#snippet status(key: ApiKeySummary)}
  {#if key.revokedAt}<StatusBadge status="notInEffect" label="Revoked" quiet />{:else}<StatusBadge status="done" label="Active" quiet />{/if}
{/snippet}

{#snippet keyMenu(key: ApiKeySummary)}
  <MoreMenu label={`Actions for ${key.name}`} size="icon" quiet onclosed={onMenuClosed}>
    <DropdownMenu.Item
      variant="destructive"
      onSelect={() => {
        revoking = key;
        revokeChosen = true;
      }}><BanIcon aria-hidden="true" />Revoke key</DropdownMenu.Item
    >
  </MoreMenu>
{/snippet}

<ConfirmDialog
  bind:open={revokeOpen}
  title={`Revoke ${revoking?.name ?? 'this key'}?`}
  description="Requests that use it are refused from now on. A revoked key cannot be turned back on."
  cancelLabel="Keep it"
  confirmLabel="Revoke key"
  failure="Could not revoke the key"
  onconfirm={revokeKey}
>
  {#snippet icon()}<BanIcon aria-hidden="true" />{/snippet}
</ConfirmDialog>

<SecretDialog bind:open={tokenOpen} title="Your new API key" label="API key" value={token} onclose={() => ((token = ''), create.reset())} />

<Dialog.Root bind:open={creating}>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Create an API key</Dialog.Title>
      <Dialog.Description>For the hiring API: jobs, applications, candidates, templates and reports.</Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
      <Field.Field data-invalid={$errors.name ? true : undefined}>
        <Field.Label for="key-name">Name</Field.Label>
        <Input
          id="key-name"
          name="name"
          maxlength={100}
          required
          autocomplete="off"
          bind:value={$form.name}
          aria-invalid={$errors.name ? 'true' : undefined}
          aria-describedby={$errors.name ? 'key-name-error' : undefined}
        />
        <Field.Error id="key-name-error" errors={$errors.name?.map((message) => ({ message }))} />
      </Field.Field>
      <div class="flex flex-col gap-2">
        <span id="key-access-label" class="text-body font-medium">Access</span>
        <div role="radiogroup" aria-labelledby="key-access-label" class="flex flex-col gap-2">
          <ChoiceCard name="access" value="Read" bind:group={$form.access} title="Read only" description="Reads jobs, candidates, applications and templates." />
          <ChoiceCard name="access" value="Write" bind:group={$form.access} title="Read and write" description="Also changes hiring data, as a Recruiter can." />
        </div>
      </div>
      {#if mayGrantVerification}
        <div class="flex items-start gap-4">
          <div class="flex min-w-0 flex-1 flex-col gap-1">
            <Field.Label for="key-verification">Verification read</Field.Label>
            <Field.Description id="key-verification-hint">{verificationOpens}</Field.Description>
          </div>
          <Switch id="key-verification" bind:checked={$form.verification} aria-describedby="key-verification-hint" class="mt-1" />
        </div>
      {:else}
        <p class="text-caption text-muted-foreground">Only an owner can give a key Verification read.</p>
      {/if}
      <FailureText message={$errors._errors?.[0]} />
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Dialog.Close>
        <Button type="submit" size="lg" disabled={$submitting}><PlusIcon aria-hidden="true" />Create key</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
