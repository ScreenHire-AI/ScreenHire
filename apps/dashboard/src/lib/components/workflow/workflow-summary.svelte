<script lang="ts">
  import { createGetOrganization, createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import type { AiUsePolicy, DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import { DEFAULT_QUESTIONS_PER_PAGE } from '@screenhire/ui/questions/model';
  import EditPreviewSwitch from '#lib/components/edit-preview-switch.svelte';
  import Panel from '#lib/components/panel.svelte';
  import PreviewFrame from '#lib/components/preview-frame.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { emailTemplateNames, followUpsOf, isTerminal, summarize } from '#lib/workflow.js';
  import type { WorkflowTab } from '#lib/workflow-tabs.js';
  import { sendingOf } from '#lib/forms-timeline.js';
  import { anyStageChips, stageChips } from '#lib/stage-facts.js';
  import AiUseRow from './ai-use-row.svelte';
  import { rowClass } from './controls.js';
  import ProblemLines from './problem-lines.svelte';
  import QuestionList from './question-list.svelte';
  import QuestionnairePreview from './questionnaire-preview.svelte';
  import ScorecardName from './scorecard-name.svelte';
  import TryIt from './try-it.svelte';
  import StageChips from './stage-chips.svelte';
  import StageFlow from './stage-flow.svelte';
  import AutomationsPanel from './automations-panel.svelte';

  // A workflow that cannot be edited here (a ScreenHire template, a published version, a closed job's), read as content
  // (design system section 7, read-only view): the same views as the builder, as panes of the page's `Tabs.Root`, with
  // data as text and pills, never disabled inputs. Try it still runs: it changes nothing.
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

  const anyStage = $derived(anyStageChips(workflow, templateNames));
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
<ProblemLines label="Problems" {workflow} {errors} />

{#snippet endStages()}
  <div class="flex flex-col gap-4">
    {#if anyStage.automations.length > 0}
      <section aria-labelledby="summary-any-stage" class="flex flex-wrap items-center gap-x-4 gap-y-1.5">
        <h3 id="summary-any-stage" class="shrink-0 text-heading">Any stage</h3>
        <StageChips chips={anyStage} stageName="any stage" />
      </section>
    {/if}
    {#if ends.length > 0}
      <!-- The ends are quiet: each name, and what it sends counted, in one row under the flow. -->
      <section aria-labelledby="summary-ends" class="flex flex-wrap items-center gap-x-4 gap-y-2">
        <h3 id="summary-ends" class="shrink-0 text-heading">Ends</h3>
        <ul class="flex flex-wrap items-center gap-x-6 gap-y-2">
          {#each ends as stage (stage.id)}
            <li class="flex min-w-0 items-center gap-2" data-end-stage><span class="font-semibold break-words">{stage.name}</span><StageChips chips={stageChips(workflow, stage, templateNames)} stageName={stage.name} empty="" /></li>
          {/each}
        </ul>
      </section>
    {/if}
  </div>
{/snippet}

<Tabs.Content value="pipeline">
  <div class="flex flex-wrap items-start gap-5">
    <!-- A row of its own, so the stages have the page's width to sit side by side in. -->
    <Panel title="Stages" footer={ends.length > 0 || anyStage.automations.length > 0 ? endStages : undefined} class="@container flex-[1_1_100%]">
      <StageFlow {workflow} {templateNames} />
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
  {#if tab === 'automations'}<AutomationsPanel {workflow} {errors} />{/if}
</Tabs.Content>

<Tabs.Content value="forms">
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
      <Panel title="Application form" count={questions.length}>
        {#if questions.length === 0}
          <p class="text-muted-foreground">No questions. Every form still asks for a resume.</p>
        {:else}
          <QuestionList {questions} />
        {/if}
        <div class="mt-4"><AiUseRow policy={aiUse?.policy} href={aiUse?.href} /></div>
      </Panel>
      {#each followUps as followUp (followUp.id)}
        <Panel title={followUp.name || followUp.id} count={followUp.questions.length}>
          <p class="mb-3 text-muted-foreground">{sendingOf(workflow, followUp.id).words}</p>
          <QuestionList questions={followUp.questions} />
        </Panel>
      {/each}
    {/if}
  </div>
</Tabs.Content>

<Tabs.Content value="test">
  <TryIt {workflow} {errors} onfix={() => {}} editable={false} aiUse={aiUse?.policy} />
</Tabs.Content>
