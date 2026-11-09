<script lang="ts">
  import type { HiringWorkflow } from '@screenhire/api-client';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import AutomationList from '#lib/components/workflow/automation-list.svelte';
  import AutomationRecipes from '#lib/components/workflow/automation-recipes.svelte';
  import AutomationSentence from '#lib/components/workflow/automation-sentence.svelte';

  // The automation sentence, the recipe list and the automation list on one sample workflow. Each reads the organization's
  // emails and team through queries, so the demo brings its own cache, as the signed-in shell does for the real one. Each part
  // is its own copy of the sample.
  let { part }: { part: 'sentence' | 'recipes' | 'list' } = $props();

  setQueryClientContext(new QueryClient({ defaultOptions: { queries: { retry: false } } }));

  let workflow = $state<HiringWorkflow>({
    stages: [
      { id: 'applied', name: 'Applied', kind: 'Applied' },
      { id: 'phone', name: 'Phone call', kind: 'Screening' },
      { id: 'trial', name: 'Trial shift', kind: 'Assessment' },
      { id: 'hired', name: 'Hired', kind: 'Hired' },
    ],
    questionnaire: { questions: [{ id: 'right_to_work', type: 'YesNo', label: 'Right to work', required: true, knockout: { disqualifyingAnswer: false, reason: null } }] },
    verificationPolicy: { aiUse: 'AllowedWithDisclosure' },
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

{#if part === 'sentence'}
  <AutomationSentence bind:automation={workflow.automations[0]!} index={0} {workflow} errors={[]} onrules={nothing} />
{:else if part === 'recipes'}
  <AutomationRecipes bind:workflow onwrite={nothing} onedit={nothing} />
{:else}
  <AutomationList {workflow} onopen={nothing} />
{/if}
