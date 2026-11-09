<script lang="ts">
  import type { DefinitionError, HiringWorkflow, WorkflowSimulationResult } from '@screenhire/api-client';
  import { createListEmailTemplates, createSimulateWorkflow } from '@screenhire/api-client/svelte-query';
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import FlaskConicalIcon from '~icons/lucide/flask-conical';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StepMarker from '#lib/components/step-marker.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';
  import { outcomeOf, samplesOf, type Outcome, type Sample } from '#lib/test-drive.js';
  import { emailTemplateNames } from '#lib/workflow.js';
  import GuidedTest from './guided-test.svelte';
  import ProblemNotice from './problem-notice.svelte';

  // The Test tab (design system section 11, Test drive): four sample candidates built from this template, run through the
  // simulate endpoint a moment after each change, and what happens to each in a few plain steps. Nothing is saved or sent.
  // While a problem blocks the workflow it shows only the notice with its fix; once it is valid the samples run again by
  // themselves. "Try your own candidate" keeps the guided test for anyone who wants to choose the facts. A step's sample can
  // be shown on the Automations tab (`onshow`, a link to it).
  let {
    workflow = $bindable(),
    errors,
    checking = false,
    onshow,
    onadd,
    onfix,
    automationsHref,
    delayMs = 400,
    editable = true,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    /** The validator's check of the latest change is on its way. */
    checking?: boolean;
    /** Lights the path a result took on the Automations tab. */
    onshow?: (result: WorkflowSimulationResult) => void;
    /** Adds an automation, for a workflow that has none. */
    onadd?: () => void;
    /** Opens the place a problem is in. */
    onfix: (path: string) => void;
    /** The Automations tab, for the "Show in the map" link. */
    automationsHref: string;
    delayMs?: number;
    /** False for a version that cannot be changed: a problem is shown, with no fix. */
    editable?: boolean;
  } = $props();

  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));
  const simulate = createSimulateWorkflow();

  type Run = { sample: Sample; result: WorkflowSimulationResult; workflow: HiringWorkflow };
  type Answer = Run & { outcome: Outcome };
  let runs = $state.raw<Run[]>([]);
  let ranFor = $state('');
  let failure = $state('');
  let own = $state(false);

  // The steps are worded from the email templates' current names, so a list that loads after the run still names them.
  const answers = $derived<Answer[]>(
    runs.map((run) => ({ ...run, outcome: outcomeOf(run.workflow, run.result, templateNames, run.sample.id === 'accommodation') })),
  );

  // The whole workflow, not only its stages and automations: the samples come from the questionnaire too (a knockout
  // question is the "Fails" sample), so a change on the Questions tab runs them again.
  const stampOf = (value: HiringWorkflow) => JSON.stringify(value);
  const stamp = $derived(stampOf(workflow));
  const blocked = $derived(errors.length > 0);
  const running = $derived(!blocked && (checking || ranFor !== stamp) && failure === '');
  const hasAutomations = $derived(workflow.automations.some((automation) => automation.enabled !== false));

  // The samples run on the workflow as it is on screen, a moment after the last change, and an answer that arrives after a
  // newer change is dropped, as the validator's is. While a problem blocks the workflow nothing runs.
  $effect(() => {
    const current = stamp;
    if (blocked || checking || own || current === ranFor) return;
    const snapshot = $state.snapshot(workflow) as HiringWorkflow;
    const timer = setTimeout(async () => {
      try {
        const samples = samplesOf(snapshot);
        const results = await Promise.all(samples.map((sample) => simulate.mutateAsync({ data: sample.request })));
        if (current !== stampOf(workflow)) return;
        runs = samples.map((sample, index) => ({ sample, result: results[index]!.data, workflow: snapshot }));
        ranFor = current;
        failure = '';
      } catch (error) {
        if (current === stampOf(workflow)) failure = describeError(error, 'Could not run the samples');
      }
    }, delayMs);
    return () => clearTimeout(timer);
  });

  // An edit clears an earlier failure, so the next change tries again.
  $effect(() => {
    void stamp;
    failure = '';
  });
</script>

{#if own}
  <Panel title="Try your own candidate">
    {#snippet actions()}
      <Button variant="outline" onclick={() => (own = false)}><ArrowLeftIcon aria-hidden="true" />Back to the samples</Button>
    {/snippet}
    <GuidedTest {workflow} {templateNames} {onadd} run={async (data) => (await simulate.mutateAsync({ data })).data} onshow={onshow} />
  </Panel>
{:else}
  <Panel title="What happens to these candidates">
    {#snippet actions()}
      {#if !blocked && hasAutomations}<Button variant="outline" onclick={() => (own = true)}>Try your own candidate</Button>{/if}
    {/snippet}
    {#if blocked}
      <ProblemNotice bind:workflow {errors} {onfix} {editable} />
    {:else if !hasAutomations}
      <Empty.Root>
        <Empty.Header>
          <Empty.Media variant="icon"><FlaskConicalIcon /></Empty.Media>
          <Empty.Title>Nothing to test yet</Empty.Title>
          <Empty.Description>This workflow has no automations on.</Empty.Description>
        </Empty.Header>
        {#if onadd}<Empty.Content><Button variant="outline" size="lg" onclick={onadd}>Add an automation</Button></Empty.Content>{/if}
      </Empty.Root>
    {:else if failure}
      <FailureText message={failure} />
    {:else}
      <div aria-live="polite" aria-busy={running} class="flex flex-col gap-4">
        {#if answers.length === 0}
          <Skeleton class="h-24 w-full" />
        {:else}
          <ul class={['flex flex-col', running && 'opacity-60']}>
            {#each answers as { sample, result, outcome }, position (sample.id)}
              <li data-sample={sample.id} class="flex flex-col gap-2 border-b border-border-subtle py-4 first:pt-0 last:border-0 last:pb-0">
                <div class="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
                  <h3 class="text-heading break-words">{sample.name}</h3>
                  {#if position === 0 || answers[position - 1]?.sample.moment !== sample.moment}<span class="text-muted-foreground">{sample.moment}</span>{/if}
                </div>
                {#if sample.caption}<p class="text-muted-foreground">{sample.caption}</p>{/if}
                {#if outcome.steps.length > 0}
                  <ol class="flex flex-col gap-1.5">
                    {#each outcome.steps as step, index (index)}
                      <li class="flex items-center gap-2"><StepMarker>{index + 1}</StepMarker><span class="min-w-0 break-words">{step}</span></li>
                    {/each}
                  </ol>
                {/if}
                {#if outcome.note}<p class="text-muted-foreground">{outcome.note}</p>{/if}
                {#if onshow && result.automations.length > 0}
                  <p>
                    <a
                      href={automationsHref}
                      class="underline underline-offset-2 pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center"
                      onclick={(event) => {
                        event.preventDefault();
                        onshow(result);
                      }}>Show in the map<span class="sr-only">: {sample.name}</span></a
                    >
                  </p>
                {/if}
              </li>
            {/each}
          </ul>
          {#if answers.some((answer) => answer.sample.id === 'signals')}
            <p class="text-muted-foreground">A check never turns anyone down.</p>
          {/if}
        {/if}
      </div>
    {/if}
  </Panel>
{/if}
