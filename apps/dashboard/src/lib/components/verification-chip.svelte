<script lang="ts">
  import Hint from '#lib/components/hint.svelte';
  import LevelMark from '#lib/components/level-mark.svelte';
  import StatusIcon from '#lib/components/status-icon.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { statuses } from '#lib/status.js';
  import type { VerificationState } from '#lib/verification.js';

  // A verification state as one word (design system section 3.2): Clear, Review, Verify, and the muted Pending and
  // Dismissed. Review and Verify are a filled badge with the level mark and the word; Clear, Pending and Dismissed are
  // quiet, an icon and a muted word, so a calm row says little (T3). The long names ("Review suggested") are
  // screen-reader text. There is never a score or a percentage (ADR-0004).
  let {
    state,
    reasons = 0,
    size = 'sm',
    tooltip,
  }: {
    state: VerificationState;
    /**
     * How many reason codes raised the level. Screen readers hear it in the chip's description; it is not shown, because a
     * bare number after the chip says nothing on its own, and the reasons themselves live on the Verification tab (W6).
     */
    reasons?: number;
    /** The candidate's header is larger than a table cell. */
    size?: 'sm' | 'lg';
    /**
     * What the tooltip says. The header passes the level's name and nothing more; Dismissed says by whom and why when the
     * page knows (the list row only knows it was dismissed). Without one, no tooltip and no tab stop, so a table's rows
     * keep their own.
     */
    tooltip?: string;
  } = $props();

  const marked = $derived(state.kind === 'review' || state.kind === 'verify');
  const description = $derived(`Verification: ${state.label}` + (reasons > 0 ? `, ${reasons} ${reasons === 1 ? 'reason' : 'reasons'}` : ''));
  const shownTooltip = $derived(tooltip ?? (state.kind === 'dismissed' ? state.label : undefined));
  const tone = $derived(statuses[state.status].tone);
  const height = $derived(size === 'lg' ? 'h-7' : 'h-6');
</script>

{#snippet chip(props: Record<string, unknown>)}
  <span {...props} class="inline-flex items-center gap-1.5" data-verification-chip data-level={state.kind} data-quiet={marked ? undefined : true}>
    {#if marked && (state.kind === 'review' || state.kind === 'verify')}
      <Badge variant={tone} class={[height, 'gap-1.5 pr-2.5 pl-2', size === 'lg' && 'text-body']}>
        <LevelMark steps={state.steps || 2} tone={state.kind} size={size} />
        <span aria-hidden="true">{state.word}</span>
        <span class="sr-only">{description}</span>
      </Badge>
    {:else}
      <Badge variant="quiet" class={[height, 'gap-1.5', size === 'lg' && 'text-body']}>
        <StatusIcon status={state.status} />
        <span aria-hidden="true">{state.word}</span>
        <span class="sr-only">{description}</span>
      </Badge>
    {/if}
  </span>
{/snippet}

{#if shownTooltip}
  <Hint content={shownTooltip}>
    {#snippet children({ props })}{@render chip(props)}{/snippet}
  </Hint>
{:else}
  {@render chip({})}
{/if}
