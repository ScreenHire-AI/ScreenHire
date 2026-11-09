<script lang="ts">
  import { createGetOrganization, createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import type { AiUsePolicy, DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import { DEFAULT_QUESTIONS_PER_PAGE } from '@screenhire/ui/questions/model';
  import EditPreviewSwitch from '#lib/components/edit-preview-switch.svelte';
  import Panel from '#lib/components/panel.svelte';
  import PreviewFrame from '#lib/components/preview-frame.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import StepMarker from '#lib/components/step-marker.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import type { Question } from '@screenhire/api-client';
  import { emailTemplateNames, followUpsOf, isTerminal, questionTypeLabels, summarize } from '#lib/workflow.js';
  import type { WorkflowTab } from '#lib/workflow-tabs.js';
  import AiUseRow from './ai-use-row.svelte';
  import { rowClass } from './controls.js';
  import JourneyOverview from './journey-overview.svelte';
  import ProblemsPanel from './problems-panel.svelte';
  import QuestionnairePreview from './questionnaire-preview.svelte';
  import ScorecardName from './scorecard-name.svelte';
  import TestDrive from './test-drive.svelte';
  import StageFlow from './stage-flow.svelte';
  import WorkflowGraph from './workflow-graph.svelte';

  // A workflow that cannot be edited here (a ScreenHire template, a published version, a closed job's), read as content
  // (design system section 7, read-only view): the same views as the builder, as panes of the page's `Tabs.Root`, with
  // data as text and pills, never disabled inputs. The test drive still runs: it changes nothing.
  let {
    workflow,
    errors,
    tab,
    hrefOf,
    aiUse,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    tab: WorkflowTab;
    hrefOf: (tab: WorkflowTab) => string;
    /** The AI-use policy in force and where to change it, if the person may. Undefined while it loads. */
    aiUse?: { policy?: AiUsePolicy; href?: string };
  } = $props();
  // Names an AssignReviewer action's reviewer.
  const team = createListTeamMembers();

  // The organization's template names, for "Send the ... email".
  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));

  const steps = $derived(workflow.stages.filter((stage) => !isTerminal(stage.kind)));
  const ends = $derived(workflow.stages.filter((stage) => isTerminal(stage.kind)));
  const questions = $derived(workflow.questionnaire.questions);
  // The preview pages the questions as the candidate meets them: the sections, or the organization's page size.
  const organization = createGetOrganization();
  const questionsPerPage = $derived(organization.data?.data.questionsPerPage ?? DEFAULT_QUESTIONS_PER_PAGE);
  const followUps = $derived(followUpsOf(workflow));
  const scored = $derived(workflow.stages.filter((stage) => stage.scorecard || stage.scorecardTemplateId));
  let mode = $state<'edit' | 'preview'>('edit');
</script>

<!-- A stored version that today's validator refuses: say why, as the badge counts it. -->
<ProblemsPanel title="Problems" {workflow} {errors} />

{#snippet endStages()}
  <div class="flex flex-wrap items-center gap-1.5">
    <span class="mr-1 text-caption text-muted-foreground">End stages</span>
    {#each ends as stage (stage.id)}<Badge variant="muted">{stage.name}</Badge>{/each}
  </div>
{/snippet}

<Tabs.Content value="overview">
  <JourneyOverview {workflow} questionsHref={hrefOf('questions')} />
</Tabs.Content>

<Tabs.Content value="pipeline">
  <div class="flex flex-wrap items-start gap-5">
    <!-- A row of its own, so the stages have the page's width to sit side by side in. -->
    <Panel title="Stages" footer={ends.length > 0 ? endStages : undefined} class="flex-[1_1_100%]">
      <StageFlow stages={steps} automations={workflow.automations} />
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
                {#if automation.enabled === false}<StatusBadge status="notInEffect" label="Off" quiet />{/if}
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
              <p class="text-muted-foreground"><ScorecardName {stage} /></p>
              <ol class="flex flex-col gap-1.5">
                {#each stage.scorecard?.questions ?? [] as question (question.id)}
                  <li class="flex flex-col">
                    <span class="font-semibold break-words">{question.label}</span>
                    {#if question.guidance}<span class="text-muted-foreground">{question.guidance}</span>{/if}
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

<Tabs.Content value="automations">
  <!-- Mounted only while shown, as in the builder: the graph measures its nodes when it mounts. -->
  <Panel title="Stages and automations" flush>
    {#if tab === 'automations'}<WorkflowGraph {workflow} {errors} />{/if}
  </Panel>
</Tabs.Content>

{#snippet questionList(list: readonly Question[])}
  <ol class="flex flex-col">
    {#each list as question, index (question.id)}
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
{/snippet}

<Tabs.Content value="questions">
  <!-- Content and the candidate's form never share the screen (A8): the switch swaps them in place. -->
  <div class="flex flex-col gap-5">
    <EditPreviewSwitch bind:mode readOnly />
    {#if mode === 'preview'}
      <Panel title="Preview">
        <PreviewFrame>
          <QuestionnairePreview {questions} sections={workflow.questionnaire.sections} {questionsPerPage} aiUse={aiUse?.policy} />
        </PreviewFrame>
      </Panel>
    {:else}
      <Panel title="Screening questions" count={questions.length}>
        {#if questions.length === 0}
          <p class="text-muted-foreground">No questions. Every form still asks for a resume.</p>
        {:else}
          {@render questionList(questions)}
        {/if}
        <div class="mt-4"><AiUseRow policy={aiUse?.policy} href={aiUse?.href} /></div>
      </Panel>
      {#each followUps as followUp (followUp.id)}
        <Panel title={`Follow-up: ${followUp.name || followUp.id}`} count={followUp.questions.length}>
          {@render questionList(followUp.questions)}
        </Panel>
      {/each}
    {/if}
  </div>
</Tabs.Content>

<Tabs.Content value="test">
  <TestDrive {workflow} {errors} automationsHref={hrefOf('automations')} onfix={() => {}} editable={false} />
</Tabs.Content>
