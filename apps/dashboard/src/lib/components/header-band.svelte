<script lang="ts">
  import type { Snippet } from 'svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';

  // The page's header band (design system section 7, S2): title, a list's count or the status beside it, details under
  // it, actions on the right, then a facts row or tabs on its bottom edge. It holds the page's one h1.
  let {
    title,
    count,
    countLabel,
    status,
    details,
    actions,
    facts,
    tabs,
    wide = false,
  }: {
    title: string;
    /** A list page's count, as a count pill after the title (T4). */
    count?: number;
    /** What the count counts ("applications"), for screen readers. */
    countLabel?: string;
    /** A status badge, shown beside the title. */
    status?: Snippet;
    /** One line under the title, such as a person's contact details. */
    details?: Snippet;
    /** The page actions, at most one of them primary (A1). */
    actions?: Snippet;
    /** `Fact` items: a labelled facts row. */
    facts?: Snippet;
    /** Tabs on the band's bottom edge. */
    tabs?: Snippet;
    /** A list page's wider content at 2xl (S6). */
    wide?: boolean;
  } = $props();
</script>

<div data-slot="header-band" class="@container border-b border-border bg-card">
  <div class={['mx-auto flex w-full max-w-page flex-col gap-4 px-4 pt-5 @3xl:px-6', wide && '2xl:max-w-page-wide', !tabs && 'pb-5']}>
    <div class="flex flex-wrap items-center gap-x-4 gap-y-3">
      <div class="flex min-w-0 flex-[1_1_16rem] flex-col gap-1.5">
        <div class="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1.5">
          <h1 class="min-w-0 font-display text-page-title break-words">{title}</h1>
          {#if count !== undefined}
            <Badge variant="count">{count}{#if countLabel}<span class="sr-only">&nbsp;{countLabel}</span>{/if}</Badge>
          {/if}
          {@render status?.()}
        </div>
        {#if details}
          <div class="flex min-w-0 flex-wrap items-center gap-x-4 gap-y-1 text-muted-foreground">{@render details()}</div>
        {/if}
      </div>
      {#if actions}
        <div class="flex flex-wrap items-center gap-2">{@render actions()}</div>
      {/if}
    </div>
    {#if facts}
      <!-- The clip hides each line's first rule; its 4px margin keeps focus rings inside it. -->
      <div class="-m-1 overflow-hidden p-1">
        <dl class="grid grid-cols-2 gap-x-8 gap-y-3 @2xl:flex @2xl:flex-wrap">{@render facts()}</dl>
      </div>
    {/if}
    {@render tabs?.()}
  </div>
</div>
