<script lang="ts">
  import BanIcon from '@lucide/svelte/icons/ban';
  import KeyRoundIcon from '@lucide/svelte/icons/key-round';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import type { ApiKeyScope, ApiKeySummary, HttpValidationProblemDetails } from '@screenhire/api-client';
  import { ApiError, createCreateApiKey, createListApiKeys, createRevokeApiKey } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
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
  import SecretDialog from '#lib/components/secret-dialog.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
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
  import * as Table from '#lib/components/ui/table/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { cn } from '#lib/utils.js';

  // Organization API keys for the hiring API (ADR-0011), on the list template. A new key is shown once, in a dialog,
  // and kept nowhere else; the list shows only its first characters. Revoking is for good.
  const keysPath = '/v1/organization/api-keys';

  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const keys = createListApiKeys();
  // gcTime 0: the response holds the token, so the mutation cache drops it as soon as the page goes, dialog open or not.
  const create = createCreateApiKey(() => ({ mutation: { gcTime: 0, onSettled: () => invalidatePath(queryClient, keysPath) } }));
  const revoke = createRevokeApiKey(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, keysPath) } }));

  const list = $derived(keys.data?.data ?? []);
  const access = (scopes: readonly ApiKeyScope[]) => (scopes.includes('Write') ? 'Read and write' : 'Read only');

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
  });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '', access: 'Read' as 'Read' | 'Write' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      try {
        const scopes: ApiKeyScope[] = submitted.data.access === 'Write' ? ['Read', 'Write'] : ['Read'];
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
    if (creating) reset({ data: { name: '', access: 'Read' } });
  });
</script>

<svelte:head>
  <title>API keys · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'API keys' }]} />

<main class={[sidebar.isMobile && 'pb-28']}>
  <HeaderBand
    title="API keys"
    count={keys.isSuccess ? list.length : undefined}
    countLabel={list.length === 1 ? 'key' : 'keys'}
    actions={sidebar.isMobile ? undefined : createButton}
  />

  <div class="flex w-full flex-col gap-5 px-4 py-6 md:px-6">
    <p role="status" class="sr-only">{announcement}</p>
    {#if keys.isError}
      <Panel title="Keys"><QueryError message={describeError(keys.error, 'Could not load the API keys')} onretry={() => keys.refetch()} inset /></Panel>
    {:else}
      <Panel title="Keys" flush>
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
        {:else if list.length === 0}
          <Empty.Root>
            <Empty.Header>
              <Empty.Media variant="icon"><KeyRoundIcon /></Empty.Media>
              <Empty.Title>No API keys yet</Empty.Title>
              <Empty.Description>A key lets another system read and change your jobs and candidates.</Empty.Description>
            </Empty.Header>
            <Empty.Content><Button variant="outline" size="lg" onclick={() => (creating = true)}><PlusIcon aria-hidden="true" />Create key</Button></Empty.Content>
          </Empty.Root>
        {:else}
          <ul class="md:hidden">
            {#each list as key (key.id)}
              <li class="flex min-w-0 items-start gap-2 border-b border-border-subtle px-4 py-3 last:border-0">
                <div class="flex min-w-0 flex-1 flex-col gap-1.5">
                  <span class="flex min-w-0 flex-wrap items-center gap-2">
                    {@render status(key)}
                    <span class="min-w-0 font-semibold break-words">{key.name}</span>
                  </span>
                  <span class="flex flex-wrap gap-1.5">
                    <Badge variant="muted" class="font-mono">{key.prefix}…</Badge>
                    <Badge variant="muted">{access(key.scopes)}</Badge>
                  </span>
                </div>
                {#if !key.revokedAt}{@render keyMenu(key)}{/if}
              </li>
            {/each}
          </ul>
          <div class="overflow-x-auto max-md:hidden">
            <Table.Root>
              <Table.Header>
                <Table.Row class="bg-muted hover:bg-muted">
                  <Table.Head class="px-4.5">Status</Table.Head>
                  <Table.Head>Name</Table.Head>
                  <Table.Head>Key</Table.Head>
                  <Table.Head>Access</Table.Head>
                  <Table.Head>Last used</Table.Head>
                  <Table.Head>Created</Table.Head>
                  <Table.Head class="w-12 pr-4.5"><span class="sr-only">Actions</span></Table.Head>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {#each list as key (key.id)}
                  <Table.Row class="h-12">
                    <Table.Cell class="px-4.5">{@render status(key)}</Table.Cell>
                    <Table.Cell class="w-full max-w-0"><TruncatedText text={key.name} class="font-semibold" /></Table.Cell>
                    <Table.Cell class="font-mono text-code text-muted-foreground">{key.prefix}…</Table.Cell>
                    <Table.Cell>{access(key.scopes)}</Table.Cell>
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
  </div>

  {#if sidebar.isMobile}
    <ActionBar>{@render createButton()}</ActionBar>
  {/if}
</main>

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
      <Dialog.Description>For the hiring API: jobs, applications, candidates and templates.</Dialog.Description>
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
      <fieldset class="flex flex-col gap-2">
        <legend class="mb-1 text-body font-medium">Access</legend>
        <label class="flex items-center gap-2 pointer-coarse:min-h-11">
          <input type="radio" name="access" value="Read" bind:group={$form.access} class="size-4 accent-primary" />Read only
        </label>
        <label class="flex items-center gap-2 pointer-coarse:min-h-11">
          <input type="radio" name="access" value="Write" bind:group={$form.access} class="size-4 accent-primary" />Read and write
        </label>
      </fieldset>
      <FailureText message={$errors._errors?.[0]} />
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Dialog.Close>
        <Button type="submit" size="lg" disabled={$submitting}><PlusIcon aria-hidden="true" />Create key</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
