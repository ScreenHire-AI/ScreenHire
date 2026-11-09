<script lang="ts">
  import type { WorkflowStage } from '@screenhire/api-client';
  import FormTimeline from '#lib/components/workflow/form-timeline.svelte';
  import type { FormLater, TimelineForm } from '#lib/forms-timeline.js';

  // The strip of forms on the candidate's timeline, with one form not sent yet. Ask more later opens its small form;
  // adding changes nothing here beyond saying what was asked.
  const forms: TimelineForm[] = [
    { id: 'apply', when: 'When they apply', name: 'Apply form', sent: true },
    { id: 'trial', when: 'When they reach Trial shift', name: 'Trial shift questions', sent: true },
    { id: 'references', when: '3 days after Offer starts', name: 'References', sent: true },
    { id: 'draft', when: 'Not sent yet', name: 'Availability', sent: false },
  ];
  const stages: WorkflowStage[] = [
    { id: 'phone', name: 'Phone call', kind: 'Screening' },
    { id: 'trial', name: 'Trial shift', kind: 'Interview' },
    { id: 'offer', name: 'Offer', kind: 'Offer' },
  ];
  let selected = $state('apply');
  let asked = $state('');
  const describe = (later: FormLater) => `${stages.find((stage) => stage.id === later.stageId)?.name}${later.days ? `, ${later.days} days after it starts` : ', when they reach it'}`;
</script>

<div class="flex flex-col gap-2">
  <FormTimeline {forms} bind:selected {stages} onadd={(later) => (asked = describe(later))} />
  {#if asked}<p role="status" class="text-muted-foreground">Asked for: {asked}</p>{/if}
</div>
