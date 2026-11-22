<script lang="ts">
  import FolderArchiveIcon from '~icons/lucide/folder-archive';
  import ArchiveRestoreIcon from '~icons/lucide/archive-restore';
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import CopyIcon from '~icons/lucide/copy';
  import SaveIcon from '~icons/lucide/save';
  import SendIcon from '~icons/lucide/send';
  import Undo2Icon from '~icons/lucide/undo-2';
  import type { DefinitionError, FormTemplateVersionDetail, Question, QuestionType } from '@screenhire/api-client';
  import {
    ApiError,
    createArchiveFormTemplate,
    createCreateFormTemplate,
    createCreateFormTemplateVersion,
    createGetFormTemplate,
    createGetFormTemplateVersion,
    createGetOrganization,
    createGetTemplateUsage,
    createListTeamMembers,
    createPublishFormTemplateVersion,
    createUnarchiveFormTemplate,
    createUpdateFormTemplateVersion,
    getGetFormTemplateHistoryQueryOptions,
  } from '@screenhire/api-client/svelte-query';
  import { DEFAULT_QUESTIONS_PER_PAGE } from '@screenhire/ui/questions/model';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import CountButton from '#lib/components/count-button.svelte';
  import EditPreviewLayout from '#lib/components/edit-preview-layout.svelte';
  import Fact from '#lib/components/fact.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import FormBuilder from '#lib/components/form-builder.svelte';
  import FormUpdateTemplatesDialog from '#lib/components/form-update-templates-dialog.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistorySheet from '#lib/components/history-sheet.svelte';
  import LastUpdated from '#lib/components/last-updated.svelte';
  import LeaveDialog from '#lib/components/leave-dialog.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import PreviewFrame from '#lib/components/preview-frame.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import QuestionSettings from '#lib/components/workflow/question-settings.svelte';
  import QuestionnairePreview from '#lib/components/workflow/questionnaire-preview.svelte';
  import { followUpKinds } from '#lib/form-builder.js';
  import { equalityKinds, formKindLabels, newFormQuestion, updateResultWords } from '#lib/form-library.js';
  import type { SectionDraft } from '#lib/form-sections.js';
  import { lastUpdateWords } from '#lib/history.js';
  import { createHistoryPages } from '#lib/history-pages.svelte.js';
  import { can } from '#lib/permissions.js';
  import { createPreviewSource } from '#lib/preview-window.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import {
    formPublishWords,
    formTemplatesPath,
    olderTemplates,
    templateHref,
    templatesHref,
    templateStatus,
    templateUsagePath,
    usageHref,
    usageLabel,
    usedByLabel,
    usedByNouns,
  } from '#lib/templates.js';
  import { errorsAt, errorsUnder, problemErrors, withQuestionType } from '#lib/workflow.js';
  import type { FormPreview } from '#lib/components/workflow/questionnaire-builder.svelte';
  import type { PageProps } from './$types';

  // One library form in its editor (workflow-engine.md section 7, Forms; design system sections 7 and 21, builder): the
  // header band with its Status, the live version, Used by (the hiring templates that copy it, T4) and Last updated with
  // History (T6); then the form builder and the candidate's page, one at a time behind the Edit / Preview switch (A8).
  // Forms are versioned like hiring templates: Save keeps a draft, and a published version never changes, so editing a
  // published form starts the next draft on its first save. Publish says how far it reaches (A11), and when hiring
  // templates use an older version it then offers to update them. An equality monitoring form asks only optional choices.
  let { params }: PageProps = $props();

  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const form = createGetFormTemplate(() => params.formId);
  const detail = $derived(form.data?.data);
  const draftVersion = $derived(detail?.versions.find((version) => version.status === 'Draft')?.version);
  const shownVersion = $derived(draftVersion ?? detail?.latestPublishedVersion ?? detail?.versions.at(-1)?.version);
  const version = createGetFormTemplateVersion(
    () => params.formId,
    () => shownVersion ?? 0,
    () => ({ query: { enabled: shownVersion != null } }),
  );
  const usage = createGetTemplateUsage();
  const organization = createGetOrganization();
  const questionsPerPage = $derived(organization.data?.data.questionsPerPage ?? DEFAULT_QUESTIONS_PER_PAGE);
  const refresh = () => Promise.all([invalidatePath(queryClient, formTemplatesPath), invalidatePath(queryClient, templateUsagePath)]);
  const createVersion = createCreateFormTemplateVersion(() => ({ mutation: { onSettled: refresh } }));
  const updateVersion = createUpdateFormTemplateVersion(() => ({ mutation: { onSettled: refresh } }));
  const publish = createPublishFormTemplateVersion(() => ({ mutation: { onSettled: refresh } }));
  const archive = createArchiveFormTemplate(() => ({ mutation: { onSettled: refresh } }));
  const unarchive = createUnarchiveFormTemplate(() => ({ mutation: { onSettled: refresh } }));
  const copy = createCreateFormTemplate(() => ({ mutation: { onSettled: refresh } }));
  const use = $derived(usage.data?.data.forms.find((entry) => entry.templateId === params.formId));
  const notFound = $derived(form.error instanceof ApiError && form.error.status === 404);
  const listHref = templatesHref('forms');
  const mayEdit = $derived(can(page.data.user, 'EditHiringTemplates'));
  const editable = $derived(mayEdit && Boolean(detail) && !detail?.archived);
  const equality = $derived(detail?.kind === 'EqualityMonitoring');
  // A hiring template that asked for a new form, to go back to with it.
  const returnTo = $derived(page.url.searchParams.get('for'));

  const team = createListTeamMembers();
  const members = $derived(team.data?.data);
  const currentUserId = $derived(page.data.user?.id);
  let historyOpen = $state(false);
  const history = createHistoryPages(
    (before) => getGetFormTemplateHistoryQueryOptions(params.formId, { before }),
    () => historyOpen,
  );

  // The draft lives on this page until Save; `baseline` is what was last loaded or saved.
  let name = $state('');
  let questions = $state<Question[]>([]);
  let sections = $state<SectionDraft[] | null>(null);
  let baseline = $state('');
  let loadedKey = '';
  const snapshot = $derived(JSON.stringify({ name, questions, sections }));
  const dirty = $derived(baseline !== '' && snapshot !== baseline);
  function load(from: FormTemplateVersionDetail) {
    name = from.content.name;
    questions = structuredClone($state.snapshot(from.content.questions) as Question[]);
    sections = from.content.sections ? (structuredClone($state.snapshot(from.content.sections)) as SectionDraft[]) : null;
    baseline = JSON.stringify({ name, questions, sections });
  }
  const loaded = $derived(version.data?.data);
  $effect(() => {
    if (loaded && loadedKey !== `${loaded.formTemplateId}:${loaded.version}`) {
      loadedKey = `${loaded.formTemplateId}:${loaded.version}`;
      load(loaded);
    }
  });

  let mode = $state<'edit' | 'preview'>('edit');
  const source = createPreviewSource<FormPreview>(() => ({ form: 'follow-up', questions, sections, questionsPerPage }));

  let errors = $state<DefinitionError[]>([]);
  let notice = $state('');
  let failure = $state('');
  const busy = $derived(createVersion.isPending || updateVersion.isPending || publish.isPending || archive.isPending || unarchive.isPending || copy.isPending);

  const content = () => ({ name: name.trim(), questions, sections: sections && sections.length > 0 ? sections : null });

  // A draft is changed in place; a published form starts its next version as a draft with these questions.
  async function save() {
    notice = '';
    failure = '';
    errors = [];
    try {
      const response =
        draftVersion != null
          ? await updateVersion.mutateAsync({ formId: params.formId, version: draftVersion, data: { content: content() } })
          : await createVersion.mutateAsync({ formId: params.formId, data: { content: content() } });
      loadedKey = `${response.data.formTemplateId}:${response.data.version}`;
      load(response.data);
      await form.refetch();
      notice = `Saved as a draft of version ${response.data.version}. Hiring templates get it once you publish.`;
    } catch (error) {
      mode = 'edit';
      if (error instanceof ApiError && error.status === 400) {
        errors = problemErrors(error.body).map((problem) => ({ ...problem, path: problem.path.replace(/^content\.?/, '') }));
        failure = 'The form has problems to fix; they show on its questions.';
      } else {
        failure = describeError(error, 'Could not save the form');
      }
    }
  }

  // Templates on an older version, after a publish, for the update dialog.
  let updating = $state(false);
  let published = $state<number | null>(null);
  const older = $derived(olderTemplates(use?.usedBy ?? [], published ?? detail?.latestPublishedVersion));

  async function publishDraft() {
    if (draftVersion == null) return;
    notice = '';
    failure = '';
    try {
      const response = await publish.mutateAsync({ formId: params.formId, version: draftVersion });
      published = response.data.version;
      await Promise.all([form.refetch(), usage.refetch()]);
      notice = `Published version ${response.data.version}.`;
      if (olderTemplates(use?.usedBy ?? [], response.data.version).length > 0) updating = true;
    } catch (error) {
      if (error instanceof ApiError && error.status === 400) {
        errors = problemErrors(error.body).map((problem) => ({ ...problem, path: problem.path.replace(/^content\.?/, '') }));
        failure = 'The form has problems to fix before it is published.';
      } else {
        failure = describeError(error, 'Could not publish the form');
      }
    }
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

  async function duplicate() {
    await run(
      async () => {
        const response = await copy.mutateAsync({ data: { copyFromFormId: params.formId } });
        baseline = snapshot;
        await goto(templateHref('forms', response.data.id));
      },
      '',
      'Could not copy the form',
    );
  }

  let discardChosen = $state(false);
  let confirmDiscard = $state(false);

  // A copy keeps everything but its id, which stays unique in the form.
  function duplicateQuestion(question: Question): Question {
    const copied = $state.snapshot(question) as Question;
    return { ...copied, id: newFormQuestion(detail?.kind ?? 'Standard', questions.map((other) => other.id)).id };
  }

  const nameErrors = $derived(errorsAt(errors, 'name').map((message) => ({ message })));
  const shownStatus = $derived(detail ? templateStatus({ archived: detail.archived, published: detail.latestPublishedVersion, draft: draftVersion }) : undefined);
  const publishNote = $derived(draftVersion != null ? formPublishWords(draftVersion, use?.usedBy ?? []) : '');
</script>

<svelte:head>
  <title>{detail?.name ?? 'Form'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Templates', href: listHref }, { label: detail?.name ?? 'Form' }]} back />

<main class={['flex flex-col', sidebar.isMobile && detail && mayEdit && 'pb-28']}>
  {#if form.isError && !detail}
    <HeaderBand title={notFound ? 'Form not found' : 'Form'} />
    <div class="flex w-full flex-col px-4 py-6 md:px-6">
      <Panel title={notFound ? 'Form not found' : 'Could not load the form'}>
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
          <QueryError message={describeError(form.error, 'Could not load the form')} onretry={() => form.refetch()} inset />
        {/if}
      </Panel>
    </div>
  {:else if !detail || !loaded}
    <div aria-hidden="true">
      <div class="border-b border-border bg-card">
        <div class="flex flex-col gap-4 px-4 py-5 md:px-6">
          <Skeleton class="h-9 w-80 max-w-full" />
          <Skeleton class="h-8 w-96 max-w-full" />
        </div>
      </div>
      <div class="flex w-full flex-col px-4 py-6 md:px-6"><Skeleton class="h-96 w-full max-w-form rounded-panel" /></div>
    </div>
    <p class="sr-only" role="status">Loading the form…</p>
  {:else}
    {#snippet primaryAction()}
      {#if detail.archived}
        <Button size="lg" onclick={() => run(() => unarchive.mutateAsync({ formId: params.formId }), 'Restored.', 'Could not restore the form')} disabled={busy}
          ><ArchiveRestoreIcon aria-hidden="true" />Restore</Button
        >
      {:else if dirty || draftVersion == null}
        <Button size="lg" onclick={save} disabled={busy || !dirty}><SaveIcon aria-hidden="true" />Save draft</Button>
      {:else}
        <Button size="lg" onclick={publishDraft} disabled={busy}><SendIcon aria-hidden="true" />Publish</Button>
      {/if}
    {/snippet}

    {#snippet details()}
      <!-- What publishing does, or why the form reads only (A11, E3), in one line under the title. -->
      <p>
        {#if !mayEdit}
          Ask an owner, admin or recruiter to change forms.
        {:else if detail.archived}
          Restore this form to use it or change it.
        {:else if draftVersion != null}
          {publishNote}
        {:else}
          Your changes start version {(detail.latestPublishedVersion ?? 0) + 1} as a draft.
        {/if}
      </p>
    {/snippet}

    <HeaderBand title={detail.name} {details}>
      {#snippet status()}
        {#if equality}<Badge variant="muted">{formKindLabels.EqualityMonitoring}</Badge>{/if}
      {/snippet}
      {#snippet actions()}
        <p role="status" class="text-muted-foreground empty:hidden">{notice}</p>
        <FailureText message={failure} />
        {#if returnTo && detail.latestPublishedVersion != null}
          <Button variant="outline" size="lg" href={`${templateHref('hiring', returnTo)}?tab=forms&addForm=${detail.id}`}>Use it in the template</Button>
        {/if}
        {#if mayEdit}
          <MoreMenu
            label="More actions"
            onclosed={() => {
              if (discardChosen) confirmDiscard = true;
              discardChosen = false;
            }}
          >
            <DropdownMenu.Item onSelect={duplicate}><CopyIcon aria-hidden="true" />Duplicate</DropdownMenu.Item>
            {#if !detail.archived}
              <DropdownMenu.Item onSelect={() => run(() => archive.mutateAsync({ formId: params.formId }), 'Archived.', 'Could not archive the form')}
                ><FolderArchiveIcon aria-hidden="true" />Archive</DropdownMenu.Item
              >
            {/if}
            {#if editable && dirty}
              <DropdownMenu.Separator />
              <DropdownMenu.Item variant="destructive" disabled={busy} onSelect={() => (discardChosen = true)}
                ><Undo2Icon aria-hidden="true" />Discard changes</DropdownMenu.Item
              >
            {/if}
          </MoreMenu>
          {#if !sidebar.isMobile}
            {@render primaryAction()}
          {/if}
        {/if}
      {/snippet}
      {#snippet facts()}
        {#if shownStatus}
          <Fact label="Status"><StatusBadge status={shownStatus.status} label={shownStatus.label} /></Fact>
        {/if}
        <Fact label="Live version">
          {#if detail.latestPublishedVersion != null}<span class="tabular-nums">Version {detail.latestPublishedVersion}</span>{:else}Not published yet{/if}
        </Fact>
        <Fact label="Used by">
          {#if use}
            <CountButton
              items={use.usedBy.map((item) => ({ id: item.id, label: usageLabel(item), href: usageHref(item) }))}
              one={usedByNouns.forms.one}
              other={usedByNouns.forms.other}
              empty={usedByLabel('forms', 0)}
              title="Used by"
              description={detail.name}
            />
          {:else}
            <Skeleton class="h-5 w-32" />
          {/if}
        </Fact>
        <Fact label={lastUpdateWords(detail.lastUpdated).label}>
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
      note="Hiring templates keep the version they copied until someone updates them."
    />

    <div class="flex w-full flex-col px-4 py-6 md:px-6">
      <EditPreviewLayout bind:mode wide readOnly={!editable} onpopout={() => source.open('/preview/form')}>
        {#snippet editor()}
          <Panel title="Details">
            <Field.Field data-invalid={nameErrors.length > 0 ? true : undefined}>
              <Field.Label for="form-name">Name</Field.Label>
              <Input
                id="form-name"
                maxlength={80}
                autocomplete="off"
                bind:value={name}
                readonly={!editable}
                aria-invalid={nameErrors.length > 0 ? 'true' : undefined}
                aria-describedby={nameErrors.length > 0 ? 'form-name-error' : undefined}
              />
              <Field.Error id="form-name-error" errors={nameErrors} />
            </Field.Field>
            {#if equality}
              <p class="mt-3 text-muted-foreground">
                Its answers never show on a candidate's page. Reports show only totals, and hide any total under 5.
              </p>
            {/if}
          </Panel>
          <FormBuilder
            bind:items={questions}
            withSections
            bind:sections
            sectionErrors={(sectionIndex) => errorsUnder(errors, `sections[${sectionIndex}]`).map((error) => error.message)}
            groups={equality ? equalityKinds : followUpKinds}
            title="Questions"
            empty="Add at least one question."
            key={(question) => question.id}
            kindOf={(question) => question.type}
            withKind={(question, type: QuestionType) => withQuestionType(question, type)}
            labelOf={(question) => question.label}
            setLabel={(question, value) => (question.label = value)}
            requiredOf={(question) => question.required ?? false}
            setRequired={(question, value) => (question.required = value)}
            help={{ label: 'Help text', get: (question) => question.helpText, set: (question, value) => (question.helpText = value) }}
            optionsOf={(question) => question.options ?? []}
            setOptions={(question, options) => (question.options = options)}
            newItem={(type) => newFormQuestion(detail.kind, questions.map((question) => question.id), type)}
            duplicate={duplicateQuestion}
            errorsFor={(_, questionIndex) => errorsUnder(errors, `questions[${questionIndex}]`).map((error) => error.message)}
            problems={[...errorsAt(errors, ''), ...errorsAt(errors, 'questions'), ...errorsAt(errors, 'sections')]}
          >
            {#snippet settings(question)}{#if !equality}<QuestionSettings {question} />{/if}{/snippet}
          </FormBuilder>
        {/snippet}
        {#snippet preview()}
          <Panel title="Preview">
            <PreviewFrame>
              <QuestionnairePreview {questions} {sections} {questionsPerPage} followUp />
            </PreviewFrame>
          </Panel>
        {/snippet}
      </EditPreviewLayout>
    </div>

    {#if sidebar.isMobile && mayEdit}
      <ActionBar>{@render primaryAction()}</ActionBar>
    {/if}

    <FormUpdateTemplatesDialog
      bind:open={updating}
      formId={detail.id}
      version={published ?? detail.latestPublishedVersion ?? 1}
      formName={detail.name}
      templates={older}
      ondone={(results) => (notice = updateResultWords(results))}
    />
  {/if}
</main>

<ConfirmDialog
  bind:open={confirmDiscard}
  title="Discard your changes?"
  description="Your unsaved changes to this form are lost."
  cancelLabel="Keep editing"
  confirmLabel="Discard changes"
  failure="Could not discard the changes"
  onconfirm={async () => {
    if (loaded) load(loaded);
    errors = [];
  }}
/>

<LeaveDialog unsaved={dirty && editable} title="Leave without saving this form?" />
