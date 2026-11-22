<script lang="ts">
  import FormCards from '#lib/components/workflow/form-cards.svelte';
  import type { TimelineForm } from '#lib/forms-timeline.js';

  // A hiring process's forms: the application form, two library forms sent at stages, an equality monitoring form sent
  // when they apply and one sent only by hand. New form opens the library's sheet; here it only says it was pressed.
  const forms: TimelineForm[] = [
    { id: 'apply', name: 'Application form', when: 'When they apply', questions: 6, library: false, equality: false },
    { id: 'equality', name: 'Equality monitoring', when: 'Sent when they apply', questions: 4, library: true, equality: true },
    { id: 'screening', name: 'Screening questionnaire', when: 'Sent when they enter Screening', questions: 5, library: true, equality: false },
    { id: 'references', name: 'References', when: 'Sent 3 days after they enter Offer', questions: 2, library: true, equality: false },
    { id: 'availability', name: 'Availability', when: 'Only when sent by hand', questions: 3, library: true, equality: false },
  ];
  let selected = $state('apply');
  let pressed = $state(false);
</script>

<div class="flex flex-col gap-2">
  <FormCards {forms} bind:selected onadd={() => (pressed = true)} />
  {#if pressed}<p role="status" class="text-muted-foreground">New form opens the form library.</p>{/if}
</div>
