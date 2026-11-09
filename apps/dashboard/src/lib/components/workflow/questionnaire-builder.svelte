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
  import type { DefinitionError, HiringWorkflow, QuestionType } from '@screenhire/api-client';
  import { createGetOrganization } from '@screenhire/api-client/svelte-query';
  import { DEFAULT_QUESTIONS_PER_PAGE } from '@screenhire/ui/questions/model';
  import Trash2Icon from '~icons/lucide/trash-2';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import EditPreviewLayout from '#lib/components/edit-preview-layout.svelte';
  import FormBuilder from '#lib/components/form-builder.svelte';
  import Hint from '#lib/components/hint.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import PreviewFrame from '#lib/components/preview-frame.svelte';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { applyFormKinds, duplicateId, followUpKinds, type AnswerKindGroup } from '#lib/form-builder.js';
  import type { SectionDraft } from '#lib/form-sections.js';
  import { addFormLater, APPLY_FORM_ID, laterStages, removeForm, timelineOf, type FormLater } from '#lib/forms-timeline.js';
  import { createPreviewSource } from '#lib/preview-window.svelte.js';
  import { allQuestions, errorsAt, errorsUnder, followUpsOf, newQuestion, withQuestionType } from '#lib/workflow.js';
  import AiUseRow from './ai-use-row.svelte';
  import { labelClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import FormTimeline from './form-timeline.svelte';
  import QuestionSettings from './question-settings.svelte';
  import QuestionnairePreview from './questionnaire-preview.svelte';

  // The Questions tab (design system section 21): the forms on the candidate's timeline across the top, each named by when
  // it reaches them, and the chosen one below in the form builder. Edit and Preview are one switch (A8): the edit pane takes
  // the work area's width, and the preview is the candidate's form at its real size, or in a second window that follows each
  // edit. The apply form ends in the AI-use row, locked: ScreenHire asks it on every application and the policy is the
  // organization's. A form sent later is made with "Ask more later", which adds the automation that emails it, and removing
  // the form takes that automation with it. A questionnaire may have named sections, each a page for the candidate; without
  // them it splits into pages of the organization's questions per page (Settings > Organization).
  let {
    workflow = $bindable(),
    errors,
    aiUse,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    /** The AI-use policy in force and where to change it (left out for someone who may not). Undefined while it loads. */
    aiUse?: { policy?: AiUsePolicy; href?: string };
  } = $props();

  /** `apply`, or the id of the follow-up form on show. */
  let selected = $state(APPLY_FORM_ID);
  let mode = $state<'edit' | 'preview'>('edit');
  let removing = $state(false);

  const followUps = $derived(followUpsOf(workflow));
  const index = $derived(followUps.findIndex((followUp) => followUp.id === selected));
  const current = $derived(index >= 0 ? followUps[index] : undefined);
  const path = $derived(current ? `followUpQuestionnaires[${index}]` : 'questionnaire');
  const questions = $derived(current ? current.questions : workflow.questionnaire.questions);
  const sections = $derived((current ? current.sections : workflow.questionnaire.sections) ?? null);
  const groups: readonly AnswerKindGroup<QuestionType>[] = $derived(current ? followUpKinds : applyFormKinds);
  const forms = $derived(timelineOf(workflow));
  const stages = $derived(laterStages(workflow));
  const organization = createGetOrganization();
  const questionsPerPage = $derived(organization.data?.data.questionsPerPage ?? DEFAULT_QUESTIONS_PER_PAGE);
  const cascade = $derived(current ? removeForm(workflow, current.id).removed : []);

  // A form that vanishes from under the selection (removed, or a draft reloaded) falls back to the apply form.
  $effect(() => {
    if (selected !== APPLY_FORM_ID && !current) selected = APPLY_FORM_ID;
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

  function askLater(later: FormLater) {
    const added = addFormLater(workflow, later);
    workflow = added.workflow;
    selected = added.formId;
  }

  // The form's automations go with it, so no step is left pointing at a form that is gone. Nothing changes until the
  // editor saves, so with no automation to take along there is nothing to confirm (section 10).
  function removeSelected() {
    if (!current) return;
    workflow = removeForm(workflow, current.id).workflow;
    selected = APPLY_FORM_ID;
  }

  // A copy keeps everything but its id, which stays unique across the workflow, so `answers.{id}` names one question.
  function duplicate(question: Question): Question {
    const copy = $state.snapshot(question) as Question;
    return { ...copy, id: duplicateId(question.id, allQuestions(workflow).map((other) => other.id)) };
  }
</script>

<!-- A problem with the follow-ups as a whole, said in the surface below the switch (S3). -->
{#snippet followUpsErrors()}<FieldErrors id="follow-ups-errors" messages={errorsAt(errors, 'followUpQuestionnaires')} />{/snippet}

<div class="flex min-w-0 flex-col gap-4">
  <FormTimeline {forms} bind:selected {stages} onadd={askLater} />

  <EditPreviewLayout bind:mode wide onpopout={() => source.open('/preview/form')}>
    {#snippet toolbar()}
      {#if current}
        <!-- The form's own actions, beside the switch: removing it takes its email automation along (L3). -->
        <MoreMenu label={`Actions for ${current.name || current.id}`} size="icon">
          <DropdownMenu.Item
            onSelect={() => (cascade.length > 0 ? (removing = true) : removeSelected())}
          ><Trash2Icon aria-hidden="true" />Remove form</DropdownMenu.Item>
        </MoreMenu>
      {/if}
    {/snippet}

    {#snippet preview()}
      <Panel title="Preview">
        <PreviewFrame>
          <QuestionnairePreview {questions} {sections} {questionsPerPage} followUp={!!current} aiUse={current ? undefined : aiUse?.policy} />
        </PreviewFrame>
      </Panel>
    {/snippet}

    {#snippet editor()}
      {@render followUpsErrors()}
      {#if current}
        <div class="flex max-w-form flex-col gap-1.5">
          <!-- The form's id is detail: it is in the label's tooltip (W6). -->
          <div class={labelClass}>
            <Hint content={`Form id: ${current.id}`}>
              {#snippet children({ props })}<label {...props} for="follow-up-name" class="w-fit">Name</label>{/snippet}
            </Hint>
            <Input
              id="follow-up-name"
              bind:value={current.name}
              maxlength={80}
              aria-invalid={errorsAt(errors, `${path}.name`).length > 0 || undefined}
            />
          </div>
          <FieldErrors id="follow-up-name-errors" messages={[...errorsAt(errors, path), ...errorsAt(errors, `${path}.id`), ...errorsAt(errors, `${path}.name`)]} />
        </div>
      {/if}
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
          problems={[
            ...(current ? [] : errorsAt(errors, path)),
            ...errorsAt(errors, `${path}.questions`),
            ...errorsAt(errors, `${path}.sections`),
          ]}
        >
          {#snippet settings(question)}<QuestionSettings {question} />{/snippet}
          {#snippet note()}{#if !current}<AiUseRow policy={aiUse?.policy} href={aiUse?.href} />{/if}{/snippet}
        </FormBuilder>
      {/key}
    {/snippet}
  </EditPreviewLayout>
</div>

{#if current}
  <ConfirmDialog
    bind:open={removing}
    title={`Remove ${current.name || 'this form'}?`}
    description={`This also removes ${cascade.join(', ')}.`}
    confirmLabel="Remove form"
    failure="Could not remove the form"
    onconfirm={async () => removeSelected()}
  />
{/if}
