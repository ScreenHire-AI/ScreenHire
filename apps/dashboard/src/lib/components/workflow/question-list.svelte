<script lang="ts">
  import type { Question } from '@screenhire/api-client';
  import StepMarker from '#lib/components/step-marker.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { choicesSummary } from '#lib/form-builder.js';
  import { questionTypeLabels } from '#lib/workflow.js';
  import { rowClass } from './controls.js';

  // A form's questions read as content, with no controls (a version that cannot be edited, a library form's copy): each
  // its number, its label and help, its answer kind, Required and a knockout as quiet badges, and its options on one
  // line, as the form builder's rows show them, so a long list of options never grows the row (S9).
  let { questions }: { questions: readonly Question[] } = $props();
</script>

<ol class="flex flex-col">
  {#each questions as question, index (question.id)}
    <li class={['flex gap-3', rowClass]}>
      <StepMarker>{index + 1}</StepMarker>
      <div class="flex min-w-0 flex-col gap-1.5 pt-1">
        <span class="font-semibold break-words">{question.label}</span>
        {#if question.helpText}<span class="text-muted-foreground">{question.helpText}</span>{/if}
        <span class="flex flex-wrap gap-1.5">
          <Badge variant="muted">{questionTypeLabels[question.type]}</Badge>
          {#if question.required}<Badge variant="muted">Required</Badge>{/if}
          {#if question.knockout}<Badge variant="muted">Knockout if {question.knockout.disqualifyingAnswer ? 'yes' : 'no'}</Badge>{/if}
        </span>
        {#if question.options?.length}<span class="truncate text-muted-foreground">{choicesSummary(question.options)}</span>{/if}
        {#if question.knockout?.reason}<span class="text-muted-foreground">{question.knockout.reason}</span>{/if}
      </div>
    </li>
  {/each}
</ol>
