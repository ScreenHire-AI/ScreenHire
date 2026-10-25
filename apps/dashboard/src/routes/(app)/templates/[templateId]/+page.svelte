<script lang="ts">
  import {
    ApiError,
    createCreateHiringTemplate,
    createCreateHiringTemplateVersion,
    createGetHiringTemplate,
    createArchiveHiringTemplate,
    createGetHiringTemplateVersion,
    createGetTemplateUsage,
    createListTeamMembers,
    createUnarchiveHiringTemplate,
    createPublishHiringTemplateVersion,
    createUpdateHiringTemplateVersion,
    getGetHiringTemplateHistoryQueryOptions,
  } from '@screenhire/api-client/svelte-query';
  import FolderArchiveIcon from '@lucide/svelte/icons/folder-archive';
  import ArchiveRestoreIcon from '@lucide/svelte/icons/archive-restore';
  import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
  import CheckIcon from '@lucide/svelte/icons/check';
  import CopyIcon from '@lucide/svelte/icons/copy';
  import LayersIcon from '@lucide/svelte/icons/layers';
  import PencilIcon from '@lucide/svelte/icons/pencil';
  import SaveIcon from '@lucide/svelte/icons/save';
  import SendIcon from '@lucide/svelte/icons/send';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import BuiltInBadge from '#lib/components/built-in-badge.svelte';
  import CountButton from '#lib/components/count-button.svelte';
  import Fact from '#lib/components/fact.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Hint from '#lib/components/hint.svelte';
  import HistorySheet from '#lib/components/history-sheet.svelte';
  import LastUpdated from '#lib/components/last-updated.svelte';
  import LeaveDialog from '#lib/components/leave-dialog.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import ValidationBadge from '#lib/components/workflow/validation-badge.svelte';
  import WorkflowEditor from '#lib/components/workflow/workflow-editor.svelte';
  import WorkflowSummary from '#lib/components/workflow/workflow-summary.svelte';
  import WorkflowTabs from '#lib/components/workflow/workflow-tabs.svelte';
  import { formatDateTime } from '#lib/dates.js';
  import { createHistoryPages } from '#lib/history-pages.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { can } from '#lib/permissions.js';
  import { jobsPath, templatesPath } from '#lib/realtime.js';
  import { copyName, maxNameLength, templatesHref, templateUsagePath, usageHref, usageLabel, usedByNouns } from '#lib/templates.js';
  import { cn } from '#lib/utils.js';
  import { createWorkflowDraft } from '#lib/workflow-draft.svelte.js';
  import type { PageProps } from './$types';

  // One hiring template: its versions, and the chosen one. Your own draft opens in the builder, to edit, save and
  // publish; anything else (a ScreenHire template, a published version, an archived template) reads as content. A
  // published version never changes, so editing one starts the next version as a draft. The header's facts are Used by
  // (the jobs started from it, T4), the version and Last updated with History (T6). A built-in says it reads only and
  // offers Copy template (E3); your own can be duplicated and archived, and an archived one restored. The Questionnaire
  // tab has the Edit / Preview switch (A8): it is the part of a template that someone else sees.
  let { params }: PageProps = $props();

  const queryClient = useQueryClient();
  const refresh = () =>
    Promise.all([invalidatePath(queryClient, templatesPath), invalidatePath(queryClient, jobsPath), invalidatePath(queryClient, templateUsagePath)]);
  const template = createGetHiringTemplate(() => params.templateId);
  const detail = $derived(template.data?.data);
  const requested = $derived(Number(page.url.searchParams.get('version')) || null);
  const versions = $derived(detail?.versions ?? []);
  const chosen = $derived(
    versions.find((version) => version.version === requested) ??
      versions.find((version) => version.status === 'Draft') ??
      versions.at(-1),
  );
  const version = createGetHiringTemplateVersion(
    () => params.templateId,
    () => chosen?.version ?? 0,
  );
  const current = $derived(version.data?.data);
  // The sidebar's own phone test (under md), where the primary action moves to the action bar.
  const sidebar = Sidebar.useSidebar();
  const mayEdit = $derived(can(page.data.user, 'EditHiringTemplates'));
  const isDraft = $derived(current?.status === 'Draft' && !detail?.isSystem && !detail?.archived && mayEdit);
  // Duplicate and Archive, for the team's own template and a role that may change templates.
  const ownActions = $derived(mayEdit && Boolean(detail) && !detail?.isSystem);
  const usage = createGetTemplateUsage();
  const use = $derived(usage.data?.data.hiring.find((entry) => entry.templateId === params.templateId));
  const draftVersion = $derived(versions.find((candidate) => candidate.status === 'Draft'));

  const save = createUpdateHiringTemplateVersion(() => ({ mutation: { onSettled: refresh } }));
  const publish = createPublishHiringTemplateVersion(() => ({ mutation: { onSettled: refresh } }));
  const nextVersion = createCreateHiringTemplateVersion(() => ({ mutation: { onSettled: refresh } }));
  const copy = createCreateHiringTemplate(() => ({ mutation: { onSettled: refresh } }));
  const archive = createArchiveHiringTemplate(() => ({ mutation: { onSettled: refresh } }));
  const unarchive = createUnarchiveHiringTemplate(() => ({ mutation: { onSettled: refresh } }));

  // Last updated and History (T6). A ScreenHire template says "by ScreenHire" and has no History: nobody can change it.
  const team = createListTeamMembers();
  const members = $derived(team.data?.data);
  const currentUserId = $derived(page.data.user?.id);
  let historyOpen = $state(false);
  const history = createHistoryPages(
    (before) => getGetHiringTemplateHistoryQueryOptions(params.templateId, { before }),
    () => historyOpen,
  );

  const draft = createWorkflowDraft();
  $effect(() => {
    if (current) draft.load(current.definition.workflow, `${current.id}:${current.status}`);
  });

  let tab = $state('pipeline');
  let notice = $state('');
  let failure = $state('');
  const busy = $derived(save.isPending || publish.isPending || nextVersion.isPending || copy.isPending || archive.isPending || unarchive.isPending);


  async function saveDraft(): Promise<boolean> {
    if (!current || !draft.workflow) return false;
    failure = '';
    if (!draft.dirty) {
      notice = 'No changes to save.';
      return true;
    }
    try {
      const response = await save.mutateAsync({
        templateId: params.templateId,
        version: current.version,
        data: { definition: { ...current.definition, workflow: draft.workflow } },
      });
      draft.saved(response.data.definition.workflow, `${response.data.id}:${response.data.status}`);
      notice = `Saved version ${current.version}.`;
      return true;
    } catch (error) {
      failure = draft.failed(error, 'Could not save the draft');
      return false;
    }
  }

  async function publishDraft() {
    if (!current) return;
    if (draft.dirty && !(await saveDraft())) return;
    failure = '';
    try {
      await publish.mutateAsync({ templateId: params.templateId, version: current.version });
      notice = `Published version ${current.version}.`;
    } catch (error) {
      failure = draft.failed(error, 'Could not publish');
    }
  }

  async function startNextVersion() {
    failure = '';
    try {
      const response = await nextVersion.mutateAsync({ templateId: params.templateId, data: {} });
      await goto(`/templates/${params.templateId}?version=${response.data.version}`);
    } catch (error) {
      failure = describeError(error, 'Could not start a new version');
    }
  }

  async function copyTemplate() {
    if (!detail || !current) return;
    failure = '';
    try {
      const response = await copy.mutateAsync({
        data: { name: copyName(detail.name, maxNameLength.hiring), description: detail.description, copyFromVersionId: current.id },
      });
      await goto(`/templates/${response.data.id}`);
    } catch (error) {
      failure = describeError(error, 'Could not copy the template');
    }
  }

  // Archive takes the template out of the pickers; Restore, on this page or under Archived, puts it back (section 10).
  async function setArchived(archived: boolean) {
    failure = '';
    notice = '';
    try {
      await (archived ? archive : unarchive).mutateAsync({ templateId: params.templateId });
      notice = archived ? 'Archived.' : 'Restored.';
    } catch (error) {
      failure = describeError(error, archived ? 'Could not archive the template' : 'Could not restore the template');
    }
  }

  const notFound = $derived(template.error instanceof ApiError && template.error.status === 404);
  const workArea = 'flex w-full flex-col gap-5 px-4 py-6 md:px-6';
</script>

<svelte:head>
  <title>{detail?.name ?? 'Template'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Templates', href: templatesHref('hiring') }, { label: detail?.name ?? 'Template' }]} back />

<main class={[sidebar.isMobile && detail && current && mayEdit && 'pb-28']}>
  {#if template.isError}
    <HeaderBand title={notFound ? 'Template not found' : 'Template'} />
    <div class={workArea}>
      {#if notFound}
        <Panel title="Details">
          <Empty.Root>
            <Empty.Header>
              <Empty.Title>This template does not exist, or it belongs to another organization.</Empty.Title>
            </Empty.Header>
            <Empty.Content><Button href="/templates" variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to templates</Button></Empty.Content>
          </Empty.Root>
        </Panel>
      {:else}
        <Panel title="Could not load the template"><QueryError message={describeError(template.error, 'Could not load this template')} onretry={() => template.refetch()} inset /></Panel>
      {/if}
    </div>
  {:else if !detail || !current || !draft.workflow}
    {#if version.isError}
      <HeaderBand title={detail?.name ?? 'Template'} />
      <div class={workArea}>
        <Panel title="Could not load the version"><QueryError message={describeError(version.error, 'Could not load this version')} onretry={() => version.refetch()} inset /></Panel>
      </div>
    {:else}
      <div aria-hidden="true">
        <div class="border-b border-border bg-card">
          <div class="flex flex-col gap-4 px-4 pt-5 md:px-6">
            <Skeleton class="h-9 w-80 max-w-full" />
            <Skeleton class="h-8 w-96 max-w-full" />
          </div>
        </div>
        <div class={workArea}>
          <Skeleton class="h-96 w-full rounded-panel" />
        </div>
      </div>
      <p class="sr-only" role="status">Loading the template…</p>
    {/if}
  {:else}
    {#snippet versionItems()}
      {#each versions.toReversed() as candidate (candidate.id)}
        <!-- A version is a place, so its item is a link (A5). -->
        <DropdownMenu.Item>
          {#snippet child({ props })}
            <a href={`/templates/${detail.id}?version=${candidate.version}`} {...props}>
              Version {candidate.version}, {candidate.status === 'Draft' ? 'draft' : 'published'}
              {#if candidate.version === current.version}<CheckIcon aria-hidden="true" class="ml-auto" /><span class="sr-only">(shown)</span>{/if}
            </a>
          {/snippet}
        </DropdownMenu.Item>
      {/each}
    {/snippet}

    {#snippet ownItems()}
      <DropdownMenu.Item disabled={busy} onSelect={copyTemplate}><CopyIcon aria-hidden="true" />Duplicate</DropdownMenu.Item>
      {#if !detail.archived}
        <DropdownMenu.Item disabled={busy} onSelect={() => setArchived(true)}><FolderArchiveIcon aria-hidden="true" />Archive</DropdownMenu.Item>
      {/if}
    {/snippet}

    {#snippet primaryAction()}
      {#if !mayEdit}
        <!-- A role that cannot change templates reads them only: no action. -->
      {:else if detail.isSystem}
        <Button size="lg" onclick={copyTemplate} disabled={busy}><CopyIcon aria-hidden="true" />Copy template</Button>
      {:else if detail.archived}
        <Button size="lg" onclick={() => setArchived(false)} disabled={busy}><ArchiveRestoreIcon aria-hidden="true" />Restore</Button>
      {:else if isDraft}
        <Button size="lg" onclick={publishDraft} disabled={busy}><SendIcon aria-hidden="true" />Publish version {current.version}</Button>
      {:else if draftVersion}
        <Button size="lg" href={`/templates/${detail.id}?version=${draftVersion.version}`}><PencilIcon aria-hidden="true" />Open the draft</Button>
      {:else}
        <Button size="lg" onclick={startNextVersion} disabled={busy}
          ><PencilIcon aria-hidden="true" />Edit as version {(versions.at(-1)?.version ?? 0) + 1}</Button
        >
      {/if}
    {/snippet}

    <Tabs.Root bind:value={tab}>
      <HeaderBand title={detail.name}>
        {#snippet status()}
          {#if detail.isSystem}<BuiltInBadge />{/if}
          {#if detail.archived}<StatusBadge status="archived" label="Archived" />{/if}
          {#if current.status === 'Draft'}<StatusBadge status="draft" label="Draft" />{/if}
          <ValidationBadge errors={draft.errors} checking={draft.checking} editable={isDraft} />
        {/snippet}
        {#snippet actions()}
          <p role="status" class="text-muted-foreground empty:hidden">{notice}</p>
          <FailureText message={failure} />
          {#if sidebar.isMobile}
            <!-- A phone has the primary action in the action bar and the rest in More (section 8). -->
            {#if isDraft || versions.length > 1 || ownActions}
              <MoreMenu label="More actions">
                {#if isDraft}<DropdownMenu.Item disabled={busy} onSelect={saveDraft}>Save draft</DropdownMenu.Item>{/if}
                {#if ownActions}{@render ownItems()}{/if}
                {#if versions.length > 1}
                  {#if isDraft || ownActions}<DropdownMenu.Separator />{/if}
                  <DropdownMenu.Label class="text-caption text-muted-foreground">Versions</DropdownMenu.Label>
                  {@render versionItems()}
                {/if}
              </MoreMenu>
            {/if}
          {:else}
            {#if ownActions}
              <MoreMenu label="More actions">{@render ownItems()}</MoreMenu>
            {/if}
            {#if versions.length > 1}
              <DropdownMenu.Root>
                <DropdownMenu.Trigger class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>
                  <LayersIcon aria-hidden="true" />Versions
                </DropdownMenu.Trigger>
                <DropdownMenu.Content align="end" class="w-56">{@render versionItems()}</DropdownMenu.Content>
              </DropdownMenu.Root>
            {/if}
            {#if isDraft}<Button size="lg" variant="outline" onclick={saveDraft} disabled={busy}><SaveIcon aria-hidden="true" />Save draft</Button>{/if}
            {@render primaryAction()}
          {/if}
        {/snippet}
        {#snippet facts()}
          <Fact label="Used by">
            {#if use}
              <CountButton
                items={use.usedBy.map((item) => ({ id: item.id, label: usageLabel(item), href: usageHref(item) }))}
                one={usedByNouns.hiring.one}
                other={usedByNouns.hiring.other}
                title="Used by"
                description={detail.name}
              />
            {:else}
              <Skeleton class="h-5 w-20" />
            {/if}
          </Fact>
          <Fact label="Version">
            {#if current.status !== 'Draft' && current.publishedAt}
              <Hint content={`Published ${formatDateTime(current.publishedAt)}`}>
                {#snippet children({ props })}<span {...props} class="tabular-nums">{current.version}</span>{/snippet}
              </Hint>
            {:else}
              <span class="tabular-nums">{current.version}</span>
            {/if}
          </Fact>
          <Fact label="Last updated">
            <LastUpdated value={detail.lastUpdated} {members} {currentUserId} onhistory={detail.isSystem ? undefined : () => (historyOpen = true)} />
          </Fact>
        {/snippet}
        {#snippet tabs()}<WorkflowTabs />{/snippet}
      </HeaderBand>
      {#if !detail.isSystem}
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
          note="Jobs keep the copy they started with; a change here reaches jobs created after it."
        />
      {/if}

      <div class={workArea}>
        {#if !isDraft && (detail.isSystem || detail.archived || !mayEdit)}
          <!-- Read only says why and what to do (E3). -->
          <p class="text-muted-foreground">
            {#if detail.isSystem}
              ScreenHire supplies this template, so it cannot be changed. Copy it to make one of your own.
            {:else if !mayEdit}
              Your role cannot change hiring templates. Ask an owner, admin or recruiter of your organization.
            {:else}
              This template is archived, so no new job can start from it. Restore it to use it or change it.
            {/if}
          </p>
        {/if}
        {#if isDraft}
          <WorkflowEditor bind:workflow={() => draft.workflow!, (value) => (draft.workflow = value)} errors={draft.errors} {tab} />
        {:else}
          <WorkflowSummary workflow={draft.workflow} errors={draft.errors} {tab} />
        {/if}
      </div>

      {#if sidebar.isMobile && mayEdit}
        <ActionBar>{@render primaryAction()}</ActionBar>
      {/if}
    </Tabs.Root>
  {/if}
</main>

<!-- Unsaved work lives only on this page: ask before leaving it. -->
<LeaveDialog unsaved={draft.dirty && isDraft} title="Leave without saving this draft?" />
