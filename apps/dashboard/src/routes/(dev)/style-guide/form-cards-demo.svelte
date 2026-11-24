<script lang="ts">
  import FormCards from '#lib/components/workflow/form-cards.svelte';
  import type { TimelineForm } from '#lib/forms-timeline.js';

  // A hiring process's forms: the application form, two library forms sent at stages, an equality monitoring form sent
  // when they apply and one sent only by hand. New form opens the library's sheet and a card's menu replaces or removes its
  // form; here each only says it was pressed.
  const forms: TimelineForm[] = [
    { id: 'apply', name: 'Application form', when: 'When they apply', whenShort: 'When they apply', moment: 'apply', questions: 6, library: false, equality: false },
    { id: 'equality', name: 'Equality monitoring', when: 'Sent when they apply', whenShort: 'When they apply', moment: 'apply', questions: 4, library: true, equality: true },
    { id: 'screening', name: 'Screening questionnaire', when: 'Sent when they enter Screening', whenShort: 'Entering Screening', moment: 'stage', questions: 5, library: true, equality: false },
    { id: 'references', name: 'References', when: 'Sent 3 days after they enter Offer', whenShort: '3 days after Offer', moment: 'later', questions: 2, library: true, equality: false },
    { id: 'availability', name: 'Availability', when: 'Only when sent by hand', whenShort: 'By hand only', moment: 'hand', questions: 3, library: true, equality: false },
  ];
  let selected = $state('apply');
  let pressed = $state('');
</script>

<div class="flex flex-col gap-2">
  <FormCards
    {forms}
    bind:selected
    onadd={() => (pressed = 'New form opens the form library.')}
    onreplace={() => (pressed = 'Replace with another form opens the form library.')}
    onremove={() => (pressed = 'Remove form takes the form and the automation that sends it.')}
  />
  {#if pressed}<p role="status" class="text-muted-foreground">{pressed}</p>{/if}
</div>
