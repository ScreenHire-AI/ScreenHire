<script lang="ts">
  import type { Snippet } from 'svelte';

  // An overview's two columns (design system section 7, Overview): a main column of about two thirds and a side column of
  // one third, each stacking its own panels, so panels of different heights never have to match across a row. One column
  // below the threshold, main then side. The overview measures its own width (`@container/overview`), so the threshold
  // means the same wherever it sits, whatever padding its parent has. Both thresholds come to about a 1415px window with
  // the full sidebar and the scrollbar's gutter, so a 1440px window has two columns: 69rem on a page (Reports, the
  // Verification overview), 55rem in the settings frame's 960px column (Billing), which a change to
  // `--container-settings-frame` moves too. A row of headline numbers stays above this, in its own equal-width row.
  let {
    main,
    side,
    within = 'page',
  }: {
    main: Snippet;
    side: Snippet;
    /** Where the overview sits: a page's content, or the settings frame's column, which is narrower. */
    within?: 'page' | 'settings';
  } = $props();
</script>

<div data-overview-columns class="@container/overview">
  <div
    class={[
      'grid grid-cols-1 items-start gap-5',
      within === 'settings'
        ? '@min-[55rem]/overview:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]'
        : '@min-[69rem]/overview:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]',
    ]}
  >
    <div data-overview-main class="flex min-w-0 flex-col gap-5">{@render main()}</div>
    <div data-overview-side class="flex min-w-0 flex-col gap-5">{@render side()}</div>
  </div>
</div>
