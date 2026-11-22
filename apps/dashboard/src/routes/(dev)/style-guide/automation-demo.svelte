<script lang="ts">
  import type { HiringWorkflow } from '@screenhire/api-client';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import RecordTabs from '#lib/components/record-tabs.svelte';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import AutomationForm from '#lib/components/workflow/automation-form.svelte';
  import AutomationList from '#lib/components/workflow/automation-list.svelte';
  import AutomationName from '#lib/components/workflow/automation-name.svelte';
  import AutomationRecipes from '#lib/components/workflow/automation-recipes.svelte';
  import AutomationSentence from '#lib/components/workflow/automation-sentence.svelte';

  // The automation sheet's body (its two tabs over one Name and On), the automation sentence, the recipe list and the
  // automation list on one sample workflow. Each reads the organization's
  // emails and team through queries, so the demo brings its own cache, as the signed-in shell does for the real one. Each part
  // is its own copy of the sample.
  let { part }: { part: 'sheet' | 'sentence' | 'recipes' | 'list' } = $props();
  let view = $state('sentence');
  const viewTabs = [
    { value: 'sentence', label: 'Sentence' },
    { value: 'rules', label: 'Rules' },
  ];

  setQueryClientContext(new QueryClient({ defaultOptions: { queries: { retry: false } } }));

  let workflow = $state<HiringWorkflow>({
    stages: [
      { id: 'applied', name: 'Applied', kind: 'Applied' },
      { id: 'phone', name: 'Phone call', kind: 'Screening' },
      { id: 'trial', name: 'Trial shift', kind: 'Assessment' },
      { id: 'hired', name: 'Hired', kind: 'Hired' },
    ],
    questionnaire: { questions: [{ id: 'right_to_work', type: 'YesNo', label: 'Right to work', required: true, knockout: { disqualifyingAnswer: false, reason: null } }] },
    automations: [
      {
        id: 'review',
        name: 'Look closer when the checks suggest it',
        trigger: { type: 'VerificationAssessed' },
        conditions: { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'In', values: ['ReviewSuggested', 'VerificationRecommended'] }] },
        actions: [{ type: 'AssignReviewer' }, { type: 'AddTag', tag: 'Check closely' }],
      },
      { id: 'phone-mail', name: 'Tell people about the call', trigger: { type: 'StageEntered', stageId: 'phone' }, conditions: null, actions: [{ type: 'SendEmail', emailTemplate: 'stage-change' }] },
    ],
  });
  const nothing = () => {};
</script>

{#if part === 'sheet'}
  <Tabs.Root bind:value={view} class="flex flex-col gap-4">
    <RecordTabs tabs={viewTabs} label="Edit the automation as" class="border-b border-border-subtle" />
    <AutomationName bind:automation={workflow.automations[0]!} index={0} errors={[]} />
    <Tabs.Content value="sentence">
      {#if view === 'sentence'}<AutomationSentence bind:automation={workflow.automations[0]!} index={0} {workflow} errors={[]} />{/if}
    </Tabs.Content>
    <Tabs.Content value="rules">
      {#if view === 'rules'}<AutomationForm bind:automation={workflow.automations[0]!} index={0} {workflow} errors={[]} onremove={nothing} />{/if}
    </Tabs.Content>
  </Tabs.Root>
{:else if part === 'sentence'}
  <AutomationSentence bind:automation={workflow.automations[0]!} index={0} {workflow} errors={[]} />
{:else if part === 'recipes'}
  <AutomationRecipes bind:workflow onwrite={nothing} onedit={nothing} />
{:else}
  <AutomationList {workflow} onopen={nothing} />
{/if}
