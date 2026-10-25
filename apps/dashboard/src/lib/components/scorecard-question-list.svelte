<script lang="ts">
  import type { ScorecardQuestion } from '@screenhire/api-client';
  import { isRatingKind } from '#lib/scorecards.js';
  import { scorecardQuestionTypeLabels } from '#lib/workflow.js';

  // A scorecard's questions read as content (design system section 7, read-only view): each question, an asterisk when
  // required, then its answer kind and what to look for. Every scorecard then asks for a recommendation and a note.
  let { questions }: { questions: readonly ScorecardQuestion[] } = $props();
</script>

<ol class="flex flex-col gap-2" aria-label="Questions">
  {#each questions as question (question.id)}
    <li class="flex flex-col gap-0.5">
      <span class="font-semibold break-words">{question.label}{#if question.required}<span class="text-muted-foreground"> *</span>{/if}</span>
      <span class="text-caption text-muted-foreground">
        {isRatingKind(question.type) ? 'Rating, ' : ''}{scorecardQuestionTypeLabels[question.type]}{question.guidance ? `. ${question.guidance}` : ''}
      </span>
    </li>
  {/each}
</ol>
<p class="text-muted-foreground">Then an overall recommendation and a note.</p>
