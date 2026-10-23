<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { aiUseLabels, errorsAt, followUpQuestionTypes, followUpsOf, newFollowUp, questionTypes } from '#lib/workflow.js';
  import { labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import QuestionList from './question-list.svelte';
  import QuestionnairePreview from './questionnaire-preview.svelte';

  // The apply form's questions and AI-use policy, and the follow-up questionnaires an automation sends after someone
  // applied, each beside a live preview of what the candidate sees. One questionnaire shows at a time; the toggles are
  // peer views of the workflow's questionnaires (design system section 11).
  let { workflow = $bindable(), errors }: { workflow: HiringWorkflow; errors: readonly DefinitionError[] } = $props();

  /** `apply`, or the id of the follow-up questionnaire on show. */
  let selected = $state('apply');

  const followUps = $derived(followUpsOf(workflow));
  const index = $derived(followUps.findIndex((followUp) => followUp.id === selected));
  const current = $derived(index >= 0 ? followUps[index] : undefined);
  const path = $derived(`followUpQuestionnaires[${index}]`);

  function addFollowUp() {
    const followUp = newFollowUp(workflow);
    workflow.followUpQuestionnaires = [...followUps, followUp];
    selected = followUp.id;
  }

  function removeFollowUp() {
    workflow.followUpQuestionnaires = followUps.filter((followUp) => followUp.id !== selected);
    selected = 'apply';
  }
</script>

<div class="flex flex-col gap-5">
  <div class="flex min-w-0 flex-wrap items-center gap-2">
    <div role="group" aria-label="Questionnaire" class="flex min-w-0 gap-1 overflow-x-auto">
      <Toggle class="shrink-0" bind:pressed={() => selected === 'apply', (pressed) => pressed && (selected = 'apply')}>
        Apply form
      </Toggle>
      {#each followUps as followUp (followUp.id)}
        <Toggle class="shrink-0" bind:pressed={() => selected === followUp.id, (pressed) => pressed && (selected = followUp.id)}>
          {followUp.name || followUp.id}
        </Toggle>
      {/each}
    </div>
    <Button variant="outline" onclick={addFollowUp}><PlusIcon aria-hidden="true" />Add follow-up questionnaire</Button>
  </div>
  <FieldErrors id="follow-ups-errors" messages={errorsAt(errors, 'followUpQuestionnaires')} />

  <div class="flex flex-wrap items-start gap-5">
    <div class="flex min-w-0 flex-[999_1_520px] flex-col gap-5">
      {#if current}
        <Panel title="Follow-up questionnaire">
          {#snippet actions()}
            <!-- A short label, so the panel title keeps its row at phone width (F5); the accessible name says what it removes. -->
            <Button variant="outline" aria-label="Remove questionnaire" onclick={removeFollowUp}>Remove</Button>
          {/snippet}
          <div class="flex flex-col gap-1.5">
            <label class={labelClass}>
              <span>Name <span class="font-mono text-code font-normal text-muted-foreground">{current.id}</span></span>
              <Input bind:value={current.name} maxlength={80} aria-describedby="follow-up-note" aria-invalid={errorsAt(errors, `${path}.name`).length > 0 || undefined} />
            </label>
            <FieldErrors id="follow-up-name-errors" messages={[...errorsAt(errors, path), ...errorsAt(errors, `${path}.id`), ...errorsAt(errors, `${path}.name`)]} />
            <p id="follow-up-note" class="text-muted-foreground">A "Send a questionnaire" automation emails the candidate a link to it.</p>
          </div>
        </Panel>
        <QuestionList
          bind:questions={() => current.questions, (next) => (current.questions = next)}
          {workflow}
          {path}
          types={followUpQuestionTypes}
          title="Questions"
          empty="Add at least one question."
          {errors}
        />
      {:else}
        <Panel title="AI use while applying">
          <div class="flex flex-col gap-1.5">
            <select class={selectClass} aria-label="AI use while applying" aria-describedby="ai-use-note" bind:value={workflow.integrityPolicy.aiUse}>
              {#each Object.entries(aiUseLabels) as [policy, label] (policy)}<option value={policy}>{label}</option>{/each}
            </select>
            <p id="ai-use-note" class="text-muted-foreground">Candidates see this before they start, and no automation uses their answer.</p>
          </div>
        </Panel>
        <QuestionList
          bind:questions={workflow.questionnaire.questions}
          {workflow}
          path="questionnaire"
          types={questionTypes}
          title="Screening questions"
          empty="No questions yet. Every form still asks for a resume."
          {errors}
        />
      {/if}
    </div>

    <Panel title="Candidate preview" class="flex-[1_1_320px] lg:sticky lg:top-4">
      {#if current}
        <QuestionnairePreview questions={current.questions} followUp />
      {:else}
        <QuestionnairePreview questions={workflow.questionnaire.questions} />
      {/if}
    </Panel>
  </div>
</div>
