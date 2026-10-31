<script lang="ts" module>
  import type { Question, QuestionSection } from '@screenhire/api-client';

  /** What the questionnaire's pop-out preview window draws (`/preview/form`): the questions, page by page. */
  export interface FormPreview {
    form: 'apply' | 'follow-up';
    questions: Question[];
    sections: QuestionSection[] | null;
    questionsPerPage: number;
  }
</script>

<script lang="ts">
  import type { DefinitionError, HiringWorkflow, QuestionType } from '@screenhire/api-client';
  import { createGetOrganization } from '@screenhire/api-client/svelte-query';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import EditPreviewLayout from '#lib/components/edit-preview-layout.svelte';
  import FormBuilder from '#lib/components/form-builder.svelte';
  import Hint from '#lib/components/hint.svelte';
  import Panel from '#lib/components/panel.svelte';
  import PreviewFrame from '#lib/components/preview-frame.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { applyFormKinds, duplicateId, followUpKinds, type AnswerKindGroup } from '#lib/form-builder.js';
  import type { SectionDraft } from '#lib/form-sections.js';
  import { createPreviewSource } from '#lib/preview-window.svelte.js';
  import { aiUseLabels, allQuestions, errorsAt, errorsUnder, followUpsOf, newFollowUp, newQuestion, withQuestionType } from '#lib/workflow.js';
  import { labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import QuestionSettings from './question-settings.svelte';
  import QuestionnairePreview from './questionnaire-preview.svelte';

  // The apply form's questions and AI-use policy, and the follow-up questionnaires an automation sends after someone
  // applied, in the form builder. One questionnaire shows at a time; the toggles are peer views of the workflow's
  // questionnaires (design system section 11). Beside the builder where both fit, or behind the Edit / Preview switch,
  // the candidate's form at its real size; the window icon opens it in a second window that follows each edit (A8). A
  // questionnaire may have named sections, each a page for the candidate; without them it splits into pages of the
  // organization's questions per page (Settings > Organization), and the preview shows the pages either way.
  let { workflow = $bindable(), errors }: { workflow: HiringWorkflow; errors: readonly DefinitionError[] } = $props();

  /** `apply`, or the id of the follow-up questionnaire on show. */
  let selected = $state('apply');
  let mode = $state<'edit' | 'preview'>('edit');

  const followUps = $derived(followUpsOf(workflow));
  const index = $derived(followUps.findIndex((followUp) => followUp.id === selected));
  const current = $derived(index >= 0 ? followUps[index] : undefined);
  const path = $derived(current ? `followUpQuestionnaires[${index}]` : 'questionnaire');
  const questions = $derived(current ? current.questions : workflow.questionnaire.questions);
  const sections = $derived((current ? current.sections : workflow.questionnaire.sections) ?? null);
  const groups: readonly AnswerKindGroup<QuestionType>[] = $derived(current ? followUpKinds : applyFormKinds);
  const organization = createGetOrganization();
  const questionsPerPage = $derived(organization.data?.data.questionsPerPage ?? 8);

  const source = createPreviewSource<FormPreview>(() => ({ form: current ? 'follow-up' : 'apply', questions, sections, questionsPerPage }));

  function setQuestions(next: Question[]) {
    if (current) current.questions = next;
    else workflow.questionnaire.questions = next;
  }

  function setSections(next: SectionDraft[] | null | undefined) {
    if (current) current.sections = next ?? null;
    else workflow.questionnaire.sections = next ?? null;
  }

  function addFollowUp() {
    const followUp = newFollowUp(workflow);
    workflow.followUpQuestionnaires = [...followUps, followUp];
    selected = followUp.id;
  }

  function removeFollowUp() {
    workflow.followUpQuestionnaires = followUps.filter((followUp) => followUp.id !== selected);
    selected = 'apply';
  }

  // A copy keeps everything but its id, which stays unique across the workflow, so `answers.{id}` names one question.
  function duplicate(question: Question): Question {
    const copy = $state.snapshot(question) as Question;
    return { ...copy, id: duplicateId(question.id, allQuestions(workflow).map((other) => other.id)) };
  }
</script>

<!-- A problem with the follow-ups as a whole, said in the surface below the switch (S3). -->
{#snippet followUpsErrors()}<FieldErrors id="follow-ups-errors" messages={errorsAt(errors, 'followUpQuestionnaires')} />{/snippet}

<EditPreviewLayout bind:mode onpopout={() => source.open('/preview/form')}>
  {#snippet toolbar()}
    <div role="group" aria-label="Questionnaire" class="flex min-w-0 gap-1 overflow-x-auto">
      <Toggle class="shrink-0" bind:pressed={() => selected === 'apply', (pressed) => pressed && (selected = 'apply')}>Apply form</Toggle>
      {#each followUps as followUp (followUp.id)}
        <Toggle class="shrink-0" bind:pressed={() => selected === followUp.id, (pressed) => pressed && (selected = followUp.id)}>
          {followUp.name || followUp.id}
        </Toggle>
      {/each}
    </div>
    <Button variant="outline" onclick={addFollowUp}><PlusIcon aria-hidden="true" />Add follow-up questionnaire</Button>
  {/snippet}

  {#snippet preview(popout)}
    <Panel title="Preview" actions={popout}>
      <PreviewFrame>
        <QuestionnairePreview {questions} {sections} {questionsPerPage} followUp={!!current} />
      </PreviewFrame>
    </Panel>
  {/snippet}

  {#snippet editor()}
    {#if current}
      <Panel title="Follow-up questionnaire">
        {#snippet actions()}
          <!-- A short label, so the panel title keeps its row at phone width (F5); the accessible name says what it removes. -->
          <Button variant="outline" aria-label="Remove questionnaire" onclick={removeFollowUp}>Remove</Button>
        {/snippet}
        {@render followUpsErrors()}
        <div class="flex max-w-form flex-col gap-1.5">
          <!-- The questionnaire's id is detail: it is in the label's tooltip (W6). -->
          <div class={labelClass}>
            <Hint content={`Questionnaire id: ${current.id}`}>
              {#snippet children({ props })}<label {...props} for="follow-up-name" class="w-fit">Name</label>{/snippet}
            </Hint>
            <Input
              id="follow-up-name"
              bind:value={current.name}
              maxlength={80}
              aria-describedby="follow-up-note"
              aria-invalid={errorsAt(errors, `${path}.name`).length > 0 || undefined}
            />
          </div>
          <FieldErrors id="follow-up-name-errors" messages={[...errorsAt(errors, path), ...errorsAt(errors, `${path}.id`), ...errorsAt(errors, `${path}.name`)]} />
          <p id="follow-up-note" class="text-muted-foreground">A "Send a questionnaire" automation emails the candidate a link to it.</p>
        </div>
      </Panel>
    {:else}
      <Panel title="AI use while applying">
        {@render followUpsErrors()}
        <div class="flex max-w-form flex-col gap-1.5">
          <select class={selectClass} aria-label="AI use while applying" aria-describedby="ai-use-note" bind:value={workflow.integrityPolicy.aiUse}>
            {#each Object.entries(aiUseLabels) as [policy, label] (policy)}<option value={policy}>{label}</option>{/each}
          </select>
          <p id="ai-use-note" class="text-muted-foreground">Candidates see this before they start, and no automation uses their answer.</p>
        </div>
      </Panel>
    {/if}
    {#key path}
      <FormBuilder
        bind:items={() => questions, setQuestions}
        withSections
        bind:sections={() => sections, setSections}
        sectionErrors={(sectionIndex) => errorsUnder(errors, `${path}.sections[${sectionIndex}]`).map((error) => error.message)}
        {groups}
        title={current ? 'Questions' : 'Screening questions'}
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
      </FormBuilder>
    {/key}
  {/snippet}
</EditPreviewLayout>
