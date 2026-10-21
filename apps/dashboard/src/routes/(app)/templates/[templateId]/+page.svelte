<script lang="ts">
  import {
    ApiError,
    createCreateHiringTemplate,
    createCreateHiringTemplateVersion,
    createGetHiringTemplate,
    createGetHiringTemplateVersion,
    createPublishHiringTemplateVersion,
    createUpdateHiringTemplateVersion,
  } from '@screenhire/api-client/svelte-query';
  import CheckIcon from '@lucide/svelte/icons/check';
  import CopyIcon from '@lucide/svelte/icons/copy';
  import HistoryIcon from '@lucide/svelte/icons/history';
  import PencilIcon from '@lucide/svelte/icons/pencil';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import LeaveDialog from '#lib/components/leave-dialog.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
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
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath, templatesPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';
  import { createWorkflowDraft } from '#lib/workflow-draft.svelte.js';
  import type { PageProps } from './$types';

  // One hiring template: its versions, and the chosen one. Your own draft opens in the builder, to edit, save and
  // publish; anything else (a ScreenHire template, a published version) reads as content. A published version never
  // changes, so editing one starts the next version as a draft.
  let { params }: PageProps = $props();

  const queryClient = useQueryClient();
  const refresh = () => Promise.all([invalidatePath(queryClient, templatesPath), invalidatePath(queryClient, jobsPath)]);
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
  const isDraft = $derived(current?.status === 'Draft' && !detail?.isSystem);
  const draftVersion = $derived(versions.find((candidate) => candidate.status === 'Draft'));

  const save = createUpdateHiringTemplateVersion(() => ({ mutation: { onSettled: refresh } }));
  const publish = createPublishHiringTemplateVersion(() => ({ mutation: { onSettled: refresh } }));
  const nextVersion = createCreateHiringTemplateVersion(() => ({ mutation: { onSettled: refresh } }));
  const copy = createCreateHiringTemplate(() => ({ mutation: { onSettled: refresh } }));

  const draft = createWorkflowDraft();
  $effect(() => {
    if (current) draft.load(current.definition.workflow, `${current.id}:${current.status}`);
  });

  let tab = $state('pipeline');
  let notice = $state('');
  let failure = $state('');
  const busy = $derived(save.isPending || publish.isPending || nextVersion.isPending || copy.isPending);


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
      const response = await copy.mutateAsync({ data: { name: `${detail.name} (copy)`, copyFromVersionId: current.id } });
      await goto(`/templates/${response.data.id}`);
    } catch (error) {
      failure = describeError(error, 'Could not copy the template');
    }
  }

  const notFound = $derived(template.error instanceof ApiError && template.error.status === 404);
  const workArea = 'mx-auto flex w-full max-w-page flex-col gap-5 px-4 py-6 md:px-6';
</script>

<svelte:head>
  <title>{detail?.name ?? 'Template'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Templates', href: '/templates' }, { label: detail?.name ?? 'Template' }]} back />

<main class={[sidebar.isMobile && detail && current && 'pb-28']}>
  {#if template.isError}
    <HeaderBand title={notFound ? 'Template not found' : 'Template'} />
    <div class={workArea}>
      {#if notFound}
        <Panel title="Details">
          <Empty.Root>
            <Empty.Header>
              <Empty.Title>This template does not exist, or it belongs to another organization.</Empty.Title>
            </Empty.Header>
            <Empty.Content><Button href="/templates" variant="outline" size="lg">Back to templates</Button></Empty.Content>
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
          <div class="mx-auto flex max-w-page flex-col gap-4 px-4 pt-5 md:px-6">
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
        <DropdownMenu.Item onSelect={() => goto(`/templates/${detail.id}?version=${candidate.version}`)}>
          Version {candidate.version}, {candidate.status === 'Draft' ? 'draft' : 'published'}
          {#if candidate.version === current.version}<CheckIcon aria-hidden="true" class="ml-auto" /><span class="sr-only">(shown)</span>{/if}
        </DropdownMenu.Item>
      {/each}
    {/snippet}

    {#snippet primaryAction()}
      {#if detail.isSystem}
        <Button size="lg" onclick={copyTemplate} disabled={busy}><CopyIcon aria-hidden="true" />Copy to customise</Button>
      {:else if isDraft}
        <Button size="lg" onclick={publishDraft} disabled={busy}>Publish version {current.version}</Button>
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
          {#if current.status === 'Draft'}<StatusBadge status="neutral" label="Draft" />{/if}
          <ValidationBadge errors={draft.errors} checking={draft.checking} editable={isDraft} />
          <span class="flex flex-wrap gap-1.5">
            <Badge variant="muted" class="tabular-nums">Version {current.version}</Badge>
            {#if current.status !== 'Draft' && current.publishedAt}
              <Badge variant="muted">Published&nbsp;<RelativeTime at={current.publishedAt} /></Badge>
            {/if}
            {#if detail.isSystem}<Badge variant="muted">ScreenHire template</Badge>{/if}
          </span>
        {/snippet}
        {#snippet actions()}
          <p role="status" class="text-muted-foreground empty:hidden">{notice}</p>
          <FailureText message={failure} />
          {#if sidebar.isMobile}
            <!-- A phone has the primary action in the action bar and the rest in More (section 8). -->
            {#if isDraft || versions.length > 1}
              <MoreMenu label="More actions">
                {#if isDraft}<DropdownMenu.Item disabled={busy} onSelect={saveDraft}>Save draft</DropdownMenu.Item>{/if}
                {#if versions.length > 1}
                  {#if isDraft}<DropdownMenu.Separator />{/if}
                  <DropdownMenu.Label class="text-caption text-muted-foreground">Versions</DropdownMenu.Label>
                  {@render versionItems()}
                {/if}
              </MoreMenu>
            {/if}
          {:else}
            {#if versions.length > 1}
              <DropdownMenu.Root>
                <DropdownMenu.Trigger class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>
                  <HistoryIcon aria-hidden="true" />Versions
                </DropdownMenu.Trigger>
                <DropdownMenu.Content align="end" class="w-56">{@render versionItems()}</DropdownMenu.Content>
              </DropdownMenu.Root>
            {/if}
            {#if isDraft}<Button size="lg" variant="outline" onclick={saveDraft} disabled={busy}>Save draft</Button>{/if}
            {@render primaryAction()}
          {/if}
        {/snippet}
        {#snippet tabs()}<WorkflowTabs />{/snippet}
      </HeaderBand>

      <div class={workArea}>
        {#if isDraft}
          <WorkflowEditor bind:workflow={() => draft.workflow!, (value) => (draft.workflow = value)} errors={draft.errors} {tab} />
        {:else}
          <WorkflowSummary workflow={draft.workflow} errors={draft.errors} {tab} />
        {/if}
      </div>

      {#if sidebar.isMobile}
        <ActionBar>{@render primaryAction()}</ActionBar>
      {/if}
    </Tabs.Root>
  {/if}
</main>

<!-- Unsaved work lives only on this page: ask before leaving it. -->
<LeaveDialog unsaved={draft.dirty && isDraft} title="Leave without saving this draft?" />
