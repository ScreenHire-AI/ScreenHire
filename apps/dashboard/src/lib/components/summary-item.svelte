<script lang="ts">
  import type { Snippet } from 'svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';

  // One label and value in a `SummaryList`. A free-text answer can be long, so the value keeps the 72ch measure (S6).
  // `hideLabel` is for a value that explains itself: the label stays for screen readers (W9). A value that is a list says
  // how many it holds, in the count badge after the label (T4).
  // `question` is a candidate's answer (section 21): the label is the question as they saw it, a sentence rather than a
  // word, so its column is wide enough for most questions on one line, and the answer is in medium weight so the two
  // never read as one. In a narrow panel the answer sits under its question.
  let {
    label,
    count,
    hideLabel = false,
    question = false,
    children,
  }: { label: string; count?: number; hideLabel?: boolean; question?: boolean; children: Snippet } = $props();
</script>

<div
  class={[
    'grid gap-1 border-b border-border-subtle py-3 first:pt-0 last:border-0 last:pb-0',
    !hideLabel && !question && '@md:grid-cols-[minmax(8rem,12rem)_1fr] @md:gap-4',
    question && '@3xl:grid-cols-[minmax(16rem,30rem)_minmax(0,1fr)] @3xl:gap-8',
  ]}
>
  <dt class={hideLabel ? 'sr-only' : 'flex items-center gap-2 text-muted-foreground'}>{label}{#if count !== undefined}<Badge variant="count">{count}</Badge>{/if}</dt>
  <dd class={['max-w-measure min-w-0 break-words', question && 'font-medium']}>{@render children()}</dd>
</div>
