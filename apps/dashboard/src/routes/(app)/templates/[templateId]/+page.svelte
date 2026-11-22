<script lang="ts">
  import {
    ApiError,
    createCreateHiringTemplate,
    createCreateHiringTemplateVersion,
    createGetHiringTemplate,
    createGetOrganizationVerificationSettings,
    createArchiveHiringTemplate,
    createGetHiringTemplateVersion,
    createGetTemplateUsage,
    createListTeamMembers,
    createUnarchiveHiringTemplate,
    createPublishHiringTemplateVersion,
    createUpdateHiringTemplateVersion,
    getGetHiringTemplateHistoryQueryOptions,
  } from '@screenhire/api-client/svelte-query';
  import FolderArchiveIcon from '~icons/lucide/folder-archive';
  import ArchiveRestoreIcon from '~icons/lucide/archive-restore';
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import CopyIcon from '~icons/lucide/copy';
  import LayersIcon from '~icons/lucide/layers';
  import PencilIcon from '~icons/lucide/pencil';
  import SaveIcon from '~icons/lucide/save';
  import SendIcon from '~icons/lucide/send';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import BuiltInBadge from '#lib/components/built-in-badge.svelte';
  import CountButton from '#lib/components/count-button.svelte';
  import Fact from '#lib/components/fact.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import LastUpdated from '#lib/components/last-updated.svelte';
  import LeaveDialog from '#lib/components/leave-dialog.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TemplateUpdateDialog from '#lib/components/template-update-dialog.svelte';
  import VersionsSheet, { type VersionsTab } from '#lib/components/versions-sheet.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import ValidationBadge from '#lib/components/workflow/validation-badge.svelte';
  import WorkflowEditor from '#lib/components/workflow/workflow-editor.svelte';
  import WorkflowSummary from '#lib/components/workflow/workflow-summary.svelte';
  import WorkflowTabs from '#lib/components/workflow/workflow-tabs.svelte';
  import { createHistoryPages } from '#lib/history-pages.svelte.js';
  import { can } from '#lib/permissions.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath, templatesPath } from '#lib/realtime.js';
  import {
    copyName,
    maxNameLength,
    olderJobs,
    olderJobsWords,
    publishWords,
    templatesHref,
    templateUsagePath,
    usageHref,
    usageLabel,
    usedByLabel,
    usedByNouns,
    usedByWords,
  } from '#lib/templates.js';
  import type { UsageItem } from '@screenhire/api-client';
  import { createWorkflowDraft } from '#lib/workflow-draft.svelte.js';
  import { createWorkflowTab } from '#lib/workflow-tab.svelte.js';
  import { lastUpdateWords } from '#lib/history.js';
  import type { PageProps } from './$types';

  // One hiring template: its versions, and the chosen one. Your own draft opens in the builder, to edit, save and
  // publish; anything else (a ScreenHire template, a published version, an archived template) reads as content. A
  // published version never changes, so editing one starts the next version as a draft. The header's facts are the shown
  // version's Status, the Live version new jobs get, Used by (the jobs started from it, with those left on an older
  // version, T4) and Last updated (T6). Versions and History are one sheet, with tabs Versions and All changes. Under the
  // title, a line says what publishing does: new jobs get the version, and the template's jobs keep theirs until someone
  // updates them, which Review updates does job by job. A built-in says it reads only and offers Copy template (E3); your
  // own can be duplicated and archived, and an archived one restored. The Questions tab has the Edit / Preview switch
  // (A8): it is the part of a template that someone else sees.
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
  // The AI-use row: the organization's policy, which a template no longer carries. Changing it is the Verification area's Checks tab,
  // so the link is left out for someone who may not (A3).
  const verification = createGetOrganizationVerificationSettings();
  const aiUse = $derived({
    policy: verification.data?.data.defaultAiUse,
    href: can(page.data.user, 'EditSettings') ? '/verification/checks' : undefined,
  });
  const isDraft = $derived(current?.status === 'Draft' && !detail?.isSystem && !detail?.archived && mayEdit);
  // Duplicate and Archive, for the team's own template and a role that may change templates.
  const ownActions = $derived(mayEdit && Boolean(detail) && !detail?.isSystem);
  const usage = createGetTemplateUsage();
  const use = $derived(usage.data?.data.hiring.find((entry) => entry.templateId === params.templateId));
  const draftVersion = $derived(versions.find((candidate) => candidate.status === 'Draft'));
  // The version new jobs get, and the jobs still on an older one, each with an update to review.
  const live = $derived(detail?.latestPublishedVersion ?? null);
  const jobs = $derived((use?.usedBy ?? []).filter((item) => item.kind === 'Job'));
  const older = $derived(olderJobs(jobs, live));
  const mayUpdateJobs = $derived(can(page.data.user, 'EditJobs'));
  let reviewOpen = $state(false);
  let updating = $state<UsageItem | null>(null);
  let updateOpen = $state(false);
  let updated = $state('');

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
  let versionsOpen = $state(false);
  let versionsTab = $state<VersionsTab>('versions');
  const history = createHistoryPages(
    (before) => getGetHiringTemplateHistoryQueryOptions(params.templateId, { before }),
    () => versionsOpen && versionsTab === 'changes',
  );
  function showVersions(tab: VersionsTab) {
    versionsTab = tab;
    versionsOpen = true;
  }
  // The phone's More menu opens Versions once the menu has closed, so the focus goes into the sheet.
  let versionsChosen = false;

  const draft = createWorkflowDraft();
  $effect(() => {
    if (current) draft.load(current.definition.workflow, `${current.id}:${current.status}`);
  });

  // The tab is in the address (`?tab=`), so a link reaches it and a reload keeps it.
  const workflowTab = createWorkflowTab();
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
    // The button is disabled while the draft has problems; this keeps any other caller from publishing one.
    if (!current || !draft.ready) return;
    if (draft.dirty && !(await saveDraft())) return;
    failure = '';
    try {
      await publish.mutateAsync({ templateId: params.templateId, version: current.version });
      notice = `Published version ${current.version}. Jobs created from now on start from it.`;
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
        <Panel label="Template"><QueryError message={describeError(template.error, 'Could not load this template')} onretry={() => template.refetch()} inset /></Panel>
      {/if}
    </div>
  {:else if !detail || !current || !draft.workflow}
    {#if version.isError}
      <HeaderBand title={detail?.name ?? 'Template'} />
      <div class={workArea}>
        <Panel label="Template version"><QueryError message={describeError(version.error, 'Could not load this version')} onretry={() => version.refetch()} inset /></Panel>
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
        <!-- A draft with problems saves; only Test and Publish wait for them, and the Fix button beside the title says why. -->
        <!-- It goes off and on with every edit, so it does not fade: a half-faded button reads as a contrast failure to a checker that looks mid-change. -->
        <Button size="lg" class="transition-none" onclick={publishDraft} disabled={busy || !draft.ready}><SendIcon aria-hidden="true" />Publish</Button>
      {:else if draftVersion}
        <Button size="lg" href={`/templates/${detail.id}?version=${draftVersion.version}`}><PencilIcon aria-hidden="true" />Open the draft</Button>
      {:else}
        <Button size="lg" onclick={startNextVersion} disabled={busy}
          ><PencilIcon aria-hidden="true" />Edit as version {(versions.at(-1)?.version ?? 0) + 1}</Button
        >
      {/if}
    {/snippet}

    {#snippet headerLine()}
      <!-- One line under the title (S8): why it reads only, what publishing the draft does, and the jobs on an older
           version with Review updates. -->
      {#if !isDraft && (detail.isSystem || detail.archived || !mayEdit)}{@render readOnlyReason()}{/if}
      {#if isDraft}<p data-publish-line>{publishWords(current.version, jobs)}</p>{/if}
      {#if older.length > 0 && !detail.isSystem && !detail.archived}
        <p class="flex flex-wrap items-baseline gap-x-2">
          <span>{olderJobsWords(older.length)}</span>
          <!-- It opens a sheet, so it is an outline button, not underlined text (A5, A6). -->
          <Button variant="outline" size="sm" aria-haspopup="dialog" onclick={() => (reviewOpen = true)}>Review updates</Button>
        </p>
      {/if}
    {/snippet}

    {#snippet readOnlyReason()}
      <!-- Read only says why and what to do (E3), in one line under the title. -->
      <p>
        {#if detail.isSystem}
          Copy this ScreenHire template to change it.
        {:else if !mayEdit}
          Ask an owner, admin or recruiter to change hiring templates.
        {:else}
          Restore this template to start jobs from it or change it.
        {/if}
      </p>
    {/snippet}

    <Tabs.Root bind:value={workflowTab.value}>
      <HeaderBand
        title={detail.name}
        details={isDraft || detail.isSystem || detail.archived || !mayEdit || older.length > 0 ? headerLine : undefined}
      >
        {#snippet status()}
          {#if detail.isSystem}<BuiltInBadge />{/if}
          {#if detail.archived}<StatusBadge status="archived" label="Archived" />{/if}
          <ValidationBadge errors={draft.errors} checking={draft.checking} editable={isDraft} fix={workflowTab.value !== 'test'} />
        {/snippet}
        {#snippet actions()}
          <p role="status" class="text-muted-foreground empty:hidden">{notice}</p>
          <FailureText message={failure} />
          {#if sidebar.isMobile}
            <!-- A phone has the primary action in the action bar and the rest in More (section 8). -->
            {#if isDraft || versions.length > 1 || ownActions}
              <MoreMenu
                label="More actions"
                onclosed={() => {
                  if (versionsChosen) showVersions('versions');
                  versionsChosen = false;
                }}
              >
                {#if isDraft}<DropdownMenu.Item disabled={busy} onSelect={saveDraft}>Save draft</DropdownMenu.Item>{/if}
                {#if ownActions}{@render ownItems()}{/if}
                {#if versions.length > 1}
                  {#if isDraft || ownActions}<DropdownMenu.Separator />{/if}
                  <DropdownMenu.Item onSelect={() => (versionsChosen = true)}><LayersIcon aria-hidden="true" />Versions</DropdownMenu.Item>
                {/if}
              </MoreMenu>
            {/if}
          {:else}
            {#if ownActions}
              <MoreMenu label="More actions">{@render ownItems()}</MoreMenu>
            {/if}
            {#if versions.length > 1}
              <Button variant="outline" size="lg" aria-haspopup="dialog" onclick={() => showVersions('versions')}><LayersIcon aria-hidden="true" />Versions</Button>
            {/if}
            {#if isDraft}<Button size="lg" variant="outline" onclick={saveDraft} disabled={busy}><SaveIcon aria-hidden="true" />Save draft</Button>{/if}
            {@render primaryAction()}
          {/if}
        {/snippet}
        {#snippet facts()}
          <!-- The shown version's state and the version new jobs get are two facts (W6, W7). -->
          <Fact label="Status">
            {#if current.status === 'Draft'}
              <!-- The longest status: on a phone's two-column facts it wraps rather than clipping its last word. -->
              <StatusBadge status="draft" label="Draft, unpublished changes" class="h-auto min-h-6 max-w-full whitespace-normal" />
            {:else if live !== null && current.version !== live}
              <!-- An earlier version, read from Versions: the fact says which, since the live one is the next fact. -->
              <StatusBadge status="published" label={`Published, version ${current.version}`} />
            {:else}
              <StatusBadge status="published" label="Published" />
            {/if}
          </Fact>
          <Fact label="Live version">
            {#if live}<span class="tabular-nums">Version {live}</span>{:else}Not published yet{/if}
          </Fact>
          <Fact label="Used by">
            {#if use}
              <CountButton
                items={use.usedBy.map((item) => ({ id: item.id, label: usageLabel(item), href: usageHref(item) }))}
                one={usedByNouns.hiring.one}
                other={usedByNouns.hiring.other}
                empty={usedByLabel('hiring', 0)}
                text={usedByWords('hiring', use.usedBy, live)}
                link
                title="Used by"
                description={detail.name}
              />
            {:else}
              <Skeleton class="h-5 w-20" />
            {/if}
          </Fact>
          <Fact label={lastUpdateWords(detail.lastUpdated).label}>
            <LastUpdated value={detail.lastUpdated} {members} {currentUserId} onhistory={detail.isSystem ? undefined : () => showVersions('changes')} />
          </Fact>
        {/snippet}
        {#snippet tabs()}<WorkflowTabs />{/snippet}
      </HeaderBand>
      <!-- A ScreenHire template has no History, so its sheet holds only Versions. -->
      <VersionsSheet
        bind:open={versionsOpen}
        bind:tab={versionsTab}
        name={detail.name}
        {versions}
        shown={current.version}
        {live}
        hrefOf={(number) => `/templates/${detail.id}?version=${number}`}
        changes={!detail.isSystem}
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

      <div class={workArea}>
        {#if isDraft}
          <WorkflowEditor
            bind:workflow={() => draft.workflow!, (value) => (draft.workflow = value)}
            errors={draft.errors}
            checking={draft.checking}
            bind:tab={workflowTab.value}
            hrefOf={(tab) => workflowTab.href(tab)}
            {aiUse}
            templateId={params.templateId}
          />
        {:else}
          <WorkflowSummary workflow={draft.workflow} errors={draft.errors} tab={workflowTab.value} hrefOf={(tab) => workflowTab.href(tab)} {aiUse} />
        {/if}
      </div>

      {#if sidebar.isMobile && mayEdit}
        <ActionBar>{@render primaryAction()}</ActionBar>
      {/if}
    </Tabs.Root>

    <!-- Review updates: each job on an older version, with what changes (its pipeline page) and Update, which asks with
         the differences first, because it replaces that job's own changes too. -->
    <Sheet.Root bind:open={reviewOpen}>
      <Sheet.Content side="right" class="w-full gap-0 overflow-y-auto data-[side=right]:sm:max-w-md">
        <Sheet.Header class="border-b border-border-subtle">
          <Sheet.Title>Review updates</Sheet.Title>
          <Sheet.Description>New jobs get version {live}. These jobs keep theirs until you update them.</Sheet.Description>
        </Sheet.Header>
        {#if older.length === 0}
          <p class="p-4 text-muted-foreground">Every job is on version {live}.</p>
        {:else}
          <ul aria-label="Jobs on an older version" class="flex flex-col">
            {#each older as item (item.id)}
              <li class="flex flex-col gap-2 border-b border-border-subtle px-4 py-3 last:border-b-0">
                <div class="flex min-w-0 flex-wrap items-baseline justify-between gap-x-3">
                  <a href={`/jobs/${item.id}`} class="min-w-0 font-semibold break-words underline-offset-2 hover:underline">{item.name}</a>
                  <span class="text-caption text-muted-foreground tabular-nums">On version {item.templateVersion}</span>
                </div>
                <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
                  <a
                    href={`/jobs/${item.id}/workflow?review=update`}
                    class="underline underline-offset-2 pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center"
                    >See what changes{' '}<span class="sr-only">for {item.name}</span></a
                  >
                  {#if mayUpdateJobs}
                    <Button
                      variant="outline"
                      aria-haspopup="dialog"
                      onclick={() => {
                        updating = item;
                        updateOpen = true;
                      }}>Update{' '}<span class="sr-only">{item.name}</span></Button
                    >
                  {/if}
                </div>
              </li>
            {/each}
          </ul>
        {/if}
        <p role="status" class="border-t border-border-subtle px-4 py-3 text-muted-foreground empty:hidden">{updated}</p>
      </Sheet.Content>
    </Sheet.Root>
    {#if updating}
      <TemplateUpdateDialog
        bind:open={updateOpen}
        jobId={updating.id}
        jobTitle={updating.name}
        templateName={detail.name}
        expectedWorkflowVersion={updating.workflowVersion ?? 1}
        onapplied={(job, version) => {
          updated = `Updated ${job.title} to version ${version}.`;
          void refresh();
        }}
      />
    {/if}
  {/if}
</main>

<!-- Unsaved work lives only on this page: ask before leaving it. -->
<LeaveDialog unsaved={draft.dirty && isDraft} title="Leave without saving this draft?" />
