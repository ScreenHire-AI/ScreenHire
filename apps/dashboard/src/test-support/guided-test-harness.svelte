<script lang="ts">
  import type { HiringWorkflow, WorkflowSimulationResult } from '@screenhire/api-client';
  import { createListEmailTemplates, createSimulateWorkflow } from '@screenhire/api-client/svelte-query';
  import { untrack } from 'svelte';
  import Panel from '#lib/components/panel.svelte';
  import GuidedTest from '#lib/components/workflow/guided-test.svelte';
  import { emailTemplateNames } from '#lib/workflow.js';

  // The guided test as the Test tab's "Try your own candidate" mounts it, with the simulate endpoint wired and the page's
  // links as callbacks a test can watch: with `onshow` a result offers to light its path, with `onadd` an empty workflow
  // offers to add an automation, with `onpipeline` a workflow whose automations are all off offers to open Pipeline. A
  // read-only version passes none of them. The harness owns a copy of the workflow, as a page owns its draft, and the
  // Edit button changes it (renames the first stage), for the test of an answer that is older than the workflow. Render
  // inside with-query-client.svelte.
  let {
    workflow,
    onshow,
    onadd,
    onpipeline,
  }: { workflow: HiringWorkflow; onshow?: (result: WorkflowSimulationResult) => void; onadd?: () => void; onpipeline?: () => void } = $props();

  let edited = $state(untrack(() => workflow));
  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));
  const simulate = createSimulateWorkflow();
</script>

<Panel title="Test this workflow">
  <button type="button" onclick={() => (edited.stages[0]!.name = 'Renamed')}>Edit the workflow</button>
  <GuidedTest workflow={edited} {templateNames} {onshow} {onadd} {onpipeline} run={async (data) => (await simulate.mutateAsync({ data })).data} />
</Panel>
