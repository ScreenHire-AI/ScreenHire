<script lang="ts">
  import FolderArchiveIcon from '@lucide/svelte/icons/folder-archive';
  import ArchiveRestoreIcon from '@lucide/svelte/icons/archive-restore';
  import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
  import CopyIcon from '@lucide/svelte/icons/copy';
  import SaveIcon from '@lucide/svelte/icons/save';
  import type { DefinitionError, ScorecardQuestion, ScorecardTemplateDetail } from '@screenhire/api-client';
  import {
    ApiError,
    createArchiveScorecardTemplate,
    createCreateScorecardTemplate,
    createGetScorecardTemplate,
    createGetTemplateUsage,
    createListTeamMembers,
    createUnarchiveScorecardTemplate,
    createUpdateScorecardTemplate,
    getGetScorecardTemplateHistoryQueryOptions,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import BuiltInBadge from '#lib/components/built-in-badge.svelte';
  import CountButton from '#lib/components/count-button.svelte';
  import EditPreviewSwitch from '#lib/components/edit-preview-switch.svelte';
  import Fact from '#lib/components/fact.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistorySheet from '#lib/components/history-sheet.svelte';
  import LastUpdated from '#lib/components/last-updated.svelte';
  import LeaveDialog from '#lib/components/leave-dialog.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import ScorecardBuilder from '#lib/components/scorecard-builder.svelte';
  import ScorecardPreview from '#lib/components/scorecard-preview.svelte';
  import ScorecardQuestionList from '#lib/components/scorecard-question-list.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { createHistoryPages } from '#lib/history-pages.svelte.js';
  import { can } from '#lib/permissions.js';
  import { createPreviewSource } from '#lib/preview-window.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { isRatingKind } from '#lib/scorecards.js';
  import {
    copyName,
    maxNameLength,
    scorecardTemplatesPath,
    templateHref,
    templatesHref,
    templateUsagePath,
    usageHref,
    usageLabel,
    usedByNouns,
  } from '#lib/templates.js';
  import { problemErrors } from '#lib/workflow.js';
  import type { PageProps } from './$types';

  // One library scorecard in its editor (design system sections 7 and 22, builder): the header band with Used by (the
  // hiring templates whose stages name it, T4), its questions and rating questions, and Last updated with History (T6);
  // then the Edit / Preview switch (A8). Edit is the form builder with the rating kinds; Preview is the scorecard as a
  // reviewer fills it in, at its real size, and pops out into a window that follows the edits. A built-in reads only,
  // says why, and offers Copy scorecard (E3); so does an archived one, with Restore, and so does every scorecard for a
  // role that cannot change hiring templates. Jobs keep the copy they took, so an edit here reaches new jobs only.
  let { params }: PageProps = $props();

  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const template = createGetScorecardTemplate(() => params.templateId);
  const usage = createGetTemplateUsage();
  const refresh = () => Promise.all([invalidatePath(queryClient, scorecardTemplatesPath), invalidatePath(queryClient, templateUsagePath)]);
  const update = createUpdateScorecardTemplate(() => ({ mutation: { onSettled: refresh } }));
  const archive = createArchiveScorecardTemplate(() => ({ mutation: { onSettled: refresh } }));
  const unarchive = createUnarchiveScorecardTemplate(() => ({ mutation: { onSettled: refresh } }));
  const copy = createCreateScorecardTemplate(() => ({ mutation: { onSettled: refresh } }));
  const detail = $derived(template.data?.data);
  const use = $derived(usage.data?.data.scorecards.find((entry) => entry.templateId === params.templateId));
  const notFound = $derived(template.error instanceof ApiError && template.error.status === 404);
  const listHref = templatesHref('scorecards');
  const mayEdit = $derived(can(page.data.user, 'EditHiringTemplates'));
  const editable = $derived(mayEdit && Boolean(detail) && !detail?.builtIn && !detail?.archived);

  // Last updated and History (T6). A built-in has no History: nobody can change it.
  const team = createListTeamMembers();
  const members = $derived(team.data?.data);
  const currentUserId = $derived(page.data.user?.id);
  let historyOpen = $state(false);
  const history = createHistoryPages(
    (before) => getGetScorecardTemplateHistoryQueryOptions(params.templateId, { before }),
    () => historyOpen,
  );

  // The draft lives on this page until Save; `baseline` is what was last loaded or saved.
  let name = $state('');
  let questions = $state<ScorecardQuestion[]>([]);
  let baseline = $state('');
  let loadedKey = '';
  const snapshot = $derived(JSON.stringify({ name, questions }));
  const dirty = $derived(baseline !== '' && snapshot !== baseline);
  function load(from: ScorecardTemplateDetail) {
    name = from.name;
    questions = structuredClone($state.snapshot(from.questions) as ScorecardQuestion[]);
    baseline = JSON.stringify({ name, questions });
  }
  $effect(() => {
    if (detail && loadedKey !== detail.id) {
      loadedKey = detail.id;
      load(detail);
    }
  });

  let mode = $state<'edit' | 'preview'>('edit');
  const source = createPreviewSource(() => ({ questions }));
  const ratingCount = $derived(questions.filter((question) => isRatingKind(question.type)).length);

  let errors = $state<DefinitionError[]>([]);
  let notice = $state('');
  let failure = $state('');
  const busy = $derived(update.isPending || archive.isPending || unarchive.isPending || copy.isPending);

  async function save() {
    notice = '';
    failure = '';
    errors = [];
    try {
      const response = await update.mutateAsync({ templateId: params.templateId, data: { name: name.trim(), questions } });
      load(response.data);
      notice = 'Saved.';
    } catch (error) {
      mode = 'edit';
      if (error instanceof ApiError && error.status === 400) {
        errors = problemErrors(error.body);
        failure = errors.some((problem) => problem.path === 'name') ? '' : 'The scorecard has problems to fix; they show on its questions.';
      } else {
        failure = describeError(error, 'Could not save the scorecard');
      }
    }
  }

  function discard() {
    if (!detail) return;
    load(detail);
    errors = [];
    notice = '';
    failure = '';
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

  // Copy (a built-in's primary action) and Duplicate make "<name> (copy)" as the team's own, and open it.
  async function copyScorecard() {
    if (!detail) return;
    await run(
      async () => {
        const response = await copy.mutateAsync({
          data: { name: copyName(detail.name, maxNameLength.scorecards), questions: detail.questions, copiedFrom: detail.id },
        });
        baseline = snapshot;
        await goto(templateHref('scorecards', response.data.id));
      },
      '',
      'Could not copy the scorecard',
    );
  }

  const nameErrors = $derived(errors.filter((problem) => problem.path === 'name').map((problem) => ({ message: problem.message })));
</script>

<svelte:head>
  <title>{detail?.name ?? 'Scorecard'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Templates', href: listHref }, { label: detail?.name ?? 'Scorecard' }]} back />

<main class={['flex flex-col', sidebar.isMobile && detail && mayEdit && 'pb-28']}>
  {#if template.isError && !detail}
    <HeaderBand title={notFound ? 'Scorecard not found' : 'Scorecard'} />
    <div class="flex w-full flex-col px-4 py-6 md:px-6">
      <Panel title={notFound ? 'Scorecard not found' : 'Could not load the scorecard'}>
        {#if notFound}
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>It does not exist, or it belongs to another organization.</Empty.Description>
            </Empty.Header>
            <Empty.Content>
              <Button href={listHref} variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to templates</Button>
            </Empty.Content>
          </Empty.Root>
        {:else}
          <QueryError message={describeError(template.error, 'Could not load the scorecard')} onretry={() => template.refetch()} inset />
        {/if}
      </Panel>
    </div>
  {:else if !detail}
    <div aria-hidden="true">
      <div class="border-b border-border bg-card">
        <div class="flex flex-col gap-4 px-4 py-5 md:px-6">
          <Skeleton class="h-9 w-80 max-w-full" />
          <Skeleton class="h-8 w-96 max-w-full" />
        </div>
      </div>
      <div class="flex w-full flex-col px-4 py-6 md:px-6"><Skeleton class="h-96 w-full max-w-form rounded-panel" /></div>
    </div>
    <p class="sr-only" role="status">Loading the scorecard…</p>
  {:else}
    {#snippet primaryAction()}
      {#if detail.builtIn}
        <Button size="lg" onclick={copyScorecard} disabled={busy}><CopyIcon aria-hidden="true" />Copy scorecard</Button>
      {:else if detail.archived}
        <Button size="lg" onclick={() => run(() => unarchive.mutateAsync({ templateId: params.templateId }), 'Restored.', 'Could not restore the scorecard')} disabled={busy}
          ><ArchiveRestoreIcon aria-hidden="true" />Restore</Button
        >
      {:else}
        <Button size="lg" onclick={save} disabled={busy}><SaveIcon aria-hidden="true" />Save</Button>
      {/if}
    {/snippet}

    <HeaderBand title={detail.name}>
      {#snippet status()}
        {#if detail.builtIn}<BuiltInBadge />{/if}
        {#if detail.archived}<StatusBadge status="archived" label="Archived" />{/if}
      {/snippet}
      {#snippet actions()}
        <p role="status" class="text-muted-foreground empty:hidden">{notice}</p>
        <FailureText message={failure} />
        {#if mayEdit}
          {#if !detail.builtIn}
            <MoreMenu label="More actions">
              <DropdownMenu.Item onSelect={copyScorecard}><CopyIcon aria-hidden="true" />Duplicate</DropdownMenu.Item>
              {#if !detail.archived}
                <DropdownMenu.Item onSelect={() => run(() => archive.mutateAsync({ templateId: params.templateId }), 'Archived.', 'Could not archive the scorecard')}
                  ><FolderArchiveIcon aria-hidden="true" />Archive</DropdownMenu.Item
                >
              {/if}
            </MoreMenu>
          {/if}
          {#if !sidebar.isMobile}
            {#if editable && dirty}<Button size="lg" variant="outline" onclick={discard} disabled={busy}>Discard changes</Button>{/if}
            {@render primaryAction()}
          {/if}
        {/if}
      {/snippet}
      {#snippet facts()}
        <Fact label="Used by">
          {#if use}
            <CountButton
              items={use.usedBy.map((item) => ({ id: item.id, label: usageLabel(item), href: usageHref(item) }))}
              one={usedByNouns.scorecards.one}
              other={usedByNouns.scorecards.other}
              title="Used by"
              description={detail.name}
            />
          {:else}
            <Skeleton class="h-5 w-32" />
          {/if}
        </Fact>
        <Fact label="Rating questions"><span class="tabular-nums">{ratingCount}</span></Fact>
        <Fact label="Last updated">
          <LastUpdated value={detail.lastUpdated} {members} {currentUserId} onhistory={detail.builtIn ? undefined : () => (historyOpen = true)} />
        </Fact>
      {/snippet}
    </HeaderBand>
    {#if !detail.builtIn}
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
        note="Jobs keep the copy they took; a change here reaches jobs created after it."
      />
    {/if}

    <div class="flex w-full flex-col gap-4 px-4 py-6 md:px-6">
      <EditPreviewSwitch bind:mode onpopout={() => source.open('/preview/scorecard')} />
      {#if mode === 'preview'}
        <ScorecardPreview {questions} />
      {:else if editable}
        <div class="flex w-full max-w-form flex-col gap-5">
          <Panel title="Details">
            <Field.Field data-invalid={nameErrors.length > 0 ? true : undefined}>
              <Field.Label for="scorecard-name">Name</Field.Label>
              <Input
                id="scorecard-name"
                maxlength={maxNameLength.scorecards}
                autocomplete="off"
                bind:value={name}
                aria-invalid={nameErrors.length > 0 ? 'true' : undefined}
                aria-describedby={nameErrors.length > 0 ? 'scorecard-name-error' : undefined}
              />
              <Field.Error id="scorecard-name-error" errors={nameErrors} />
            </Field.Field>
          </Panel>
          <ScorecardBuilder bind:questions {errors} />
        </div>
      {:else}
        <Panel title="Questions" count={questions.length} class="max-w-form">
          <!-- Read only says why and what to do (E3). -->
          <p class="mb-3 text-muted-foreground">
            {#if detail.builtIn}
              ScreenHire supplies this scorecard, so it cannot be changed. Copy it to make one of your own.
            {:else if !mayEdit}
              Your role cannot change scorecards. Ask an owner, admin or recruiter of your organization.
            {:else}
              This scorecard is archived, so it is out of the pickers. Restore it to use it or change it.
            {/if}
          </p>
          <div class="flex flex-col gap-2"><ScorecardQuestionList {questions} /></div>
        </Panel>
      {/if}
    </div>

    {#if sidebar.isMobile && mayEdit}
      <ActionBar>{@render primaryAction()}</ActionBar>
    {/if}
  {/if}
</main>

<!-- Unsaved work lives only on this page: ask before leaving it. -->
<LeaveDialog unsaved={dirty && editable} title="Leave without saving this scorecard?" />
