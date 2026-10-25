<script lang="ts" module>
  import type { Question } from '@screenhire/api-client';

  /** What the questionnaire's pop-out preview window draws (`/preview/form`). */
  export interface FormPreview {
    form: 'apply' | 'follow-up';
    questions: Question[];
  }
</script>

<script lang="ts">
  import type { DefinitionError, HiringWorkflow, QuestionType } from '@screenhire/api-client';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import EditPreviewSwitch from '#lib/components/edit-preview-switch.svelte';
  import FormBuilder from '#lib/components/form-builder.svelte';
  import Panel from '#lib/components/panel.svelte';
  import PreviewFrame from '#lib/components/preview-frame.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { applyFormKinds, duplicateId, followUpKinds, type AnswerKindGroup } from '#lib/form-builder.js';
  import { createPreviewSource } from '#lib/preview-window.svelte.js';
  import { aiUseLabels, allQuestions, errorsAt, errorsUnder, followUpsOf, newFollowUp, newQuestion, withQuestionType } from '#lib/workflow.js';
  import { labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import QuestionSettings from './question-settings.svelte';
  import QuestionnairePreview from './questionnaire-preview.svelte';

  // The apply form's questions and AI-use policy, and the follow-up questionnaires an automation sends after someone
  // applied, in the form builder. One questionnaire shows at a time; the toggles are peer views of the workflow's
  // questionnaires (design system section 11). The Edit / Preview switch swaps the builder for the candidate's form at
  // its real size, and its window icon opens that form in a second window that follows each edit (A8).
  let { workflow = $bindable(), errors }: { workflow: HiringWorkflow; errors: readonly DefinitionError[] } = $props();

  /** `apply`, or the id of the follow-up questionnaire on show. */
  let selected = $state('apply');
  let mode = $state<'edit' | 'preview'>('edit');

  const followUps = $derived(followUpsOf(workflow));
  const index = $derived(followUps.findIndex((followUp) => followUp.id === selected));
  const current = $derived(index >= 0 ? followUps[index] : undefined);
  const path = $derived(current ? `followUpQuestionnaires[${index}]` : 'questionnaire');
  const questions = $derived(current ? current.questions : workflow.questionnaire.questions);
  const groups: readonly AnswerKindGroup<QuestionType>[] = $derived(current ? followUpKinds : applyFormKinds);

  const source = createPreviewSource<FormPreview>(() => ({ form: current ? 'follow-up' : 'apply', questions }));

  function setQuestions(next: Question[]) {
    if (current) current.questions = next;
    else workflow.questionnaire.questions = next;
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

<div class="flex flex-col gap-5">
  <div class="flex min-w-0 flex-wrap items-center justify-between gap-2">
    <div class="flex min-w-0 flex-wrap items-center gap-2">
      <div role="group" aria-label="Questionnaire" class="flex min-w-0 gap-1 overflow-x-auto">
        <Toggle class="shrink-0" bind:pressed={() => selected === 'apply', (pressed) => pressed && (selected = 'apply')}>Apply form</Toggle>
        {#each followUps as followUp (followUp.id)}
          <Toggle class="shrink-0" bind:pressed={() => selected === followUp.id, (pressed) => pressed && (selected = followUp.id)}>
            {followUp.name || followUp.id}
          </Toggle>
        {/each}
      </div>
      <Button variant="outline" onclick={addFollowUp}><PlusIcon aria-hidden="true" />Add follow-up questionnaire</Button>
    </div>
    <EditPreviewSwitch bind:mode onpopout={() => source.open('/preview/form')} />
  </div>
  <FieldErrors id="follow-ups-errors" messages={errorsAt(errors, 'followUpQuestionnaires')} />

  {#if mode === 'preview'}
    <PreviewFrame>
      <QuestionnairePreview {questions} followUp={!!current} />
    </PreviewFrame>
  {:else}
    {#if current}
      <Panel title="Follow-up questionnaire">
        {#snippet actions()}
          <!-- A short label, so the panel title keeps its row at phone width (F5); the accessible name says what it removes. -->
          <Button variant="outline" aria-label="Remove questionnaire" onclick={removeFollowUp}>Remove</Button>
        {/snippet}
        <div class="flex max-w-form flex-col gap-1.5">
          <label class={labelClass}>
            <span>Name <span class="font-mono text-code font-normal text-muted-foreground">{current.id}</span></span>
            <Input bind:value={current.name} maxlength={80} aria-describedby="follow-up-note" aria-invalid={errorsAt(errors, `${path}.name`).length > 0 || undefined} />
          </label>
          <FieldErrors id="follow-up-name-errors" messages={[...errorsAt(errors, path), ...errorsAt(errors, `${path}.id`), ...errorsAt(errors, `${path}.name`)]} />
          <p id="follow-up-note" class="text-muted-foreground">A "Send a questionnaire" automation emails the candidate a link to it.</p>
        </div>
      </Panel>
    {:else}
      <Panel title="AI use while applying">
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
        problems={current ? errorsAt(errors, `${path}.questions`) : [...errorsAt(errors, path), ...errorsAt(errors, `${path}.questions`)]}
      >
        {#snippet settings(question)}<QuestionSettings {question} />{/snippet}
      </FormBuilder>
    {/key}
  {/if}
</div>
