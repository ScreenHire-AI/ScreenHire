<script lang="ts">
  import { createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import Panel from '#lib/components/panel.svelte';
  import StepLine from '#lib/components/step-line.svelte';
  import StepMarker from '#lib/components/step-marker.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { aiUseLabels, emailTemplateNames, isTerminal, questionTypeLabels, stageKindLabels, summarize } from '#lib/workflow.js';
  import { rowClass } from './controls.js';
  import ProblemsPanel from './problems-panel.svelte';
  import QuestionnairePreview from './questionnaire-preview.svelte';
  import SimulatePanel from './simulate-panel.svelte';
  import WorkflowGraph from './workflow-graph.svelte';

  // A workflow that cannot be edited here (a ScreenHire template, a published version, a closed job's), read as content
  // (design system section 7, read-only view): the same views as the builder, as panes of the page's `Tabs.Root`, with
  // data as text and pills, never disabled inputs. Simulate still runs: it changes nothing.
  let { workflow, errors, tab }: { workflow: HiringWorkflow; errors: readonly DefinitionError[]; tab: string } = $props();
  // Names an AssignReviewer action's reviewer.
  const team = createListTeamMembers();

  // The organization's template names, for "Send the ... email".
  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));

  const steps = $derived(workflow.stages.filter((stage) => !isTerminal(stage.kind)));
  const ends = $derived(workflow.stages.filter((stage) => isTerminal(stage.kind)));
  const questions = $derived(workflow.questionnaire.questions);
  const scored = $derived(workflow.stages.filter((stage) => stage.scorecard));
  const days = (count: number) => `${count}-day limit`;
</script>

<!-- A stored version that today's validator refuses: say why, as the badge counts it. -->
<ProblemsPanel title="Problems" {workflow} {errors} />

{#snippet endStages()}
  <div class="flex flex-wrap items-center gap-1.5">
    <span class="mr-1 text-caption text-muted-foreground">End stages</span>
    {#each ends as stage (stage.id)}<Badge variant="muted">{stage.name}</Badge>{/each}
  </div>
{/snippet}

<Tabs.Content value="pipeline">
  <div class="flex flex-wrap items-start gap-5">
    <Panel title="Stages" count={workflow.stages.length} footer={ends.length > 0 ? endStages : undefined} class="flex-[1_1_320px]">
      <StepLine items={steps} label="Stages in order" key={(stage) => stage.id}>
        {#snippet children(stage)}
          <span class="font-semibold break-words">{stage.name}</span>
          <!-- A kind that only repeats the stage's name is said once (W4). -->
          {#if stageKindLabels[stage.kind] !== stage.name || stage.slaDays}
            <span class="flex flex-wrap gap-1.5">
              {#if stageKindLabels[stage.kind] !== stage.name}<Badge variant="muted">{stageKindLabels[stage.kind]}</Badge>{/if}
              {#if stage.slaDays}<Badge variant="muted" class="tabular-nums">{days(stage.slaDays)}</Badge>{/if}
            </span>
          {/if}
        {/snippet}
      </StepLine>
    </Panel>

    <Panel title="Automations" count={workflow.automations.length} class="flex-[999_1_520px]">
      {#if workflow.automations.length === 0}
        <p class="text-muted-foreground">No automations.</p>
      {:else}
        <ul class="flex flex-col">
          {#each workflow.automations as automation (automation.id)}
            <li class={['flex flex-col gap-1', rowClass]}>
              <span class="flex flex-wrap items-center gap-2">
                <span class="font-semibold break-words">{automation.name}</span>
                {#if automation.enabled === false}<Badge variant="muted">Off</Badge>{/if}
              </span>
              <span class={automation.enabled === false ? 'text-muted-foreground' : 'text-secondary-foreground'}>{summarize(automation, workflow, team.data?.data, templateNames)}</span>
            </li>
          {/each}
        </ul>
      {/if}
    </Panel>

    <Panel title="Scorecards" count={scored.length} class="flex-[1_1_100%]">
      {#if scored.length === 0}
        <p class="text-muted-foreground">No scorecards.</p>
      {:else}
        <ul class="flex flex-col">
          {#each scored as stage (stage.id)}
            <li class={['flex flex-col gap-2', rowClass]}>
              <h3 class="text-heading break-words">{stage.name}</h3>
              <ol class="flex flex-col gap-1.5">
                {#each stage.scorecard?.criteria ?? [] as criterion (criterion.id)}
                  <li class="flex flex-col">
                    <span class="font-semibold break-words">{criterion.label}</span>
                    {#if criterion.guidance}<span class="text-muted-foreground">{criterion.guidance}</span>{/if}
                  </li>
                {/each}
              </ol>
            </li>
          {/each}
        </ul>
      {/if}
    </Panel>
  </div>
</Tabs.Content>

<Tabs.Content value="graph">
  <!-- Mounted only while shown, as in the builder: the graph fits its view to the size it mounts at. -->
  <Panel title="Stages and automations" flush>
    {#if tab === 'graph'}<WorkflowGraph {workflow} {errors} />{/if}
  </Panel>
</Tabs.Content>

<Tabs.Content value="questionnaire">
  <div class="flex flex-wrap items-start gap-5">
    <div class="flex min-w-0 flex-[999_1_520px] flex-col gap-5">
      <Panel title="AI use while applying">
        <p>{aiUseLabels[workflow.integrityPolicy.aiUse]}</p>
      </Panel>
      <Panel title="Screening questions" count={questions.length}>
        {#if questions.length === 0}
          <p class="text-muted-foreground">No questions. Every form still asks for a resume.</p>
        {:else}
          <ol class="flex flex-col">
            {#each questions as question, index (question.id)}
              <li class={['flex gap-3', rowClass]}>
                <StepMarker>{index + 1}</StepMarker>
                <div class="flex min-w-0 flex-col gap-1.5 pt-1">
                  <span class="font-semibold break-words">{question.label}</span>
                  {#if question.helpText}<span class="text-muted-foreground">{question.helpText}</span>{/if}
                  <span class="flex flex-wrap gap-1.5">
                    <Badge variant="muted">{questionTypeLabels[question.type]}</Badge>
                    {#if question.required}<Badge variant="muted">Required</Badge>{/if}
                    {#if question.knockout}<Badge variant="muted">Knockout if {question.knockout.disqualifyingAnswer ? 'yes' : 'no'}</Badge>{/if}
                    {#each question.options ?? [] as option (option.id)}<Badge variant="muted">{option.label}</Badge>{/each}
                  </span>
                  {#if question.knockout?.reason}<span class="text-muted-foreground">{question.knockout.reason}</span>{/if}
                </div>
              </li>
            {/each}
          </ol>
        {/if}
      </Panel>
    </div>
    <Panel title="Candidate preview" class="flex-[1_1_320px] lg:sticky lg:top-4">
      <QuestionnairePreview {questions} />
    </Panel>
  </div>
</Tabs.Content>

<Tabs.Content value="simulate">
  <SimulatePanel {workflow} />
</Tabs.Content>
