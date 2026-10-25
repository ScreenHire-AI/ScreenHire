<script lang="ts">
  import type { DefinitionError, ScorecardQuestion, WorkflowStage } from '@screenhire/api-client';
  import { createGetScorecardTemplate, createListScorecardTemplates } from '@screenhire/api-client/svelte-query';
  import QueryError from '#lib/components/query-error.svelte';
  import ScorecardQuestionList from '#lib/components/scorecard-question-list.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';
  import { errorsAt } from '#lib/workflow.js';
  import { labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';

  // A stage's scorecard (workflow-engine.md section 8): one from the library, ScreenHire's built-ins or the team's own,
  // shown here as it asks. A hiring template's stage names it; a job copies its questions when the workflow is saved,
  // so a later edit in the library never changes what this job's candidates are asked. Questions are written in the
  // library scorecard on the Templates page, not here. An archived scorecard is out of the list unless the stage has it.
  // Changing the choice changes nothing until Save.
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
  const own = $derived((library.data?.data ?? []).filter((scorecard) => !scorecard.builtIn && (!scorecard.archived || scorecard.id === choice)));
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
  {#if library.isError}
    <QueryError message={describeError(library.error, 'Could not load the scorecard library')} onretry={() => library.refetch()} inset />
  {/if}

  {#if choice === ''}
    <p class="text-body">No scorecard: reviewers record no decision in {stage.name}.</p>
  {:else if chosen.isError}
    <QueryError message={describeError(chosen.error, 'Could not load this scorecard')} onretry={() => chosen.refetch()} inset />
  {:else if chosen.isPending && !stage.scorecard}
    <div aria-hidden="true" class="flex flex-col gap-2">
      {#each [0, 1, 2] as row (row)}<Skeleton class="h-10 w-full" />{/each}
    </div>
    <p class="sr-only" role="status">Loading the scorecard…</p>
  {:else if questions.length > 0}
    <ScorecardQuestionList {questions} />
  {/if}
</div>
