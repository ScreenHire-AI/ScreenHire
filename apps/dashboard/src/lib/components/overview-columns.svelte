<script lang="ts">
  import type { Snippet } from 'svelte';

  // An overview's two columns (design system section 7, Overview): a main column of about two thirds and a side column of
  // one third, each stacking its own panels, so panels of different heights never have to match across a row. One column
  // below the threshold, main then side. The threshold is measured on the container the overview sits in, and both come to
  // about a 1415px window with the full sidebar and the scrollbar's gutter, so a 1440px window has two columns: 69rem of
  // content on a page (Reports, the Verification overview), 55rem in the settings frame's column (Billing). A row of
  // headline numbers stays above this, in its own equal-width row.
  let {
    main,
    side,
    within = 'page',
  }: {
    main: Snippet;
    side: Snippet;
    /** What holds the overview: the page's own `@container`, or the settings frame's column. */
    within?: 'page' | 'settings';
  } = $props();
</script>

<div
  data-overview-columns
  class={[
    'grid items-start gap-5',
    within === 'settings' ? '@min-[55rem]/column:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]' : '@min-[69rem]:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]',
  ]}
>
  <div data-overview-main class="flex min-w-0 flex-col gap-5">{@render main()}</div>
  <div data-overview-side class="flex min-w-0 flex-col gap-5">{@render side()}</div>
</div>
