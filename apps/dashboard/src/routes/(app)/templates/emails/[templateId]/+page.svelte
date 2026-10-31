<script lang="ts">
  import FolderArchiveIcon from '@lucide/svelte/icons/folder-archive';
  import ArchiveRestoreIcon from '@lucide/svelte/icons/archive-restore';
  import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
  import CopyIcon from '@lucide/svelte/icons/copy';
  import RotateCcwIcon from '@lucide/svelte/icons/rotate-ccw';
  import SaveIcon from '@lucide/svelte/icons/save';
  import type { EmailPreview } from '@screenhire/api-client';
  import {
    ApiError,
    createArchiveEmailTemplate,
    createCreateEmailTemplate,
    createDeleteEmailTemplate,
    createGetEmailTemplate,
    createGetTemplateUsage,
    createListEmailTemplates,
    createListTeamMembers,
    createUnarchiveEmailTemplate,
    createUpdateEmailTemplate,
    getGetEmailTemplateHistoryQueryOptions,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import BuiltInBadge from '#lib/components/built-in-badge.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import CountButton from '#lib/components/count-button.svelte';
  import EmailTemplateForm from '#lib/components/email-template-form.svelte';
  import Fact from '#lib/components/fact.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistorySheet from '#lib/components/history-sheet.svelte';
  import LastUpdated from '#lib/components/last-updated.svelte';
  import LeaveDialog from '#lib/components/leave-dialog.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { emailTemplatesPath } from '#lib/email.js';
  import { createHistoryPages } from '#lib/history-pages.svelte.js';
  import { can } from '#lib/permissions.js';
  import { createPreviewSource } from '#lib/preview-window.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { copyName, maxNameLength, templateHref, templatesHref, templateUsagePath, usageHref, usageLabel, usedByLabel, usedByNouns } from '#lib/templates.js';
  import type { PageProps } from './$types';

  // One email template in its editor (design system sections 7 and 21, builder): the header band with Built in for
  // ScreenHire's own, Used by (the automations that send it, T4), Last sent and Last updated with History (T6), then the
  // Edit / Preview switch (A8) over the wording. Built-in emails stay editable in place, and one the team edited offers
  // Reset to original, which confirms (A2). The team's own can be duplicated and archived; an archived one reads only
  // until restored. Without the role to change email templates, the wording reads only and says why (E3).
  let { params }: PageProps = $props();

  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const template = createGetEmailTemplate(() => params.templateId);
  const templates = createListEmailTemplates();
  const usage = createGetTemplateUsage();
  const refresh = () => Promise.all([invalidatePath(queryClient, emailTemplatesPath), invalidatePath(queryClient, templateUsagePath)]);
  const update = createUpdateEmailTemplate(() => ({ mutation: { onSettled: refresh } }));
  const reset = createDeleteEmailTemplate(() => ({ mutation: { onSettled: refresh } }));
  const archive = createArchiveEmailTemplate(() => ({ mutation: { onSettled: refresh } }));
  const unarchive = createUnarchiveEmailTemplate(() => ({ mutation: { onSettled: refresh } }));
  const duplicate = createCreateEmailTemplate(() => ({ mutation: { onSettled: refresh } }));
  const detail = $derived(template.data?.data);
  const list = $derived(templates.data?.data);
  const use = $derived(usage.data?.data.emails.find((entry) => entry.templateId === params.templateId));
  const notFound = $derived(template.error instanceof ApiError && template.error.status === 404);
  const listHref = templatesHref('emails');
  const mayEdit = $derived(can(page.data.user, 'EditEmailTemplates'));
  const editable = $derived(mayEdit && !detail?.archived);
  const duplicable = $derived(Boolean(detail && !detail.rejectOnly && !detail.questionnaireOnly && !detail.invitationOnly));
  const formId = 'email-template-form';

  // Last updated and History (T6): who changed the wording, named as the team list names them.
  const team = createListTeamMembers();
  const members = $derived(team.data?.data);
  const currentUserId = $derived(page.data.user?.id);
  let historyOpen = $state(false);
  const history = createHistoryPages(
    (before) => getGetEmailTemplateHistoryQueryOptions(params.templateId, { before }),
    () => historyOpen,
  );

  // The pop-out preview window shows what Preview shows, and follows each edit (A8).
  let mode = $state<'edit' | 'preview'>('edit');
  let rendered = $state<{ preview: EmailPreview | undefined; failure: string | undefined }>({ preview: undefined, failure: undefined });
  let dirty = $state(false);
  const source = createPreviewSource(() => rendered);

  let notice = $state('');
  let failure = $state('');

  async function save(value: { name: string | null; subject: string; body: string }) {
    notice = '';
    await update.mutateAsync({ templateId: params.templateId, data: { subject: value.subject, body: value.body, name: value.name } });
    notice = 'Saved.';
  }

  async function run(action: () => Promise<unknown>, done: string, failed: string) {
    notice = '';
    failure = '';
    try {
      await action();
      notice = done;
    } catch (error) {
      failure = describeError(error, failed);
    }
  }

  async function duplicateTemplate() {
    if (!detail) return;
    await run(
      async () => {
        const response = await duplicate.mutateAsync({ data: { name: copyName(detail.name, maxNameLength.emails), subject: detail.subject, body: detail.body } });
        await goto(templateHref('emails', response.data.id));
      },
      '',
      'Could not duplicate the template',
    );
  }

  // The reset dialog opens once the More menu has closed, so focus can return to the menu's button.
  let confirmOpen = $state(false);
  let chosen = false;
  const onMenuClosed = () => {
    if (chosen) confirmOpen = true;
    chosen = false;
  };

  async function resetToOriginal() {
    await reset.mutateAsync({ templateId: params.templateId });
    notice = "Back to ScreenHire's wording.";
  }
</script>

<svelte:head>
  <title>{detail?.name ?? 'Email template'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Templates', href: listHref }, { label: detail?.name ?? 'Email template' }]} back />

<main class={['flex flex-col', sidebar.isMobile && detail && mayEdit && 'pb-28']}>
  {#if template.isError && !detail}
    <HeaderBand title={notFound ? 'Template not found' : 'Email template'} />
    <div class="flex w-full flex-col px-4 py-6 md:px-6">
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
    <HeaderBand title="Email template" />
    <div class="flex w-full flex-col px-4 py-6 md:px-6">
      <Panel title="Wording" class="max-w-form">
        <div aria-hidden="true" class="flex flex-col gap-4">{#each [0, 1, 2] as field (field)}<Skeleton class="h-14 w-full" />{/each}</div>
        <p class="sr-only" role="status">Loading the template…</p>
      </Panel>
    </div>
  {:else}
    {#snippet primaryAction()}
      {#if mayEdit && detail.archived}
        <Button size="lg" onclick={() => run(() => unarchive.mutateAsync({ templateId: params.templateId }), 'Restored.', 'Could not restore the template')}
          ><ArchiveRestoreIcon aria-hidden="true" />Restore</Button
        >
      {:else if editable}
        <Button type="submit" form={formId} size="lg" disabled={update.isPending}><SaveIcon aria-hidden="true" />Save</Button>
      {/if}
    {/snippet}

    {#snippet readOnlyReason()}
      <!-- Read only says why and what to do (E3), in one line under the title. -->
      {#if !mayEdit}
        <p>Ask an owner or admin to change email templates.</p>
      {:else if detail.archived}
        <p>Restore this template to use it or change it.</p>
      {/if}
    {/snippet}

    <HeaderBand title={detail.name} details={!mayEdit || detail.archived ? readOnlyReason : undefined}>
      {#snippet status()}
        {#if detail.builtIn}<BuiltInBadge />{/if}
        {#if detail.archived}<StatusBadge status="archived" label="Archived" />{/if}
      {/snippet}
      {#snippet actions()}
        <p role="status" class="text-muted-foreground empty:hidden">{notice}</p>
        <FailureText message={failure} />
        {#if mayEdit && (duplicable || !detail.builtIn || detail.edited)}
          <MoreMenu label="More actions" onclosed={onMenuClosed}>
            {#if duplicable}<DropdownMenu.Item onSelect={duplicateTemplate}><CopyIcon aria-hidden="true" />Duplicate</DropdownMenu.Item>{/if}
            {#if !detail.builtIn && !detail.archived}
              <DropdownMenu.Item onSelect={() => run(() => archive.mutateAsync({ templateId: params.templateId }), 'Archived.', 'Could not archive the template')}
                ><FolderArchiveIcon aria-hidden="true" />Archive</DropdownMenu.Item
              >
            {/if}
            {#if detail.builtIn && detail.edited}
              <DropdownMenu.Item variant="destructive" onSelect={() => (chosen = true)}><RotateCcwIcon aria-hidden="true" />Reset to original</DropdownMenu.Item>
            {/if}
          </MoreMenu>
        {/if}
        {#if !sidebar.isMobile}{@render primaryAction()}{/if}
      {/snippet}
      {#snippet facts()}
        <Fact label="Used by">
          {#if use}
            <CountButton
              items={use.usedBy.map((item) => ({ id: `${item.id}:${item.automationId}`, label: usageLabel(item), href: usageHref(item) }))}
              one={usedByNouns.emails.one}
              other={usedByNouns.emails.other}
              empty={usedByLabel('emails', 0)}
              title="Used by"
              description={detail.name}
            />
          {:else}
            <Skeleton class="h-5 w-24" />
          {/if}
        </Fact>
        <Fact label="Last sent">
          {#if use?.lastUsedAt}<RelativeTime at={use.lastUsedAt} />{:else if use}Never{:else}<Skeleton class="h-5 w-20" />{/if}
        </Fact>
        <Fact label="Last updated">
          <LastUpdated value={detail.lastUpdated} {members} {currentUserId} onhistory={() => (historyOpen = true)} />
        </Fact>
      {/snippet}
    </HeaderBand>
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
      empty={detail.builtIn ? "ScreenHire's wording. Your team's changes show here." : 'No changes yet.'}
    />

    <div class="flex w-full flex-col gap-4 px-4 py-6 md:px-6">
      <!-- A reset or a restore changes what the form starts from, so the form starts again. -->
      {#key `${detail.id}:${editable}:${detail.edited}`}
        <EmailTemplateForm
          template={detail}
          variables={list.variables}
          sendingEnabled={list.sendingEnabled}
          {editable}
          bind:mode
          onpopout={() => source.open('/preview/email')}
          bind:rendered
          bind:dirty
          {formId}
          failure="Could not save the template"
          onsave={save}
        />
      {/key}
    </div>

    {#if sidebar.isMobile && mayEdit}
      <ActionBar>{@render primaryAction()}</ActionBar>
    {/if}

    <ConfirmDialog
      bind:open={confirmOpen}
      title="Reset to the original?"
      description="Your team's wording is discarded, and this email goes out in ScreenHire's current wording from now on."
      cancelLabel="Keep ours"
      confirmLabel="Reset to original"
      failure="Could not reset the email"
      onconfirm={resetToOriginal}
    >
      {#snippet icon()}<RotateCcwIcon aria-hidden="true" />{/snippet}
    </ConfirmDialog>
  {/if}
</main>

<!-- Unsaved wording lives only on this page: ask before leaving it. -->
<LeaveDialog unsaved={dirty && editable} title="Leave without saving this template?" />
