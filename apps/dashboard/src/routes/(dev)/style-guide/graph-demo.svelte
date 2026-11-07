<script lang="ts">
  import type { HiringWorkflow } from '@screenhire/api-client';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import WorkflowGraph from '#lib/components/workflow/workflow-graph.svelte';
  import type { GraphActions } from '#lib/components/workflow/step-node.svelte';

  // The workflow graph on its own sample workflow, with the actions of a builder that change nothing: the nodes press, the
  // plus buttons are drawn, and hovering or focusing a node lights its path. The graph reads the organization's email
  // template names through a query, so it brings its own cache, as the signed-in shell does for the real one.
  setQueryClientContext(new QueryClient({ defaultOptions: { queries: { retry: false } } }));

  const workflow: HiringWorkflow = {
    stages: [
      { id: 'applied', name: 'Applied', kind: 'Applied' },
      { id: 'screening', name: 'Screening', kind: 'Screening' },
      { id: 'interview', name: 'Interview', kind: 'Interview' },
      { id: 'hired', name: 'Hired', kind: 'Hired' },
    ],
    questionnaire: { questions: [] },
    verificationPolicy: { aiUse: 'Allowed' },
    automations: [
      { id: 'tell', name: 'Tell the team', trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'NotifyTeam', note: 'New application' }] },
      {
        id: 'fast',
        name: 'Fast-track referrals',
        trigger: { type: 'StageEntered', stageId: 'screening' },
        conditions: { match: 'All', rules: [{ fact: 'application.tags', operator: 'Contains', value: 'referral' }] },
        actions: [
          { type: 'AddTag', tag: 'fast-track' },
          { type: 'MoveToStage', stageId: 'interview' },
        ],
      },
    ],
  };

  const nothing = () => {};
  const actions: GraphActions = { edit: nothing, openLane: nothing, add: nothing, addCondition: nothing, addAction: nothing, connect: nothing };
</script>

<WorkflowGraph {workflow} errors={[]} {actions} />
