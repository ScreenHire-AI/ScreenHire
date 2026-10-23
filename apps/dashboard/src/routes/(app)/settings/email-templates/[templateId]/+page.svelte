<script lang="ts">
  import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
  import RotateCcwIcon from '@lucide/svelte/icons/rotate-ccw';
  import Trash2Icon from '@lucide/svelte/icons/trash-2';
  import {
    ApiError,
    createDeleteEmailTemplate,
    createGetEmailTemplate,
    createListEmailTemplates,
    createUpdateEmailTemplate,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import EmailTemplateForm from '#lib/components/email-template-form.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { emailTemplatesPath } from '#lib/email.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import type { PageProps } from './$types';

  // One email template's wording, on the form template. The organization's own can be deleted, unless an automation
  // sends it; a built-in one it has reworded can go back to ScreenHire's wording. Both confirm (A2).
  let { params }: PageProps = $props();

  const queryClient = useQueryClient();
  const template = createGetEmailTemplate(() => params.templateId);
  const templates = createListEmailTemplates();
  const invalidate = () => invalidatePath(queryClient, emailTemplatesPath);
  const update = createUpdateEmailTemplate(() => ({ mutation: { onSettled: invalidate } }));
  const remove = createDeleteEmailTemplate(() => ({ mutation: { onSettled: invalidate } }));
  const detail = $derived(template.data?.data);
  const list = $derived(templates.data?.data);
  const notFound = $derived(template.error instanceof ApiError && template.error.status === 404);
  const listHref = '/settings/email-templates';

  async function save(value: { name: string | null; subject: string; body: string }) {
    await update.mutateAsync({ templateId: params.templateId, data: { subject: value.subject, body: value.body, name: value.name } });
    await goto(listHref);
  }

  // The dialog opens once the More menu has closed, so focus can return to the menu's button.
  let confirmOpen = $state(false);
  let chosen = false;
  const onMenuClosed = () => {
    if (chosen) confirmOpen = true;
    chosen = false;
  };

  async function discard() {
    await remove.mutateAsync({ templateId: params.templateId });
    await goto(listHref);
  }
</script>

<svelte:head>
  <title>{detail?.name ?? 'Email template'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Email templates', href: listHref }, { label: detail?.name ?? 'Template' }]} back />

<main class="flex flex-col">
  {#if template.isError && !detail}
    <HeaderBand form title={notFound ? 'Template not found' : 'Email template'} />
    <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
      <Panel title={notFound ? 'Template not found' : 'Could not load the template'}>
        {#if notFound}
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>It was deleted, or it belongs to another organization.</Empty.Description>
            </Empty.Header>
            <Empty.Content>
              <Button href={listHref} variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to templates</Button>
            </Empty.Content>
          </Empty.Root>
        {:else}
          <QueryError message={describeError(template.error, 'Could not load the template')} onretry={() => template.refetch()} inset />
        {/if}
      </Panel>
    </div>
  {:else if !detail || !list}
    <HeaderBand form title="Email template" />
    <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
      <Panel title="Wording">
        <div aria-hidden="true" class="flex flex-col gap-4">{#each [0, 1, 2] as field (field)}<Skeleton class="h-14 w-full" />{/each}</div>
        <p class="sr-only" role="status">Loading the template…</p>
      </Panel>
    </div>
  {:else}
    <HeaderBand form title={detail.name}>
      {#snippet actions()}
        {#if !detail.builtIn || detail.edited}
          <MoreMenu label="More actions" onclosed={onMenuClosed}>
            {#if detail.builtIn}
              <DropdownMenu.Item variant="destructive" onSelect={() => (chosen = true)}><RotateCcwIcon aria-hidden="true" />Restore the original wording</DropdownMenu.Item>
            {:else}
              <DropdownMenu.Item variant="destructive" onSelect={() => (chosen = true)}><Trash2Icon aria-hidden="true" />Delete template</DropdownMenu.Item>
            {/if}
          </MoreMenu>
        {/if}
      {/snippet}
    </HeaderBand>
    <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
      {#key detail.id}
        <EmailTemplateForm
          template={detail}
          variables={list.variables}
          sendingEnabled={list.sendingEnabled}
          submitLabel="Save"
          cancelHref={listHref}
          failure="Could not save the template"
          onsave={save}
        />
      {/key}
    </div>

    {#if detail.builtIn}
      <ConfirmDialog
        bind:open={confirmOpen}
        title="Restore the original wording?"
        description="Your wording is discarded, and this email goes out in ScreenHire's wording from now on."
        cancelLabel="Keep mine"
        confirmLabel="Restore"
        failure="Could not restore the wording"
        onconfirm={discard}
      >
        {#snippet icon()}<RotateCcwIcon aria-hidden="true" />{/snippet}
      </ConfirmDialog>
    {:else}
      <ConfirmDialog
        bind:open={confirmOpen}
        title="Delete this template?"
        description="It cannot be undone. Emails already sent stay in each candidate's activity."
        cancelLabel="Keep it"
        confirmLabel="Delete template"
        failure="Could not delete the template"
        onconfirm={discard}
      >
        {#snippet icon()}<Trash2Icon aria-hidden="true" />{/snippet}
      </ConfirmDialog>
    {/if}
  {/if}
</main>
