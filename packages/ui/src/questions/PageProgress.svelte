<script lang="ts">
  // Where the candidate is in a form of several pages (apply-flow.md §7): one bar and "40% done", from progressPercent.
  // The bar is a native <progress>, so its fill needs no inline style under the careers pages' content security policy.
  // It is hidden from screen readers, which read the same words as everyone else. Server-rendered on the careers pages
  // (no JavaScript) and live in the dashboard's questionnaire preview, so the preview is the form.
  import { progressPercent } from './model';

  interface Props {
    /** The current step, from 1. */
    step: number;
    steps: number;
  }

  let { step, steps }: Props = $props();

  const percent = $derived(progressPercent(step, steps));
</script>

<div class="flex flex-col gap-2">
  <progress class="page-progress" max="100" value={percent} aria-hidden="true"></progress>
  <p class="text-candidate-hint font-semibold text-candidate-foreground" data-page-progress>{percent}% done</p>
</div>
