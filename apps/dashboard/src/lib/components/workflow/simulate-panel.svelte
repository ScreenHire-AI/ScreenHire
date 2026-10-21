<script lang="ts">
  import type { AnswerFact, EvaluationOutcome, HiringWorkflow, TriggerType, WorkflowSimulationResult } from '@screenhire/api-client';
  import { ApiError, createSimulateWorkflow } from '@screenhire/api-client/svelte-query';
  import CircleIcon from '@lucide/svelte/icons/circle';
  import CircleCheckIcon from '@lucide/svelte/icons/circle-check';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { describeError } from '#lib/query.js';
  import {
    aiAssistanceLevels,
    authenticityLevels,
    describeAction,
    describePath,
    operatorLabels,
    problemErrors,
    triggerLabels,
    triggerNeedsStage,
    triggerTypes,
  } from '#lib/workflow.js';
  import { checkboxClass, kickerClass, labelClass, selectClass } from './controls.js';

  // "Test this workflow" (workflow-engine.md §4): a sample application through the definition being edited, unsaved
  // changes included, with the evaluator the worker runs. Nothing is saved or sent.
  let { workflow }: { workflow: HiringWorkflow } = $props();

  const simulate = createSimulateWorkflow();

  let triggerType = $state<TriggerType>('ApplicationSubmitted');
  let stageId = $state('');
  let answers = $state<Record<string, string>>({});
  let tags = $state('');
  let source = $state('');
  let hoursInStage = $state('');
  let authenticity = $state('');
  let aiAssistance = $state('');
  let accommodation = $state(false);
  let result = $state<WorkflowSimulationResult | null>(null);
  let failure = $state<{ message: string; problems: string[] } | null>(null);

  const stage = $derived(workflow.stages.some((candidate) => candidate.id === stageId) ? stageId : (workflow.stages[0]?.id ?? ''));
  const questions = $derived(workflow.questionnaire.questions.filter((question) => question.type !== 'File'));
  const list = (text: string) =>
    text
      .split(',')
      .map((item) => item.trim())
      .filter(Boolean);

  const outcomeText: Record<EvaluationOutcome, string> = {
    Matched: 'Would run',
    NotMatched: 'Conditions do not hold',
    Skipped: 'Skipped',
    Blocked: 'Blocked',
  };

  async function run() {
    failure = null;
    const facts = {
      answers: questions
        .map((question): AnswerFact => ({ questionId: question.id, values: question.type === 'MultiChoice' ? list(answers[question.id] ?? '') : [answers[question.id] ?? ''].filter(Boolean) }))
        .filter((answer) => answer.values.length > 0),
      tags: list(tags),
      source: source.trim() || null,
      hoursInStage: hoursInStage.trim() ? Number(hoursInStage) : null,
      integrityAuthenticity: authenticity || null,
      integrityAiAssistance: aiAssistance || null,
    };
    try {
      const response = await simulate.mutateAsync({
        data: {
          workflow,
          trigger: { type: triggerType, ...(triggerNeedsStage(triggerType) && { stageId: stage }) },
          facts,
          accommodationRequested: accommodation,
        },
      });
      result = response.data;
    } catch (error) {
      result = null;
      failure =
        error instanceof ApiError && error.status === 400
          ? {
              message: 'Fix the problems in this workflow first: a workflow that cannot be saved cannot be tested.',
              problems: problemErrors(error.body).map((problem) => `${describePath(workflow, problem.path)}: ${problem.message}`),
            }
          : { message: describeError(error, 'Could not run the test'), problems: [] };
    }
  }
</script>

<div class="grid items-start gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
  <form
    aria-labelledby="sample-heading"
    class="flex flex-col gap-3 rounded-xl border border-border bg-card p-3.5"
    onsubmit={(event) => {
      event.preventDefault();
      void run();
    }}
  >
    <h3 id="sample-heading" class="font-bold">Sample application</h3>
    <div class="grid gap-2 sm:grid-cols-2">
      <label class={labelClass}>
        Trigger
        <select class={selectClass} bind:value={triggerType}>
          {#each triggerTypes as type (type)}<option value={type}>{triggerLabels[type]}</option>{/each}
        </select>
      </label>
      {#if triggerNeedsStage(triggerType)}
        <label class={labelClass}>
          Stage
          <select class={selectClass} value={stage} onchange={(event) => (stageId = event.currentTarget.value)}>
            {#each workflow.stages as option (option.id)}<option value={option.id}>{option.name}</option>{/each}
          </select>
        </label>
      {/if}
    </div>

    {#if questions.length > 0}
      <fieldset class="flex flex-col gap-2">
        <legend class={kickerClass}>Answers</legend>
        {#each questions as question (question.id)}
          <label class={labelClass}>
            {question.label}
            {#if question.type === 'YesNo' || question.type === 'SingleChoice'}
              <select class={selectClass} bind:value={answers[question.id]}>
                <option value="">Not answered</option>
                {#if question.type === 'YesNo'}
                  <option value="yes">Yes</option>
                  <option value="no">No</option>
                {:else}
                  {#each question.options ?? [] as option (option.id)}<option value={option.id}>{option.label}</option>{/each}
                {/if}
              </select>
            {:else}
              <Input bind:value={answers[question.id]} placeholder={question.type === 'MultiChoice' ? 'Option ids, separated by commas' : undefined} />
            {/if}
          </label>
        {/each}
      </fieldset>
    {/if}

    <fieldset class="flex flex-col gap-2">
      <legend class={kickerClass}>Application</legend>
      <div class="grid gap-2 sm:grid-cols-3">
        <label class={labelClass}>Tags, separated by commas<Input bind:value={tags} /></label>
        <label class={labelClass}>Source<Input bind:value={source} /></label>
        <label class={labelClass}>Hours in stage<Input type="number" min={0} bind:value={hoursInStage} /></label>
      </div>
    </fieldset>

    <fieldset class="flex flex-col gap-2">
      <legend class={kickerClass}>Integrity levels</legend>
      <div class="grid gap-2 sm:grid-cols-2">
        <label class={labelClass}>
          Authenticity
          <select class={selectClass} bind:value={authenticity}>
            <option value="">Not assessed</option>
            {#each authenticityLevels as level (level.value)}<option value={level.value}>{level.label}</option>{/each}
          </select>
        </label>
        <label class={labelClass}>
          AI assistance
          <select class={selectClass} bind:value={aiAssistance}>
            <option value="">Not assessed</option>
            {#each aiAssistanceLevels as level (level.value)}<option value={level.value}>{level.label}</option>{/each}
          </select>
        </label>
      </div>
      <label class="flex items-center gap-2 text-body font-semibold">
        <input type="checkbox" class={checkboxClass} bind:checked={accommodation} />
        The candidate asked for an accommodation (timing facts are off)
      </label>
    </fieldset>

    <div><Button type="submit" disabled={simulate.isPending}>Run test</Button></div>
  </form>

  <section aria-labelledby="results-heading" aria-live="polite" class="flex flex-col gap-2">
    <h3 id="results-heading" class={kickerClass}>What would happen</h3>
    {#if failure}
      <div role="alert" class="rounded-xl border border-destructive/40 bg-card p-3.5 text-body">
        <p class="font-semibold text-destructive">{failure.message}</p>
        {#if failure.problems.length > 0}
          <ul class="mt-1.5 flex list-disc flex-col gap-0.5 pl-5">{#each failure.problems as problem (problem)}<li>{problem}</li>{/each}</ul>
        {/if}
      </div>
    {:else if !result}
      <p class="text-body text-muted-foreground">Fill in a sample application and run the test. Unsaved changes are included.</p>
    {:else if result.automations.length === 0}
      <p class="text-body">No automation fires on this trigger.</p>
    {:else}
      <ol class="flex flex-col gap-2">
        {#each result.automations as automation (automation.automationId)}
          <li aria-label={`${automation.name}: ${outcomeText[automation.outcome]}`} class="flex flex-col gap-1.5 rounded-xl border border-border bg-card p-3.5">
            <div class="flex flex-wrap items-baseline justify-between gap-2">
              <h4 class="font-bold">{automation.name}</h4>
              <span class={['inline-flex items-center gap-1 text-caption', automation.outcome === 'Matched' ? 'text-foreground' : 'text-muted-foreground']}>
                {#if automation.outcome === 'Matched'}<CircleCheckIcon aria-hidden="true" class="size-3.5" />{:else}<CircleIcon aria-hidden="true" class="size-3.5" />{/if}
                {outcomeText[automation.outcome]}
              </span>
            </div>
            {#if automation.reason}<p class="text-body text-muted-foreground">{automation.reason}</p>{/if}
            {#if automation.rules.length > 0}
              <ul class="flex flex-col gap-0.5">
                {#each automation.rules as rule, ruleIndex (ruleIndex)}
                  <li><code class="font-mono text-code">{rule.fact}</code> {operatorLabels[rule.operator]}: {rule.held ? 'holds' : 'does not hold'}</li>
                {/each}
              </ul>
            {/if}
            {#if automation.actions.length > 0}
              <ol class="flex list-decimal flex-col gap-0.5 pl-5 text-body">
                {#each automation.actions as action, actionIndex (actionIndex)}<li>{describeAction(action, workflow)}</li>{/each}
              </ol>
            {/if}
          </li>
        {/each}
      </ol>
    {/if}
  </section>
</div>
