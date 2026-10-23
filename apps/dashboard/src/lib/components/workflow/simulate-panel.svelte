<script lang="ts">
  import type { AnswerFact, EvaluationOutcome, HiringWorkflow, TriggerType, WorkflowSimulationResult } from '@screenhire/api-client';
  import { ApiError, createListEmailTemplates, createSimulateWorkflow } from '@screenhire/api-client/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { describeError } from '#lib/query.js';
  import {
    aiAssistanceLevels,
    authenticityLevels,
    describeAction,
    emailTemplateNames,
    describePath,
    hasTimeLimit,
    operatorLabels,
    problemErrors,
    recommendationLabels,
    recommendations,
    triggerLabels,
    triggerNeedsStage,
    triggerTypes,
  } from '#lib/workflow.js';
  import { checkboxClass, kickerClass, labelClass, rowClass, selectClass } from './controls.js';

  // "Test this workflow" (workflow-engine.md §4): a sample application through the definition being edited, unsaved
  // changes included, with the evaluator the worker runs. Nothing is saved or sent.
  let { workflow }: { workflow: HiringWorkflow } = $props();

  // The organization's template names, for "Send the ... email".
  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));

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
  let recommendation = $state('');
  let result = $state<WorkflowSimulationResult | null>(null);
  let failure = $state<{ message: string; problems: string[] } | null>(null);

  // A reviewer decision comes from a stage's scorecard, and a passed time limit from a stage's limit, so each offers only
  // the stages that have one.
  const reviewed = $derived(triggerType === 'ReviewerDecision');
  const timed = $derived(triggerType === 'SlaBreached');
  const stageOptions = $derived(
    reviewed ? workflow.stages.filter((candidate) => candidate.scorecard) : timed ? workflow.stages.filter(hasTimeLimit) : workflow.stages,
  );
  const stage = $derived(stageOptions.some((candidate) => candidate.id === stageId) ? stageId : (stageOptions[0]?.id ?? ''));
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
      reviewerRecommendation: (reviewed && recommendation) || null,
    };
    try {
      const response = await simulate.mutateAsync({
        data: {
          workflow,
          trigger: { type: triggerType, ...((triggerNeedsStage(triggerType) || ((reviewed || timed) && stage)) && { stageId: stage }) },
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
        {#if triggerNeedsStage(triggerType) || ((reviewed || timed) && stageOptions.length > 0)}
          <label class={labelClass}>
            Stage
            <select class={selectClass} value={stage} onchange={(event) => (stageId = event.currentTarget.value)}>
              {#each stageOptions as option (option.id)}<option value={option.id}>{option.name}</option>{/each}
            </select>
          </label>
        {/if}
        {#if reviewed}
          <label class={labelClass}>
            Recommendation
            <select class={selectClass} bind:value={recommendation}>
              <option value="">Not given</option>
              {#each recommendations as value (value)}<option {value}>{recommendationLabels[value]}</option>{/each}
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
        <label class="flex items-center gap-2 pointer-coarse:min-h-11 font-semibold">
          <input type="checkbox" class={checkboxClass} bind:checked={accommodation} />
          The candidate asked for an accommodation (timing facts are off)
        </label>
      </fieldset>

      <div><Button type="submit" variant="outline" size="lg" disabled={simulate.isPending}>Run test</Button></div>
    </form>
  </Panel>

  <Panel title="What would happen" aria-live="polite" class="flex-[1_1_320px]">
    {#if failure}
      <div>
        <FailureText message={failure.message} />
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
                {#each automation.actions as action, actionIndex (actionIndex)}<li>{describeAction(action, workflow, undefined, templateNames)}</li>{/each}
              </ol>
            {/if}
          </li>
        {/each}
      </ol>
    {/if}
  </Panel>
</div>
