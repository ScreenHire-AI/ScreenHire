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
  import { useQueryClient } from '@tanstack/svelte-query';
  import { beforeNavigate, goto } from '$app/navigation';
  import { page } from '$app/state';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import WorkflowEditor from '#lib/components/workflow/workflow-editor.svelte';
  import { formatShortDate } from '#lib/format.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath, templatesPath } from '#lib/realtime.js';
  import { createWorkflowDraft } from '#lib/workflow-draft.svelte.js';
  import type { PageProps } from './$types';

  // One hiring template: its versions, and the builder over the chosen one. A draft is edited and saved here, then
  // published; a published version never changes, so editing one starts the next version as a draft.
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
  const isDraft = $derived(current?.status === 'Draft' && !detail?.isSystem);
  const hasDraft = $derived(versions.some((candidate) => candidate.status === 'Draft'));

  const save = createUpdateHiringTemplateVersion(() => ({ mutation: { onSettled: refresh } }));
  const publish = createPublishHiringTemplateVersion(() => ({ mutation: { onSettled: refresh } }));
  const nextVersion = createCreateHiringTemplateVersion(() => ({ mutation: { onSettled: refresh } }));
  const copy = createCreateHiringTemplate(() => ({ mutation: { onSettled: refresh } }));

  const draft = createWorkflowDraft();
  $effect(() => {
    if (current) draft.load(current.definition.workflow, `${current.id}:${current.status}`);
  });

  let status = $state('');
  let failure = $state('');
  const busy = $derived(save.isPending || publish.isPending || nextVersion.isPending || copy.isPending);

  // Unsaved work lives only on this page: ask before leaving it.
  beforeNavigate((navigation) => {
    if (draft.dirty && isDraft && !confirm('Leave without saving this draft?')) navigation.cancel();
  });

  async function saveDraft(): Promise<boolean> {
    if (!current || !draft.workflow) return false;
    failure = '';
    try {
      const response = await save.mutateAsync({
        templateId: params.templateId,
        version: current.version,
        data: { definition: { ...current.definition, workflow: draft.workflow } },
      });
      draft.saved(response.data.definition.workflow, `${response.data.id}:${response.data.status}`);
      status = `Saved version ${current.version}.`;
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
      status = `Published version ${current.version}. New jobs can use it, and jobs from older versions show an update.`;
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
</script>

<svelte:head>
  <title>{detail?.name ?? 'Template'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Templates', href: '/templates' }, { label: detail?.name ?? 'Template' }]} />

<main class="flex flex-col gap-3.5 px-5 pt-4.5 pb-9">
  {#if template.isError}
    {#if notFound}
      <h1 class="font-display text-[30px] font-bold tracking-[-0.02em]">Template not found</h1>
      <p class="text-muted-foreground">This template does not exist, or it belongs to another organization. <a href="/templates">Back to templates</a></p>
    {:else}
      <h1 class="font-display text-[30px] font-bold tracking-[-0.02em]">Template</h1>
      <QueryError message={describeError(template.error, 'Could not load this template')} onretry={() => template.refetch()} />
    {/if}
  {:else if !detail || !current || !draft.workflow}
    {#if version.isError}
      <QueryError message={describeError(version.error, 'Could not load this version')} onretry={() => version.refetch()} />
    {:else}
      <div aria-hidden="true" class="flex flex-col gap-2">
        <Skeleton class="h-9 w-80" />
        <Skeleton class="h-4 w-96" />
        <Skeleton class="mt-2 h-64 w-full" />
      </div>
      <p class="sr-only" role="status">Loading the template…</p>
    {/if}
  {:else}
    <section aria-label="Template" class="flex flex-wrap items-start justify-between gap-3">
      <div class="flex min-w-0 flex-col gap-1.5">
        <h1 class="font-display text-[30px] leading-[1.1] font-bold tracking-[-0.02em]">{detail.name}</h1>
        <p class="flex flex-wrap items-center gap-2 font-mono text-[11.5px] text-muted-foreground uppercase">
          <span>Version {current.version}</span>
          <Badge variant={current.status === 'Draft' ? 'secondary' : 'clear'}>{current.status}</Badge>
          {#if current.publishedAt}<span>Published {formatShortDate(current.publishedAt)}</span>{/if}
          {#if detail.isSystem}<span>ScreenHire template · read-only</span>{/if}
        </p>
      </div>
      <div class="flex flex-wrap gap-2">
        {#if detail.isSystem}
          <Button onclick={copyTemplate} disabled={busy}>Copy to customise</Button>
        {:else if isDraft}
          <Button variant="outline" onclick={saveDraft} disabled={busy || !draft.dirty}>Save draft</Button>
          <Button onclick={publishDraft} disabled={busy}>Publish version {current.version}</Button>
        {:else if !hasDraft}
          <Button onclick={startNextVersion} disabled={busy}>Edit as version {(detail.versions.at(-1)?.version ?? 0) + 1}</Button>
        {/if}
      </div>
    </section>

    <nav aria-label="Versions" class="flex flex-wrap gap-1.5">
      {#each versions as candidate (candidate.id)}
        <a
          href={`/templates/${detail.id}?version=${candidate.version}`}
          aria-current={candidate.version === current.version ? 'page' : undefined}
          class={[
            'rounded-md border px-2 py-0.5 font-mono text-[11.5px] no-underline',
            candidate.version === current.version ? 'border-primary bg-sidebar-accent font-semibold text-sidebar-accent-foreground' : 'border-border text-muted-foreground hover:bg-muted',
          ]}>v{candidate.version} · {candidate.status === 'Draft' ? 'draft' : 'published'}</a
        >
      {/each}
    </nav>

    <p role="status" class="text-[13px] empty:hidden">{status}</p>
    <p role="alert" class="text-[13px] font-semibold text-destructive empty:hidden">{failure}</p>
    {#if !isDraft && !detail.isSystem && hasDraft}
      <p class="text-[13px] text-muted-foreground">Published versions never change. Open the draft to edit.</p>
    {/if}

    <WorkflowEditor
      bind:workflow={() => draft.workflow!, (value) => (draft.workflow = value)}
      errors={draft.errors}
      checking={draft.checking}
      readonly={!isDraft}
    />
  {/if}
</main>
