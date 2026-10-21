<script lang="ts">
  import StatusMark from '#lib/components/status-mark.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { assessmentNames, pendingLevel, type AssessmentKind, type LevelView } from '#lib/integrity.js';

  // One assessment's level as a chip: shape mark, colour and words, always together (design system T1). AI assistance is
  // information and has no mark. The words are the level's category; there is never a score or a percentage (ADR-0004).
  let {
    kind,
    level = null,
    reasons = 0,
    dismissed = false,
    compact = false,
  }: {
    kind: AssessmentKind;
    /** Null while the checks are pending. */
    level?: LevelView | null;
    /** How many reason codes raised the level; a count pill after the chip. */
    reasons?: number;
    /** A reviewer dismissed the assessment; the level stays as it was assessed. */
    dismissed?: boolean;
    /**
     * Lists, tables and the board: the short words ("Verify"), and calm states quiet (T3). Only a level above the lowest
     * that no reviewer has dismissed gets a filled badge.
     */
    compact?: boolean;
  } = $props();

  const shown = $derived(level ?? pendingLevel);
  const words = $derived(compact ? shown.short : shown.label);
  const quiet = $derived(compact && (shown.rank <= 0 || dismissed));
  const description = $derived(
    `${assessmentNames[kind]}: ${shown.label}` +
      (reasons > 0 ? `, ${reasons} ${reasons === 1 ? 'reason' : 'reasons'}` : '') +
      (dismissed ? ', dismissed after review' : ''),
  );
</script>

<span class="inline-flex items-center gap-1.5" data-integrity-chip={kind} data-level={level ? shown.label : 'pending'} data-quiet={quiet || undefined}>
  <Badge variant={quiet ? 'quiet' : shown.variant} class={['h-[22px] gap-1.5', !quiet && 'px-2.5']}>
    {#if kind !== 'aiAssistance' || !level}
      <StatusMark shape={shown.shape} tone={shown.tone} class="size-1.5" />
    {/if}
    <span aria-hidden="true">{words}</span>
    <span class="sr-only">{description}</span>
  </Badge>
  {#if reasons > 0 && !quiet}
    <Badge variant="count" aria-hidden="true">{reasons}</Badge>
  {/if}
  {#if dismissed}
    <span aria-hidden="true" class="text-caption text-muted-foreground">Dismissed</span>
  {/if}
</span>
