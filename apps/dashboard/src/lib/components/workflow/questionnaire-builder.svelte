<script lang="ts" module>
  import type { AiUsePolicy, Question, QuestionSection } from '@screenhire/api-client';

  /** What the questionnaire's pop-out preview window draws (`/preview/form`): the questions, page by page. */
  export interface FormPreview {
    form: 'apply' | 'follow-up';
    questions: Question[];
    sections: QuestionSection[] | null;
    questionsPerPage: number;
    /** The apply form ends with the AI-use question under this policy; a follow-up has none. */
    aiUse?: AiUsePolicy;
  }
</script>

<script lang="ts">
  import LockIcon from '~icons/lucide/lock';
  import PencilIcon from '~icons/lucide/pencil';
  import RefreshCwIcon from '~icons/lucide/refresh-cw';
  import type { DefinitionError, HiringWorkflow, QuestionType } from '@screenhire/api-client';
  import {
    createGetOrganization,
    createListFormTemplates,
    getGetFormTemplateQueryOptions,
    getGetFormTemplateVersionQueryOptions,
  } from '@screenhire/api-client/svelte-query';
  import { DEFAULT_QUESTIONS_PER_PAGE } from '@screenhire/ui/questions/model';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { tick } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import EditPreviewLayout from '#lib/components/edit-preview-layout.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import FormBuilder from '#lib/components/form-builder.svelte';
  import Hint from '#lib/components/hint.svelte';
  import NewFormSheet from '#lib/components/new-form-sheet.svelte';
  import Panel from '#lib/components/panel.svelte';
  import PreviewFrame from '#lib/components/preview-frame.svelte';
  import PreviewPanel from '#lib/components/preview-panel.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { applyFormKinds, duplicateId, followUpKinds, type AnswerKindGroup } from '#lib/form-builder.js';
  import type { SectionDraft } from '#lib/form-sections.js';
  import { addLibraryForm, APPLY_FORM_ID, questionClashes, removeForm, replaceForm, timelineOf, useLibraryVersion, type LibraryVersion } from '#lib/forms-timeline.js';
  import { can } from '#lib/permissions.js';
  import { createPreviewSource } from '#lib/preview-window.svelte.js';
  import { describeError } from '#lib/query.js';
  import { templateHref } from '#lib/templates.js';
  import { allQuestions, answerIsRead, errorsAt, errorsUnder, followUpsOf, newQuestion, withQuestionType } from '#lib/workflow.js';
  import AddFormSheet from './add-form-sheet.svelte';
  import AiUseRow from './ai-use-row.svelte';
  import { labelClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import FormCards from './form-cards.svelte';
  import FormSendMoment from './form-send-moment.svelte';
  import QuestionList from './question-list.svelte';
  import QuestionSettings from './question-settings.svelte';
  import QuestionnairePreview from './questionnaire-preview.svelte';

  // The Forms tab (design system section 21; workflow-engine.md section 7, Forms): the hiring process's forms as cards
  // across the top, each its name, when it is sent and how many questions it asks, with "New form" last, and the chosen
  // one below, each in the same panels: its name with when it is sent, then its questions. The application form is the
  // template's own, always sent when they apply, and edits here in the form builder; Edit and Preview are one switch (A8), and it ends in the AI-use row, locked, because ScreenHire asks it on every application and the policy is
  // the organization's. Every other form is a copy of a library form: it says when it is sent, which writes the automation
  // behind it, and its questions read here and change in the library, where publishing a new version offers to update the
  // templates that use it. A form's card has its menu: Remove form takes the automations that exist to send it along, and
  // Replace with another form swaps its questions and keeps when it is sent. A job's own older forms, which copy no library
  // form, still edit here.
  let {
    workflow = $bindable(),
    errors,
    aiUse,
    automationsHref,
    automationHref,
    templateId,
    selected = $bindable(page.url.searchParams.get('form') ?? APPLY_FORM_ID),
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    /** The AI-use policy in force and where to change it (left out for someone who may not). Undefined while it loads. */
    aiUse?: { policy?: AiUsePolicy; href?: string };
    /** The address of the Automations tab, keeping the rest of the page's address (the version), for a knockout nobody reads (A11). */
    automationsHref: string;
    /** Where one automation opens on the Automations tab. */
    automationHref: (automationId: string) => string;
    /** The hiring template being edited, so a new library form comes back to it. */
    templateId?: string;
    /** `apply`, or the id of the form on show; the address's `?form=` when the tab opens, and a stage's form item after. */
    selected?: string;
  } = $props();

  const queryClient = useQueryClient();
  let mode = $state<'edit' | 'preview'>('edit');
  let root = $state<HTMLElement | null>(null);
  // What each confirmation says is read when it is asked, so its words hold while it closes over the changed workflow.
  let removing = $state(false);
  let removal = $state.raw<{ id: string; name: string; removed: string[] }>();
  let choosingReplacement = $state(false);
  let replacingId = $state('');
  let replacing = $state(false);
  let replacement = $state.raw<{ name: string; version: LibraryVersion; removed: string[] }>();
  let adding = $state(false);
  let building = $state(false);
  let failure = $state('');

  const followUps = $derived(followUpsOf(workflow));
  const index = $derived(followUps.findIndex((followUp) => followUp.id === selected));
  const current = $derived(index >= 0 ? followUps[index] : undefined);
  const linked = $derived(Boolean(current?.formTemplateId));
  const path = $derived(current ? `followUpQuestionnaires[${index}]` : 'questionnaire');
  const questions = $derived(current ? current.questions : workflow.questionnaire.questions);
  const sections = $derived((current ? current.sections : workflow.questionnaire.sections) ?? null);
  const groups: readonly AnswerKindGroup<QuestionType>[] = $derived(current ? followUpKinds : applyFormKinds);
  const forms = $derived(timelineOf(workflow));
  const organization = createGetOrganization();
  const questionsPerPage = $derived(organization.data?.data.questionsPerPage ?? DEFAULT_QUESTIONS_PER_PAGE);
  const nameOfForm = (id: string) => forms.find((form) => form.id === id)?.name ?? 'this form';
  const replacingForm = $derived(forms.find((form) => form.id === replacingId));
  const library = createListFormTemplates();
  const libraryForm = $derived(current?.formTemplateId ? library.data?.data.forms.find((form) => form.id === current.formTemplateId) : undefined);
  const newer = $derived(
    libraryForm?.latestPublishedVersion != null && current?.formVersion != null && libraryForm.latestPublishedVersion > current.formVersion
      ? libraryForm.latestPublishedVersion
      : null,
  );
  const mayBuild = $derived(can(page.data.user, 'EditHiringTemplates'));

  // A form that vanishes from under the selection (removed, or a draft reloaded) falls back to the application form.
  $effect(() => {
    if (selected !== APPLY_FORM_ID && !current) selected = APPLY_FORM_ID;
  });

  // Back from the library with a new form (`?addForm=`): its published version joins the workflow once.
  let addedFromAddress = false;
  $effect(() => {
    const formId = page.url.searchParams.get('addForm');
    if (!formId || addedFromAddress) return;
    addedFromAddress = true;
    const existing = followUpsOf(workflow).find((form) => form.formTemplateId === formId);
    if (existing) {
      selected = existing.id;
      return;
    }
    void (async () => {
      try {
        const latest = (await queryClient.fetchQuery(getGetFormTemplateQueryOptions(formId))).data.latestPublishedVersion;
        if (latest == null) throw new Error('The form is not published yet.');
        const version = (await queryClient.fetchQuery(getGetFormTemplateVersionQueryOptions(formId, latest))).data;
        addFrom({ formTemplateId: version.formTemplateId, version: version.version, kind: version.kind, content: version.content });
      } catch (error) {
        failure = describeError(error, 'Could not add the form');
      }
      const url = new URL(page.url.href);
      url.searchParams.delete('addForm');
      void goto(url, { replace: true, reset: false });
    })();
  });

  const source = createPreviewSource<FormPreview>(() => ({ form: current ? 'follow-up' : 'apply', questions, sections, questionsPerPage, aiUse: current ? undefined : aiUse?.policy }));

  function setQuestions(next: Question[]) {
    if (current) current.questions = next;
    else workflow.questionnaire.questions = next;
  }

  function setSections(next: SectionDraft[] | null | undefined) {
    if (current) current.sections = next ?? null;
    else workflow.questionnaire.sections = next ?? null;
  }

  function addFrom(version: LibraryVersion) {
    failure = '';
    const clashes = questionClashes(workflow, version.content);
    if (clashes.length > 0) {
      failure = `${version.content.name} asks ${clashes.map((label) => `"${label}"`).join(', ')}, which another form here asks already. Change one of them in the library first.`;
      return;
    }
    const added = addLibraryForm(workflow, version);
    workflow = added.workflow;
    selected = added.formId;
  }

  // The form keeps its id, so what sends it and what reads its answers still do.
  async function takeNewer() {
    if (!current?.formTemplateId || newer == null) return;
    failure = '';
    try {
      const version = (await queryClient.fetchQuery(getGetFormTemplateVersionQueryOptions(current.formTemplateId, newer))).data;
      const content = version.content;
      const clashes = questionClashes(workflow, content, current.id);
      if (clashes.length > 0) {
        failure = `Version ${newer} asks ${clashes.map((label) => `"${label}"`).join(', ')}, which another form here asks already.`;
        return;
      }
      workflow = useLibraryVersion(workflow, current.id, { formTemplateId: version.formTemplateId, version: version.version, kind: version.kind, content });
    } catch (error) {
      failure = describeError(error, 'Could not load the new version');
    }
  }

  // The form's automations go with it, so no step is left pointing at a form that is gone. Nothing changes until the
  // editor saves, so with no automation to take along there is nothing to confirm (section 10).
  function askToRemove(id: string) {
    const { removed } = removeForm(workflow, id);
    removal = { id, name: nameOfForm(id), removed };
    if (removed.length > 0) removing = true;
    else void remove(id);
  }

  // The card and its menu leave with the form, so the focus moves to the next card, or to the last one.
  async function remove(id: string) {
    const at = forms.findIndex((form) => form.id === id);
    workflow = removeForm(workflow, id).workflow;
    if (selected === id) selected = APPLY_FORM_ID;
    removing = false;
    await tick();
    const next = forms[at] ?? forms.at(-1);
    if (next) root?.querySelector<HTMLElement>(`[data-form="${CSS.escape(next.id)}"]`)?.focus();
  }

  function askToReplace(id: string) {
    failure = '';
    replacingId = id;
    choosingReplacement = true;
  }

  // As with removing, a confirmation only where automations go too: the ones that read answers the new form does not ask.
  function chooseReplacement(version: LibraryVersion) {
    const { removed } = replaceForm(workflow, replacingId, version);
    replacement = { name: nameOfForm(replacingId), version, removed };
    if (removed.length > 0) replacing = true;
    else replace(version);
  }

  function replace(version: LibraryVersion) {
    workflow = replaceForm(workflow, replacingId, version).workflow;
    selected = replacingId;
  }

  // A copy keeps everything but its id, which stays unique across the workflow, so `answers.{id}` names one question.
  function duplicate(question: Question): Question {
    const copy = $state.snapshot(question) as Question;
    return { ...copy, id: duplicateId(question.id, allQuestions(workflow).map((other) => other.id)) };
  }
</script>

{#snippet libraryVersion()}<Badge variant="muted">Library form, version {current?.formVersion}</Badge>{/snippet}

<!-- A problem with the forms as a whole, said in the surface below the switch (S3). -->
{#snippet followUpsErrors()}<FieldErrors id="follow-ups-errors" messages={errorsAt(errors, 'followUpQuestionnaires')} />{/snippet}

<div bind:this={root} class="flex min-w-0 flex-col gap-4">
  <FormCards {forms} bind:selected onadd={() => (adding = true)} onreplace={askToReplace} onremove={askToRemove} />
  <FailureText message={failure} />

  <!-- No toolbar: the switch starts the row, beside the form it shows, rather than pushed to the window's far edge (S7). -->
  <EditPreviewLayout bind:mode wide readOnly={linked}>
    {#snippet preview()}
      <PreviewPanel onpopout={() => source.open('/preview/form')}>
        <PreviewFrame>
          <QuestionnairePreview {questions} {sections} {questionsPerPage} followUp={!!current} aiUse={current ? undefined : aiUse?.policy} />
        </PreviewFrame>
      </PreviewPanel>
    {/snippet}

    {#snippet editor()}
      {@render followUpsErrors()}
      <!-- Every form has the same panel: its name and when it is sent, never on the page background (S3). -->
      {#if current}
        <Panel title={linked ? current.name : 'Form'} mark={linked ? libraryVersion : undefined}>
          <div class="flex max-w-form flex-col gap-4">
            {#if !linked}
              <!-- A job's own older form, which copies no library form, keeps its name here. Its id is detail: in the
                   label's tooltip (W6). -->
              <div class="flex flex-col gap-1.5">
                <div class={labelClass}>
                  <Hint content={`Form id: ${current.id}`}>
                    {#snippet children({ props })}<label {...props} for="follow-up-name" class="w-fit">Name</label>{/snippet}
                  </Hint>
                  <Input id="follow-up-name" bind:value={current.name} maxlength={80} aria-invalid={errorsAt(errors, `${path}.name`).length > 0 || undefined} />
                </div>
                <FieldErrors id="follow-up-name-errors" messages={[...errorsAt(errors, path), ...errorsAt(errors, `${path}.id`), ...errorsAt(errors, `${path}.name`)]} />
              </div>
            {:else}
              <FieldErrors id="follow-up-errors" messages={[...errorsAt(errors, path), ...errorsUnder(errors, `${path}.formTemplateId`).map((error) => error.message)]} />
            {/if}
            <FormSendMoment bind:workflow formId={current.id} {automationHref} />
            {#if current.kind === 'EqualityMonitoring'}
              <p class="text-muted-foreground">
                An equality monitoring form: its answers never show on a candidate's page, and Reports show only totals.
              </p>
            {/if}
          </div>
        </Panel>
      {:else}
        <Panel title="Application form">
          <dl class="flex flex-col gap-1">
            <dt class="font-semibold text-secondary-foreground">When it's sent</dt>
            <dd data-apply-moment class="flex min-h-9 flex-wrap items-center gap-x-2 gap-y-1 pointer-coarse:min-h-11">
              <LockIcon aria-hidden="true" class="size-4 shrink-0 text-muted-foreground" />When they apply
              <span class="text-muted-foreground">Fixed for the application form.</span>
            </dd>
          </dl>
        </Panel>
      {/if}
      {#if current && linked}
        <!-- A library form's questions change in the library, so every template that uses it can take the change. -->
        <Panel title="Questions" count={current.questions.length}>
          {#snippet actions()}
            {#if newer != null}
              <Button variant="outline" onclick={takeNewer}><RefreshCwIcon aria-hidden="true" />Use version {newer}</Button>
            {/if}
            <Button variant="outline" href={templateHref('forms', current.formTemplateId!)}><PencilIcon aria-hidden="true" />Edit</Button>
          {/snippet}
          <QuestionList questions={current.questions} />
        </Panel>
      {:else}
        {#key path}
          <FormBuilder
            bind:items={() => questions, setQuestions}
            withSections
            bind:sections={() => sections, setSections}
            sectionErrors={(sectionIndex) => errorsUnder(errors, `${path}.sections[${sectionIndex}]`).map((error) => error.message)}
            {groups}
            title="Questions"
            empty={current ? 'Add at least one question.' : 'No questions yet. Every form still asks for a resume.'}
            key={(question) => question.id}
            kindOf={(question) => question.type}
            withKind={withQuestionType}
            labelOf={(question) => question.label}
            setLabel={(question, value) => (question.label = value)}
            requiredOf={(question) => question.required ?? false}
            setRequired={(question, value) => (question.required = value)}
            help={{ label: 'Help text', get: (question) => question.helpText, set: (question, value) => (question.helpText = value) }}
            optionsOf={(question) => question.options ?? []}
            setOptions={(question, options) => (question.options = options)}
            newItem={(type) => newQuestion(workflow, type)}
            {duplicate}
            errorsFor={(_, questionIndex) => errorsUnder(errors, `${path}.questions[${questionIndex}]`).map((error) => error.message)}
            problems={[...(current ? [] : errorsAt(errors, path)), ...errorsAt(errors, `${path}.questions`), ...errorsAt(errors, `${path}.sections`)]}
          >
            {#snippet settings(question)}<QuestionSettings {question} unhandled={!answerIsRead(workflow, question.id)} {automationsHref} />{/snippet}
            {#snippet note()}{#if !current}<AiUseRow policy={aiUse?.policy} href={aiUse?.href} />{/if}{/snippet}
          </FormBuilder>
        {/key}
      {/if}
    {/snippet}
  </EditPreviewLayout>
</div>

<AddFormSheet bind:open={adding} {workflow} onadd={addFrom} onbuild={templateId && mayBuild ? () => (building = true) : undefined} />
{#if templateId}<NewFormSheet bind:open={building} returnTo={templateId} />{/if}

<AddFormSheet
  bind:open={choosingReplacement}
  {workflow}
  replacing={replacingForm && { id: replacingForm.id, name: replacingForm.name, when: replacingForm.whenShort }}
  onadd={chooseReplacement}
/>

{#if removal}
  {@const { id, name, removed } = removal}
  <ConfirmDialog
    bind:open={removing}
    title={`Remove ${name}?`}
    description={`This also removes ${removed.join(', ')}.`}
    confirmLabel="Remove form"
    failure="Could not remove the form"
    onconfirm={() => remove(id)}
  />
{/if}

{#if replacement}
  {@const { name, version, removed } = replacement}
  <ConfirmDialog
    bind:open={replacing}
    title={`Replace ${name} with ${version.content.name}?`}
    description={`This also removes ${removed.join(', ')}, because the new form does not ask what ${removed.length === 1 ? 'it reads' : 'they read'}.`}
    confirmLabel="Replace form"
    failure="Could not replace the form"
    onconfirm={async () => replace(version)}
  />
{/if}
