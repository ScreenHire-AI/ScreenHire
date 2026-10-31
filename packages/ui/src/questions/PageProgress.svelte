<script lang="ts">
  // Where the candidate is in a form of several pages (apply-flow.md §7): a bar of one segment per step, then "Step 2 of
  // 4" and "9 of 24 answered". Server-rendered on the careers pages (no JavaScript) and live in the dashboard's
  // questionnaire preview, so the preview is the form.
  interface Props {
    /** The current step, from 1. */
    step: number;
    steps: number;
    answered: number;
    /** The form's questions; with none, only the step shows. */
    total: number;
  }

  let { step, steps, answered, total }: Props = $props();
</script>

<div class="flex flex-col gap-1.5">
  <div aria-hidden="true" class="flex gap-1">
    {#each Array.from({ length: steps }, (_, i) => i) as i (i)}
      <span class={['h-1.5 flex-1 rounded-full', i < step ? 'bg-candidate-cta' : 'bg-candidate-chip']}></span>
    {/each}
  </div>
  <p class="flex flex-wrap justify-between gap-x-4 gap-y-0.5 text-candidate-hint text-candidate-muted-foreground" data-page-progress>
    <span class="font-semibold text-candidate-foreground">Step {step} of {steps}</span>
    {#if total > 0}<span>{answered} of {total} answered</span>{/if}
  </p>
</div>
