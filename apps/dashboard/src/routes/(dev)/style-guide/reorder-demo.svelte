<script lang="ts">
  import ReorderList from '#lib/components/reorder-list.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';

  // Questions people reorder (A7): drag a grip, click it for the Move menu, or press Space and the arrow keys. Nothing is
  // saved.
  let questions = $state([
    { id: 'resume', label: 'Your CV or resume', kind: 'File upload' },
    { id: 'right-to-work', label: 'Do you have the right to work in the country this job is in?', kind: 'Yes or no' },
    { id: 'years', label: 'How many years have you worked as a software engineer?', kind: 'Choice' },
    { id: 'notice', label: 'What is your notice period?', kind: 'Short text' },
  ]);
</script>

<!-- Rules between the rows only, and the last row's bottom corners rounded like the panel's, so its background does not
     square them off. Rounded rather than clipped, so a picked-up row's focus ring shows whole. -->
<ReorderList
  items={questions}
  key={(question) => question.id}
  label="Screening questions"
  itemLabel={(question) => question.label}
  onreorder={(next) => (questions = next)}
  class="divide-y divide-border-subtle [&>li:last-child>div]:rounded-b-panel"
>
  {#snippet item(question, grip)}
    <div class="flex min-w-0 items-center gap-2 bg-card py-2 pr-4 pl-2.5">
      {@render grip()}
      <span class="flex min-w-0 flex-1"><TruncatedText text={question.label} class="font-semibold" /></span>
      <Badge variant="muted" class="shrink-0">{question.kind}</Badge>
    </div>
  {/snippet}
</ReorderList>
