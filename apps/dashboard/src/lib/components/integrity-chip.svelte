<script lang="ts">
  import StatusMark from '#lib/components/status-mark.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { assessmentNames, pendingLevel, type AssessmentKind, type LevelView } from '#lib/integrity.js';

  // One assessment's level as a chip: shape mark, colour and words, always together (design-system section 2.2).
  // The words are the level's category; there is never a score or a percentage (ADR-0004).
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
    /** How many reason codes raised the level; shown as "Review · 3". */
    reasons?: number;
    /** A reviewer dismissed the assessment; the level stays as it was assessed. */
    dismissed?: boolean;
    /** The table's short words ("Verify") in place of the full level ("Verification recommended"). */
    compact?: boolean;
  } = $props();

  const shown = $derived(level ?? pendingLevel);
  const words = $derived(compact ? shown.short : shown.label);
  const description = $derived(
    `${assessmentNames[kind]}: ${shown.label}` +
      (reasons > 0 ? `, ${reasons} ${reasons === 1 ? 'reason' : 'reasons'}` : '') +
      (dismissed ? ', dismissed after review' : ''),
  );
</script>

<span class="inline-flex items-center gap-1.5" data-integrity-chip={kind} data-level={level ? shown.label : 'pending'}>
  <Badge variant={shown.variant} class="h-[22px] gap-1.5 px-2.5 text-xs">
    <StatusMark shape={shown.shape} tone={shown.tone} class="size-1.5" />
    <span aria-hidden="true">{words}{reasons > 0 ? ` · ${reasons}` : ''}</span>
    <span class="sr-only">{description}</span>
  </Badge>
  {#if dismissed}
    <span aria-hidden="true" class="font-mono text-[10.5px] tracking-[0.06em] text-muted-foreground uppercase">Dismissed</span>
  {/if}
</span>
