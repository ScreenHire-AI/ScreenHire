<script lang="ts">
  import type { AiUsePolicy, DefinitionError, HiringWorkflow, QuestionType } from '@screenhire/api-client';
  import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
  import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
  import Trash2Icon from '@lucide/svelte/icons/trash-2';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { errorsAt, errorsUnder, idFrom, moveItem, newQuestion, questionTypeLabels, questionTypes, removeAt, withQuestionType } from '#lib/workflow.js';
  import { checkboxClass, kickerClass, labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import QuestionnairePreview from './questionnaire-preview.svelte';

  // The screening questions and the AI-use policy, beside a live preview of the candidate's form. Questions reorder with
  // buttons (WCAG 2.5.7).
  let { workflow = $bindable(), errors, readonly = false }: { workflow: HiringWorkflow; errors: readonly DefinitionError[]; readonly?: boolean } =
    $props();

  let addType = $state<QuestionType>('YesNo');

  const aiUseLabels: Record<AiUsePolicy, string> = {
    Allowed: 'Allowed: candidates may use AI tools and need not say so',
    AllowedWithDisclosure: 'Allowed with disclosure: candidates say if they used AI tools',
    NotAllowed: 'Not allowed: candidates are asked to write without AI tools',
  };

  const questions = $derived(workflow.questionnaire.questions);
  const setQuestions = (next: typeof questions) => (workflow.questionnaire = { ...workflow.questionnaire, questions: next });
  const numberOrNull = (value: string) => (value.trim() === '' ? null : Number(value));
</script>

<div class="grid items-start gap-4 lg:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
  <section aria-labelledby="questions-heading" class="flex flex-col gap-3">
    <fieldset disabled={readonly} class="flex flex-col gap-3">
      <legend class="sr-only">Questionnaire</legend>
      <label class={[labelClass, 'rounded-xl border border-border bg-card p-3.5']}>
        AI use while applying
        <select class={selectClass} bind:value={workflow.integrityPolicy.aiUse}>
          {#each Object.entries(aiUseLabels) as [policy, label] (policy)}<option value={policy}>{label}</option>{/each}
        </select>
        <span class="font-normal text-muted-foreground">Candidates see this before they start. What they declare is never a fact an automation can use.</span>
      </label>

      <h3 id="questions-heading" class={kickerClass}>Screening questions</h3>
      <FieldErrors id="questions-errors" messages={[...errorsAt(errors, 'questionnaire'), ...errorsAt(errors, 'questionnaire.questions')]} />
      {#if questions.length === 0}
        <p class="text-[13px] text-muted-foreground">No questions yet. Every form still asks for a resume.</p>
      {/if}
      <ol class="flex flex-col gap-2.5">
        {#each questions as question, index (question.id)}
          {@const path = `questionnaire.questions[${index}]`}
          {@const questionErrors = errorsUnder(errors, path).map((error) => error.message)}
          <li aria-label={`Question ${index + 1}: ${question.label}`} class="flex flex-col gap-2.5 rounded-xl border border-border bg-card p-3.5">
            <div class="grid items-end gap-2 sm:grid-cols-[minmax(0,1.7fr)_minmax(0,1fr)_auto]">
              <label class={labelClass}>
                <span>Question <span class="font-mono text-[11px] font-normal text-muted-foreground">answers.{question.id}</span></span>
                <Input bind:value={question.label} maxlength={300} aria-invalid={questionErrors.length > 0 || undefined} />
              </label>
              <label class={labelClass}>
                Answer type
                <select class={selectClass} value={question.type} onchange={(event) => (workflow.questionnaire.questions[index] = withQuestionType(question, event.currentTarget.value as QuestionType))}>
                  {#each questionTypes as type (type)}<option value={type}>{questionTypeLabels[type]}</option>{/each}
                </select>
              </label>
              <div class="flex gap-0.5">
                <Button variant="ghost" size="icon-sm" aria-label={`Move "${question.label}" up`} disabled={index === 0} onclick={() => setQuestions(moveItem(questions, index, -1))}
                  ><ArrowUpIcon aria-hidden="true" /></Button
                >
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Move "${question.label}" down`}
                  disabled={index === questions.length - 1}
                  onclick={() => setQuestions(moveItem(questions, index, 1))}><ArrowDownIcon aria-hidden="true" /></Button
                >
                <Button variant="ghost" size="icon-sm" aria-label={`Remove "${question.label}"`} onclick={() => setQuestions(removeAt(questions, index))}
                  ><Trash2Icon aria-hidden="true" /></Button
                >
              </div>
            </div>
            <div class="grid gap-2 sm:grid-cols-[minmax(0,1fr)_auto]">
              <label class={labelClass}>
                Help text (optional)
                <Input bind:value={() => question.helpText ?? '', (value) => (question.helpText = value || null)} maxlength={500} />
              </label>
              <label class="flex items-center gap-2 self-end pb-1.5 text-[13px] font-semibold">
                <input type="checkbox" class={checkboxClass} bind:checked={() => question.required ?? false, (on) => (question.required = on)} />
                Required
              </label>
            </div>

            {#if question.type === 'SingleChoice' || question.type === 'MultiChoice'}
              <fieldset class="flex flex-col gap-1.5">
                <legend class={kickerClass}>Options</legend>
                {#each question.options ?? [] as option, optionIndex (option.id)}
                  <div class="flex items-end gap-1.5">
                    <label class={[labelClass, 'flex-1']}>
                      <span class="sr-only">Option {optionIndex + 1}</span>
                      <Input bind:value={option.label} maxlength={200} />
                    </label>
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Remove option ${option.label}`}
                      onclick={() => (question.options = removeAt(question.options ?? [], optionIndex))}><Trash2Icon aria-hidden="true" /></Button
                    >
                  </div>
                {/each}
                <div>
                  <Button
                    variant="outline"
                    size="sm"
                    onclick={() => {
                      const label = `Option ${(question.options?.length ?? 0) + 1}`;
                      question.options = [...(question.options ?? []), { id: idFrom(label, (question.options ?? []).map((o) => o.id), 'option'), label }];
                    }}>Add option</Button
                  >
                </div>
              </fieldset>
            {:else if question.type === 'YesNo'}
              <div class="flex flex-col gap-2 rounded-lg border border-border-subtle bg-muted/40 p-2.5">
                <label class="flex items-center gap-2 text-[13px] font-semibold">
                  <input
                    type="checkbox"
                    class={checkboxClass}
                    bind:checked={() => !!question.knockout, (on) => (question.knockout = on ? { disqualifyingAnswer: false, reason: null } : null)}
                  />
                  Knockout question
                </label>
                <p class="text-[12.5px] text-muted-foreground">
                  A knockout is an objective eligibility check only, such as the right to work or a required licence. Never use
                  one for AI use or for needing an accommodation: that would screen people out for exactly what ScreenHire
                  protects. A knockout answer only queues the application for a person to review unless an automation opts in
                  to rejecting.
                </p>
                {#if question.knockout}
                  <div class="grid gap-2 sm:grid-cols-[10rem_minmax(0,1fr)]">
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
              <div class="grid gap-2 sm:grid-cols-2">
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
          </li>
        {/each}
      </ol>
      <div class="flex flex-wrap items-end gap-2">
        <label class={[labelClass, 'w-48']}>
          New question's answer type
          <select class={selectClass} bind:value={addType}>
            {#each questionTypes as type (type)}<option value={type}>{questionTypeLabels[type]}</option>{/each}
          </select>
        </label>
        <Button variant="outline" onclick={() => setQuestions([...questions, newQuestion(workflow, addType)])}>Add question</Button>
      </div>
    </fieldset>
  </section>

  <section aria-labelledby="preview-heading" class="flex flex-col gap-2 lg:sticky lg:top-4">
    <h3 id="preview-heading" class={kickerClass}>Candidate preview</h3>
    <p class="text-[12.5px] text-muted-foreground">
      The careers apply page renders these same components. Try the answers here; nothing is sent.
    </p>
    <QuestionnairePreview {questions} />
  </section>
</div>
