<script lang="ts">
  import type { DefinitionError, ScorecardQuestion, WorkflowStage } from '@screenhire/api-client';
  import { createGetScorecardTemplate, createListScorecardTemplates } from '@screenhire/api-client/svelte-query';
  import { isRatingKind } from '#lib/scorecards.js';
  import { errorsAt, scorecardQuestionTypeLabels } from '#lib/workflow.js';
  import { labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';

  // A stage's scorecard (workflow-engine.md section 8): one from the library, ScreenHire's built-ins or the team's own,
  // shown here as it asks. A hiring template's stage names it; a job copies its questions when the workflow is saved,
  // so a later edit in the library never changes what this job's candidates are asked. Questions are written in the
  // library's scorecard editor, not here. Changing the choice changes nothing until Save.
  let { stage = $bindable(), index, errors }: { stage: WorkflowStage; index: number; errors: readonly DefinitionError[] } = $props();

  const uid = $props.id();
  const library = createListScorecardTemplates();
  // A job's stage may hold a copy that names no library scorecard: the job's own, kept as it is.
  const ownCopy = '__copy';
  const choice = $derived(stage.scorecardTemplateId ?? (stage.scorecard ? ownCopy : ''));
  const chosen = createGetScorecardTemplate(
    () => stage.scorecardTemplateId ?? '',
    () => ({ query: { enabled: Boolean(stage.scorecardTemplateId) && !stage.scorecard } }),
  );
  const builtIns = $derived((library.data?.data ?? []).filter((scorecard) => scorecard.builtIn));
  const own = $derived((library.data?.data ?? []).filter((scorecard) => !scorecard.builtIn));
  const questions = $derived<readonly ScorecardQuestion[]>(stage.scorecard?.questions ?? chosen.data?.data.questions ?? []);
  const messages = $derived([...errorsAt(errors, `stages[${index}].scorecard`), ...errorsAt(errors, `stages[${index}].scorecardTemplateId`)]);

  function pick(key: string) {
    if (key === choice) return;
    // A different scorecard: the job takes a fresh copy of it when it is saved.
    stage.scorecard = null;
    stage.scorecardTemplateId = key === '' ? null : key;
  }
</script>

<div class="flex flex-col gap-4">
  <label class={labelClass}>
    Scorecard
    <select id={`${uid}-scorecard`} class={selectClass} value={choice} onchange={(event) => pick(event.currentTarget.value)} aria-invalid={messages.length > 0 || undefined}>
      <option value="">No scorecard</option>
      {#if choice === ownCopy}<option value={ownCopy}>{stage.scorecard?.name} (this job's copy)</option>{/if}
      {#if builtIns.length > 0}
        <optgroup label="Built in">
          {#each builtIns as scorecard (scorecard.id)}<option value={scorecard.id}>{scorecard.name}</option>{/each}
        </optgroup>
      {/if}
      {#if own.length > 0}
        <optgroup label="Your scorecards">
          {#each own as scorecard (scorecard.id)}<option value={scorecard.id}>{scorecard.name}</option>{/each}
        </optgroup>
      {/if}
    </select>
  </label>
  <FieldErrors id={`${uid}-errors`} {messages} />

  {#if choice === ''}
    <p class="text-body">No scorecard: reviewers record no decision in {stage.name}.</p>
  {:else if questions.length > 0}
    <ol class="flex flex-col gap-2" aria-label="Questions">
      {#each questions as question (question.id)}
        <li class="flex flex-col gap-0.5">
          <span class="font-semibold break-words">{question.label}{#if question.required}<span class="text-muted-foreground"> *</span>{/if}</span>
          <span class="text-caption text-muted-foreground">
            {isRatingKind(question.type) ? 'Rating, ' : ''}{scorecardQuestionTypeLabels[question.type]}{question.guidance ? `. ${question.guidance}` : ''}
          </span>
        </li>
      {/each}
    </ol>
    <p class="text-muted-foreground">Then an overall recommendation and a note.</p>
  {/if}
</div>
