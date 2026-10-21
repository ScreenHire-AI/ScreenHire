<script lang="ts">
  import type { AnswerFact, EvaluationOutcome, HiringWorkflow, TriggerType, WorkflowSimulationResult } from '@screenhire/api-client';
  import { ApiError, createSimulateWorkflow } from '@screenhire/api-client/svelte-query';
  import Panel from '#lib/components/panel.svelte';
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
  import { checkboxClass, kickerClass, labelClass, rowClass, selectClass } from './controls.js';

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

<div class="flex flex-wrap items-start gap-5">
  <Panel title="Sample application" class="flex-[999_1_520px]">
    <form
      aria-label="Sample application"
      class="@container flex flex-col gap-4"
      onsubmit={(event) => {
        event.preventDefault();
        void run();
      }}
    >
      <div class="grid gap-2 @md:grid-cols-2">
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
        <div class="grid gap-2 @xl:grid-cols-3">
          <label class={labelClass}>Tags, separated by commas<Input bind:value={tags} /></label>
          <label class={labelClass}>Source<Input bind:value={source} /></label>
          <label class={labelClass}>Hours in stage<Input type="number" min={0} bind:value={hoursInStage} /></label>
        </div>
      </fieldset>

      <fieldset class="flex flex-col gap-2">
        <legend class={kickerClass}>Integrity levels</legend>
        <div class="grid gap-2 @md:grid-cols-2">
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
        <label class="flex items-center gap-2 font-semibold">
          <input type="checkbox" class={checkboxClass} bind:checked={accommodation} />
          The candidate asked for an accommodation (timing facts are off)
        </label>
      </fieldset>

      <div><Button type="submit" variant="outline" disabled={simulate.isPending}>Run test</Button></div>
    </form>
  </Panel>

  <Panel title="What would happen" aria-live="polite" class="max-w-95 flex-[1_1_320px]">
    {#if failure}
      <div role="alert">
        <p class="font-semibold text-status-verify">{failure.message}</p>
        {#if failure.problems.length > 0}
          <ul class="mt-1.5 flex list-disc flex-col gap-0.5 pl-5">{#each failure.problems as problem (problem)}<li>{problem}</li>{/each}</ul>
        {/if}
      </div>
    {:else if !result}
      <p class="text-muted-foreground">Nothing run yet. Unsaved changes are included.</p>
    {:else if result.automations.length === 0}
      <p>No automation fires on this trigger.</p>
    {:else}
      <ol class="flex flex-col">
        {#each result.automations as automation (automation.automationId)}
          <li aria-label={`${automation.name}: ${outcomeText[automation.outcome]}`} class={['flex flex-col gap-1.5', rowClass]}>
            <div class="flex flex-wrap items-baseline justify-between gap-2">
              <h3 class="font-semibold">{automation.name}</h3>
              <span class={automation.outcome === 'Matched' ? 'font-semibold' : 'text-muted-foreground'}>{outcomeText[automation.outcome]}</span>
            </div>
            {#if automation.reason}<p class="text-muted-foreground">{automation.reason}</p>{/if}
            {#if automation.rules.length > 0}
              <ul class="flex flex-col gap-0.5">
                {#each automation.rules as rule, ruleIndex (ruleIndex)}
                  <li><code class="font-mono text-code">{rule.fact}</code> {operatorLabels[rule.operator]}: {rule.held ? 'holds' : 'does not hold'}</li>
                {/each}
              </ul>
            {/if}
            {#if automation.actions.length > 0}
              <ol class="flex list-decimal flex-col gap-0.5 pl-5">
                {#each automation.actions as action, actionIndex (actionIndex)}<li>{describeAction(action, workflow)}</li>{/each}
              </ol>
            {/if}
          </li>
        {/each}
      </ol>
    {/if}
  </Panel>
</div>
