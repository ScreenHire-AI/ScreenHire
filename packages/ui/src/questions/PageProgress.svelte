<script lang="ts">
  // Where the candidate is in a form of several pages (apply-flow.md §7): one bar with "40% done" at its end, over a
  // hairline that separates it from the questions. The review says "Ready to send" with the bar full. The fill is an <svg>
  // because its width is an attribute, which the careers pages' content security policy allows where it refuses an inline
  // style, and because an outer <svg> has a box of its own, so it can carry the view-transition-name that grows it from the
  // last step's width (careers global.css). A <progress> draws its fill as a pseudo-element, which cannot. The bar is hidden
  // from screen readers, which read the same words as everyone else. Server-rendered on the careers pages (no JavaScript)
  // and live in the dashboard's questionnaire preview, so the preview is the form.
  import { progressLabel, progressPercent } from './model';

  interface Props {
    /** The current step, from 1. */
    step: number;
    steps: number;
    /** The step checks the answers before they are sent, so nothing is left to fill in. */
    review?: boolean;
  }

  let { step, steps, review = false }: Props = $props();

  const width = $derived(`${review ? 100 : progressPercent(step, steps)}%`);
</script>

<div class="page-progress">
  <div class="page-progress-track" aria-hidden="true">
    <svg class="page-progress-fill" {width}></svg>
    <!-- Drawn again for each step, so the sheen crosses once; the fill stays and grows. -->
    {#key step}<svg class="page-progress-sheen" {width}></svg>{/key}
  </div>
  <p class="text-candidate-hint font-semibold whitespace-nowrap text-candidate-foreground tabular-nums" data-page-progress>{progressLabel(step, steps, review)}</p>
</div>
