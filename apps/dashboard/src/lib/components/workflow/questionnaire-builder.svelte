<script lang="ts">
  import type { DefinitionError, HiringWorkflow, QuestionType } from '@screenhire/api-client';
  import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
  import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import Trash2Icon from '@lucide/svelte/icons/trash-2';
  import IconButton from '#lib/components/icon-button.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StepMarker from '#lib/components/step-marker.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { cn } from '#lib/utils.js';
  import { aiUseLabels, errorsAt, errorsUnder, idFrom, moveItem, newQuestion, questionTypeLabels, questionTypes, removeAt, withQuestionType } from '#lib/workflow.js';
  import { checkboxClass, insetClass, kickerClass, labelClass, rowClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import QuestionnairePreview from './questionnaire-preview.svelte';

  // The screening questions and the AI-use policy, beside a live preview of the candidate's form. Questions reorder with
  // buttons (WCAG 2.5.7).
  let { workflow = $bindable(), errors }: { workflow: HiringWorkflow; errors: readonly DefinitionError[] } = $props();

  let addType = $state<QuestionType>('YesNo');

  const questions = $derived(workflow.questionnaire.questions);
  const setQuestions = (next: typeof questions) => (workflow.questionnaire = { ...workflow.questionnaire, questions: next });
  const numberOrNull = (value: string) => (value.trim() === '' ? null : Number(value));
</script>

<div class="flex flex-wrap items-start gap-5">
  <div class="flex min-w-0 flex-[999_1_520px] flex-col gap-5">
    <Panel title="AI use while applying">
      <div class="flex flex-col gap-1.5">
        <select class={selectClass} aria-label="AI use while applying" aria-describedby="ai-use-note" bind:value={workflow.integrityPolicy.aiUse}>
          {#each Object.entries(aiUseLabels) as [policy, label] (policy)}<option value={policy}>{label}</option>{/each}
        </select>
        <p id="ai-use-note" class="text-muted-foreground">Candidates see this before they start, and no automation uses their answer.</p>
      </div>
    </Panel>

    <Panel title="Screening questions" count={questions.length}>
      <FieldErrors id="questions-errors" messages={[...errorsAt(errors, 'questionnaire'), ...errorsAt(errors, 'questionnaire.questions')]} />
      {#if questions.length === 0}
        <p class="text-muted-foreground">No questions yet. Every form still asks for a resume.</p>
      {/if}
      <ol class="@container flex flex-col">
        {#each questions as question, index (question.id)}
          {@const path = `questionnaire.questions[${index}]`}
          {@const questionErrors = errorsUnder(errors, path).map((error) => error.message)}
          <li aria-label={`Question ${index + 1}: ${question.label}`} class={['flex gap-3', rowClass]}>
            <span class="pt-6"><StepMarker>{index + 1}</StepMarker></span>
            <div class="flex min-w-0 flex-1 flex-col gap-3">
              <div class="grid items-end gap-2 @xl:grid-cols-[minmax(0,1.7fr)_minmax(0,1fr)_auto]">
                <label class={labelClass}>
                  <span>Question <span class="font-mono text-code font-normal text-muted-foreground">answers.{question.id}</span></span>
                  <Input bind:value={question.label} maxlength={300} aria-invalid={questionErrors.length > 0 || undefined} />
                </label>
                <label class={labelClass}>
                  Answer type
                  <select
                    class={selectClass}
                    value={question.type}
                    onchange={(event) => (workflow.questionnaire.questions[index] = withQuestionType(question, event.currentTarget.value as QuestionType))}
                  >
                    {#each questionTypes as type (type)}<option value={type}>{questionTypeLabels[type]}</option>{/each}
                  </select>
                </label>
                <div class="flex shrink-0 gap-0.5">
                  <!-- The first question cannot move up nor the last down: the button is left out, its place kept (A3). -->
                  {#if index > 0}
                    <IconButton label={`Move "${question.label}" up`} onclick={() => setQuestions(moveItem(questions, index, -1))}><ArrowUpIcon /></IconButton>
                  {:else}<span class="size-8 pointer-coarse:size-11"></span>{/if}
                  {#if index < questions.length - 1}
                    <IconButton label={`Move "${question.label}" down`} onclick={() => setQuestions(moveItem(questions, index, 1))}><ArrowDownIcon /></IconButton>
                  {:else}<span class="size-8 pointer-coarse:size-11"></span>{/if}
                  <IconButton label={`Remove "${question.label}"`} onclick={() => setQuestions(removeAt(questions, index))}><Trash2Icon /></IconButton>
                </div>
              </div>
              <div class="grid gap-2 @md:grid-cols-[minmax(0,1fr)_auto]">
                <label class={labelClass}>
                  Help text (optional)
                  <Input bind:value={() => question.helpText ?? '', (value) => (question.helpText = value || null)} maxlength={500} />
                </label>
                <label class="flex items-center gap-2 pointer-coarse:min-h-11 self-end pb-1.5 font-semibold">
                  <input type="checkbox" class={checkboxClass} bind:checked={() => question.required ?? false, (on) => (question.required = on)} />
                  Required
                </label>
              </div>

              {#if question.type === 'SingleChoice' || question.type === 'MultiChoice'}
                <fieldset class={insetClass}>
                  <legend class={[kickerClass, 'mb-1']}>Options</legend>
                  {#each question.options ?? [] as option, optionIndex (option.id)}
                    <div class="flex items-center gap-1.5">
                      <label class={[labelClass, 'flex-1']}>
                        <span class="sr-only">Option {optionIndex + 1}</span>
                        <Input bind:value={option.label} maxlength={200} />
                      </label>
                      <IconButton label={`Remove option ${option.label}`} onclick={() => (question.options = removeAt(question.options ?? [], optionIndex))}
                        ><Trash2Icon /></IconButton
                      >
                    </div>
                  {/each}
                  <div>
                    <Button
                      variant="outline"
                      onclick={() => {
                        const label = `Option ${(question.options?.length ?? 0) + 1}`;
                        question.options = [...(question.options ?? []), { id: idFrom(label, (question.options ?? []).map((o) => o.id), 'option'), label }];
                      }}>Add option</Button
                    >
                  </div>
                </fieldset>
              {:else if question.type === 'YesNo'}
                <div class={insetClass}>
                  <label class="flex items-center gap-2 pointer-coarse:min-h-11 font-semibold">
                    <input
                      type="checkbox"
                      class={checkboxClass}
                      bind:checked={() => !!question.knockout, (on) => (question.knockout = on ? { disqualifyingAnswer: false, reason: null } : null)}
                    />
                    Knockout question
                  </label>
                  <p class="text-muted-foreground">
                    A knockout is an objective eligibility check only, such as the right to work, never AI use or an accommodation.
                  </p>
                  {#if question.knockout}
                    <div class="grid gap-2 @md:grid-cols-[10rem_minmax(0,1fr)]">
                      <label class={labelClass}>
                        Ineligible answer
                        <select
                          class={selectClass}
                          value={question.knockout.disqualifyingAnswer ? 'yes' : 'no'}
                          onchange={(event) => (question.knockout!.disqualifyingAnswer = event.currentTarget.value === 'yes')}
                        >
                          <option value="no">No</option>
                          <option value="yes">Yes</option>
                        </select>
                      </label>
                      <label class={labelClass}>
                        Reason the candidate sees
                        <Input bind:value={() => question.knockout?.reason ?? '', (value) => (question.knockout!.reason = value || null)} maxlength={500} />
                      </label>
                    </div>
                  {/if}
                </div>
              {:else if question.type === 'Number'}
                <div class="grid gap-2 @md:grid-cols-2">
                  <label class={labelClass}>
                    Minimum (optional)
                    <Input type="number" value={question.min ?? ''} onchange={(event) => (question.min = numberOrNull(event.currentTarget.value))} />
                  </label>
                  <label class={labelClass}>
                    Maximum (optional)
                    <Input type="number" value={question.max ?? ''} onchange={(event) => (question.max = numberOrNull(event.currentTarget.value))} />
                  </label>
                </div>
              {:else if question.type === 'File'}
                <label class={labelClass}>
                  Accepted file types, separated by commas
                  <Input
                    value={(question.acceptedFileTypes ?? []).join(', ')}
                    onchange={(event) =>
                      (question.acceptedFileTypes = event.currentTarget.value
                        .split(',')
                        .map((type) => type.trim().toLowerCase())
                        .filter(Boolean))}
                  />
                </label>
              {:else}
                <label class={[labelClass, 'max-w-48']}>
                  Longest answer (optional)
                  <Input type="number" min={1} value={question.maxLength ?? ''} onchange={(event) => (question.maxLength = numberOrNull(event.currentTarget.value))} />
                </label>
              {/if}
              <FieldErrors id={`question-${index}-errors`} messages={questionErrors} />
            </div>
          </li>
        {/each}
      </ol>
      {#snippet footer()}
        <div class="flex flex-wrap items-end gap-2">
          <label class={[labelClass, 'w-56 max-w-full']}>
            New question's answer type
            <select class={cn(selectClass, 'bg-card')} bind:value={addType}>
              {#each questionTypes as type (type)}<option value={type}>{questionTypeLabels[type]}</option>{/each}
            </select>
          </label>
          <Button variant="outline" onclick={() => setQuestions([...questions, newQuestion(workflow, addType)])}><PlusIcon aria-hidden="true" />Add question</Button>
        </div>
      {/snippet}
    </Panel>
  </div>

  <Panel title="Candidate preview" class="flex-[1_1_320px] lg:sticky lg:top-4">
    <QuestionnairePreview {questions} />
  </Panel>
</div>
