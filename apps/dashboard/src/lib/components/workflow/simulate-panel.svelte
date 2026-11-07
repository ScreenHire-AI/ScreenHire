<script lang="ts">
  import type { HiringWorkflow, WorkflowSimulationResult } from '@screenhire/api-client';
  import { createListEmailTemplates, createSimulateWorkflow } from '@screenhire/api-client/svelte-query';
  import Panel from '#lib/components/panel.svelte';
  import { emailTemplateNames } from '#lib/workflow.js';
  import GuidedTest from './guided-test.svelte';

  // The Simulate tab (workflow-engine.md section 4): the guided test over the workflow being edited, unsaved changes
  // included, with the evaluator the worker runs. Nothing is saved or sent. With `onshow` a result offers to light the
  // path it took on the graph; with `onadd` an empty workflow offers to add an automation, and with `onpipeline` one
  // whose automations are all off offers to open Pipeline.
  let {
    workflow,
    onshow,
    onadd,
    onpipeline,
  }: { workflow: HiringWorkflow; onshow?: (result: WorkflowSimulationResult) => void; onadd?: () => void; onpipeline?: () => void } = $props();

  // The organization's template names, for "Send the ... email".
  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));

  const simulate = createSimulateWorkflow();
</script>

<Panel title="Test this workflow">
  <GuidedTest {workflow} {templateNames} {onshow} {onadd} {onpipeline} run={async (data) => (await simulate.mutateAsync({ data })).data} />
</Panel>
