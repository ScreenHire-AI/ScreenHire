<script lang="ts">
  import type { HiringWorkflow, SimulateWorkflowRequest, TriggerType, WorkflowSimulationResult } from '@screenhire/api-client';
  import { ApiError } from '@screenhire/api-client/svelte-query';
  import { tick } from 'svelte';
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import FlaskConicalIcon from '~icons/lucide/flask-conical';
  import FailureText from '#lib/components/failure-text.svelte';
  import StepLine from '#lib/components/step-line.svelte';
  import StepMarkers, { type Step as Progress } from '#lib/components/step-markers.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';
  import {
    blankCandidate,
    describeMoment,
    factsOf,
    followUpFor,
    hasFields,
    listenersOf,
    momentsOf,
    needsAnswer,
    presetsFor,
    readsOf,
    timelineOf,
    triggerFor,
    type SampleCandidate,
  } from '#lib/simulate.js';
  import { aiAssistanceLevels, authenticityLevels, describePath, emailTemplateNames, problemErrors, recommendationLabels, recommendations, sources } from '#lib/workflow.js';
  import { definitionStamp } from '#lib/workflow-graph.js';
  import { checkboxClass, labelClass, selectClass } from './controls.js';
  import OptionCard from './option-card.svelte';

  // The guided test of a workflow (workflow-engine.md section 4, design system "Guided test"): three questions, one on
  // the screen at a time. What happened, who is the candidate, then what would happen. It runs on the workflow as it
  // is on screen, unsaved changes included, and touches no real candidate. `run` is the simulate endpoint; the panel that
  // mounts this wires it, and the style guide gives it a canned answer.
  let {
    workflow,
    run,
    templateNames = emailTemplateNames(undefined),
    onshow,
    onadd,
    onpipeline,
  }: {
    workflow: HiringWorkflow;
    run: (request: SimulateWorkflowRequest) => Promise<WorkflowSimulationResult>;
    /** The organization's email template names, for "Send the ... email". */
    templateNames?: ReadonlyMap<string, string>;
    /** With it, a result offers to light the path it took on the graph. */
    onshow?: (result: WorkflowSimulationResult) => void;
    /** With it, a workflow with no automations offers to add one. A read-only version has none to offer. */
    onadd?: () => void;
    /** With it, a workflow whose automations are all off offers to open Pipeline, where they are switched on. */
    onpipeline?: () => void;
  } = $props();

  type Screen = 'moment' | 'question' | 'candidate' | 'customize' | 'result';
  let screen = $state<Screen>('moment');
  let momentType = $state<TriggerType | null>(null);
  // The stage or questionnaire chosen for a moment that asked.
  let answer = $state<string | null>(null);
  // The candidate chosen: a ready-made one's id, or 'custom' for the one the person described.
  let choice = $state<string | null>(null);
  let custom = $state<SampleCandidate>(blankCandidate());
  let draft = $state<SampleCandidate>(blankCandidate());

  const moments = $derived(momentsOf(workflow));
  const followUp = $derived(momentType ? followUpFor(workflow, momentType) : null);
  const trigger = $derived(momentType ? triggerFor(workflow, momentType, answer) : null);
  const listeners = $derived(trigger ? listenersOf(workflow, trigger) : []);
  const reads = $derived(readsOf(workflow, listeners));
  const presets = $derived(trigger ? presetsFor(workflow, trigger, listeners) : []);
  const chosen = $derived(choice === 'custom' ? null : presets.find((preset) => preset.id === choice));
  const candidate = $derived(choice === 'custom' ? custom : (chosen?.candidate ?? presets[0]?.candidate ?? blankCandidate()));
  const candidateName = $derived(choice === 'custom' ? 'A candidate you described' : (chosen?.name ?? ''));

  // An edit can take the moment away (its automation was removed or switched off); the guide then starts again.
  const stillOffered = $derived(momentType !== null && moments.some((moment) => moment.type === momentType));
  const shown = $derived<Screen>(screen === 'moment' || stillOffered ? screen : 'moment');

  const headings: Record<Screen, string> = {
    moment: 'What happened?',
    question: 'Which one?',
    candidate: 'Who is the candidate?',
    customize: 'Describe the candidate',
    result: 'What would happen',
  };
  const heading = $derived(shown === 'question' ? (followUp?.title ?? headings.question) : headings[shown]);

  const place = $derived(shown === 'moment' || shown === 'question' ? 0 : shown === 'candidate' || shown === 'customize' ? 1 : 2);
  const progress = $derived<Progress[]>(
    ['Moment', 'Candidate', 'Result'].map((label, index): Progress => ({ label, state: index < place ? 'done' : index === place ? 'current' : 'upcoming' })),
  );

  // What a test found, kept with the workflow it ran on, so a later edit does not rewrite the answer on screen.
  let ran = $state<{ workflow: HiringWorkflow; trigger: NonNullable<typeof trigger>; result: WorkflowSimulationResult; stamp: string; who: string } | null>(null);
  let pending = $state(false);
  let failure = $state<{ message: string; problems: string[] } | null>(null);
  const timeline = $derived(ran ? timelineOf(ran.workflow, ran.trigger, ran.result, templateNames) : []);
  const happening = $derived(timeline.filter((step) => step.ran));
  const notHappening = $derived(timeline.filter((step) => !step.ran));
  // The tabs stay mounted, so a person can edit in another tab and come back to an answer from before the edit.
  const outdated = $derived(ran !== null && ran.stamp !== definitionStamp(workflow));

  // Each question takes the focus when it appears, so a keyboard or screen reader user lands on it.
  let headingElement = $state<HTMLElement>();
  let first = true;
  $effect(() => {
    void shown;
    if (first) {
      first = false;
      return;
    }
    void tick().then(() => headingElement?.focus());
  });

  function chooseMoment(type: TriggerType) {
    momentType = type;
    answer = null;
    if (needsAnswer(followUpFor(workflow, type))) screen = 'question';
    else proceed();
  }

  function chooseAnswer(id: string) {
    answer = id;
    proceed();
  }

  // A person who already chose a candidate keeps them for another moment, when that candidate still exists for it.
  function proceed() {
    if (choice === 'custom' || presets.some((preset) => preset.id === choice)) void test();
    else {
      choice = null;
      screen = 'candidate';
    }
  }

  function chooseCandidate(id: string) {
    choice = id;
    void test();
  }

  function customize() {
    draft = structuredClone($state.snapshot(candidate));
    screen = 'customize';
  }

  function testDraft() {
    custom = structuredClone($state.snapshot(draft));
    choice = 'custom';
    void test();
  }

  function back() {
    if (shown === 'customize') screen = 'candidate';
    else if (shown === 'candidate' && needsAnswer(followUp)) screen = 'question';
    else screen = 'moment';
  }

  async function test() {
    if (!trigger) return;
    const sent = trigger;
    const snapshot = $state.snapshot(workflow) as HiringWorkflow;
    const stamp = definitionStamp(workflow);
    failure = null;
    pending = true;
    screen = 'result';
    try {
      const result = await run({ workflow: snapshot, trigger: sent, facts: factsOf(candidate), accommodationRequested: candidate.accommodation });
      ran = { workflow: snapshot, trigger: sent, result, stamp, who: candidateName };
    } catch (error) {
      ran = null;
      failure =
        error instanceof ApiError && error.status === 400
          ? {
              message: 'Fix the problems in this workflow first: a workflow that cannot be saved cannot be tested.',
              problems: problemErrors(error.body).map((problem) => `${describePath(workflow, problem.path)}: ${problem.message}`),
            }
          : { message: describeError(error, 'Could not run the test'), problems: [] };
    } finally {
      pending = false;
    }
  }

  const setAnswer = (id: string, value: string) => (draft.answers[id] = value ? [value] : []);
  function toggleOption(id: string, optionId: string, on: boolean) {
    const rest = (draft.answers[id] ?? []).filter((value) => value !== optionId);
    draft.answers[id] = on ? [...rest, optionId] : rest;
  }

  const uid = $props.id();
</script>

{#if workflow.automations.length === 0}
  <Empty.Root>
    <Empty.Header>
      <Empty.Media variant="icon"><FlaskConicalIcon /></Empty.Media>
      <Empty.Title>Nothing to test yet</Empty.Title>
      <Empty.Description>This workflow has no automations.</Empty.Description>
    </Empty.Header>
    {#if onadd}<Empty.Content><Button variant="outline" size="lg" onclick={onadd}>Add an automation</Button></Empty.Content>{/if}
  </Empty.Root>
{:else if moments.length === 0}
  <Empty.Root>
    <Empty.Header>
      <Empty.Media variant="icon"><FlaskConicalIcon /></Empty.Media>
      <Empty.Title>Nothing to test yet</Empty.Title>
      <Empty.Description>Every automation is switched off.</Empty.Description>
    </Empty.Header>
    {#if onpipeline}<Empty.Content><Button variant="outline" size="lg" onclick={onpipeline}>Open Pipeline</Button></Empty.Content>{/if}
  </Empty.Root>
{:else}
  <div class="@container flex flex-col gap-5">
    <StepMarkers label="Progress of the test" steps={progress} />

    <div class="flex flex-col gap-1">
      <h3 bind:this={headingElement} tabindex="-1" class="text-heading focus-visible:outline-none">{heading}</h3>
      {#if shown === 'moment'}
        <p class="max-w-[72ch] text-muted-foreground">The test runs on this workflow as it is on screen. It never touches a real candidate or sends anything.</p>
      {/if}
    </div>

    {#if shown === 'moment'}
      <ul class="flex flex-col gap-2" aria-label="Moments">
        {#each moments as moment (moment.type)}
          <li>
            <OptionCard
              title={moment.label}
              description={`${moment.automations} ${moment.automations === 1 ? 'automation reacts' : 'automations react'}`}
              current={moment.type === momentType}
              onclick={() => chooseMoment(moment.type)}
            />
          </li>
        {/each}
      </ul>
    {:else if shown === 'question' && followUp}
      <ul class="flex flex-col gap-2" aria-label={followUp.kind === 'stage' ? 'Stages' : 'Questionnaires'}>
        {#each followUp.options as option (option.id)}
          <li><OptionCard title={option.name} current={option.id === answer} onclick={() => chooseAnswer(option.id)} /></li>
        {/each}
      </ul>
      <div><Button variant="outline" size="lg" onclick={back}><ArrowLeftIcon />Back</Button></div>
    {:else if shown === 'candidate'}
      <ul class="flex flex-col gap-2" aria-label="Candidates">
        {#each presets as preset (preset.id)}
          <li><OptionCard title={preset.name} description={preset.description} current={preset.id === choice} onclick={() => chooseCandidate(preset.id)} /></li>
        {/each}
      </ul>
      <div class="flex flex-wrap gap-2">
        <Button variant="outline" size="lg" onclick={back}><ArrowLeftIcon />Back</Button>
        {#if hasFields(reads)}<Button variant="outline" size="lg" onclick={customize}>Customize</Button>{/if}
      </div>
    {:else if shown === 'customize'}
      <!-- Only what this workflow's automations read. A fact nothing reads never appears (W6). -->
      <form
        aria-label="Describe the candidate"
        class="flex max-w-[43rem] flex-col gap-4"
        onsubmit={(event) => {
          event.preventDefault();
          testDraft();
        }}
      >
        {#each reads.questions as question (question.id)}
          {#if question.type === 'MultiChoice'}
            <!-- Picked by the option's name, never typed as an id (W8); the answer is kept as the ids the API takes. -->
            <fieldset class="flex flex-col gap-1">
              <legend class={labelClass}>{question.label}</legend>
              {#each question.options ?? [] as option (option.id)}
                <label class="flex items-center gap-2 pointer-coarse:min-h-11">
                  <input
                    type="checkbox"
                    class={checkboxClass}
                    checked={(draft.answers[question.id] ?? []).includes(option.id)}
                    onchange={(event) => toggleOption(question.id, option.id, event.currentTarget.checked)}
                  />
                  {option.label}
                </label>
              {/each}
            </fieldset>
          {:else}
            <label class={labelClass}>
              {question.label}
              {#if question.type === 'YesNo' || question.type === 'SingleChoice'}
                <select class={selectClass} value={draft.answers[question.id]?.[0] ?? ''} onchange={(event) => setAnswer(question.id, event.currentTarget.value)}>
                  <option value="">Not answered</option>
                  {#if question.type === 'YesNo'}
                    <option value="yes">Yes</option>
                    <option value="no">No</option>
                  {:else}
                    {#each question.options ?? [] as option (option.id)}<option value={option.id}>{option.label}</option>{/each}
                  {/if}
                </select>
              {:else}
                <Input type={question.type === 'Number' ? 'number' : 'text'} value={draft.answers[question.id]?.[0] ?? ''} oninput={(event) => setAnswer(question.id, event.currentTarget.value)} />
              {/if}
            </label>
          {/if}
        {/each}

        {#if reads.tags}
          <div class="flex min-w-0 flex-col gap-1">
            <label class={labelClass}>Tags<Input bind:value={draft.tags} aria-describedby={`${uid}-tags`} /></label>
            <p id={`${uid}-tags`} class="text-muted-foreground">Separated by commas.</p>
          </div>
        {/if}
        {#if reads.source}
          <label class={labelClass}>
            Source
            <select class={selectClass} bind:value={draft.source}>
              <option value="">Not given</option>
              {#each sources as option (option.value)}<option value={option.value}>{option.label}</option>{/each}
            </select>
          </label>
        {/if}
        {#if reads.hoursInStage}
          <label class={labelClass}>Hours in the stage<Input type="number" min={0} bind:value={draft.hoursInStage} /></label>
        {/if}
        {#if reads.recommendation}
          <label class={labelClass}>
            Scorecard recommendation
            <select class={selectClass} bind:value={draft.recommendation}>
              <option value="">Not given</option>
              {#each recommendations as value (value)}<option {value}>{recommendationLabels[value]}</option>{/each}
            </select>
          </label>
        {/if}
        <!-- Verification levels are words and only ever route a candidate to a person (ADR-0004). -->
        {#if reads.authenticity}
          <label class={labelClass}>
            Authenticity check
            <select class={selectClass} bind:value={draft.authenticity}>
              <option value="">Not assessed</option>
              {#each authenticityLevels as level (level.value)}<option value={level.value}>{level.label}</option>{/each}
            </select>
          </label>
        {/if}
        {#if reads.aiAssistance}
          <label class={labelClass}>
            AI assistance check
            <select class={selectClass} bind:value={draft.aiAssistance}>
              <option value="">Not assessed</option>
              {#each aiAssistanceLevels as level (level.value)}<option value={level.value}>{level.label}</option>{/each}
            </select>
          </label>
        {/if}
        {#if reads.timing}
          <div class="flex flex-col gap-0.5">
            <label class="flex items-center gap-2 pointer-coarse:min-h-11 font-semibold">
              <input type="checkbox" class={checkboxClass} bind:checked={draft.accommodation} aria-describedby={`${uid}-accommodation`} />
              Asked for an accommodation
            </label>
            <p id={`${uid}-accommodation`} class="pl-6 text-muted-foreground">Timing is not used for them.</p>
          </div>
        {/if}

        <div class="flex flex-wrap gap-2">
          <Button type="button" variant="outline" size="lg" onclick={back}><ArrowLeftIcon />Back</Button>
          <Button type="submit" variant="outline" size="lg">Run the test</Button>
        </div>
      </form>
    {:else}
      <div class="flex flex-col gap-4">
        {#if pending}
          <div class="flex flex-col gap-3">
            <p role="status" class="sr-only">Running the test.</p>
            <Skeleton class="h-5 w-1/2" />
            <Skeleton class="h-5 w-1/3" />
            <Skeleton class="h-12 w-full" />
          </div>
        {:else if failure}
          <div>
            <FailureText message={failure.message} />
            {#if failure.problems.length > 0}
              <ul class="mt-1.5 flex list-disc flex-col gap-0.5 pl-5">{#each failure.problems as problem (problem)}<li>{problem}</li>{/each}</ul>
            {/if}
          </div>
        {:else if ran}
          <div class="flex flex-col">
            <p class="font-semibold">{describeMoment(ran.workflow, ran.trigger)}</p>
            <p class="text-muted-foreground">{ran.who}</p>
          </div>
          {#if happening.length > 0}
            <StepLine items={happening} label="What would happen, in order" key={(step) => step.id}>
              {#snippet children(step)}
                <span class="font-semibold">{step.name}</span>
                {#if step.actions.length > 0}
                  <ol class="flex flex-col gap-0.5">{#each step.actions as action, index (index)}<li>{action}</li>{/each}</ol>
                {/if}
              {/snippet}
            </StepLine>
          {:else}
            <p>Nothing would run.</p>
          {/if}
          {#if notHappening.length > 0}
            <!-- Not steps: nothing happens, so they are not numbered into the line above. -->
            <div class="flex flex-col gap-2">
              <h4 class="text-muted-foreground">Would not run</h4>
              <ul class="flex flex-col gap-2">
                {#each notHappening as step (step.id)}
                  <li class="flex flex-col"><span class="font-semibold">{step.name}</span><span class="text-muted-foreground">{step.why}</span></li>
                {/each}
              </ul>
            </div>
          {/if}
          {#if outdated}<p class="text-muted-foreground">The workflow changed after this test.</p>{/if}
        {/if}
      </div>
      {#if !pending}
        <div class="flex flex-wrap gap-2">
          <Button variant="outline" size="lg" onclick={() => (screen = 'candidate')}>Change the candidate</Button>
          <Button variant="outline" size="lg" onclick={() => (screen = 'moment')}>Try another moment</Button>
          {#if outdated}
            <Button variant="outline" size="lg" onclick={proceed}>Run it again</Button>
          {:else if onshow && ran}
            <Button variant="outline" size="lg" onclick={() => onshow(ran!.result)}>Show this path in the graph</Button>
          {/if}
        </div>
      {/if}
    {/if}
  </div>
{/if}
