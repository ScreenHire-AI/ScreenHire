<script lang="ts">
  import StatusIcon from '#lib/components/status-icon.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { assessmentNames, pendingLevel, type AssessmentKind, type LevelView } from '#lib/integrity.js';
  import { statuses } from '#lib/status.js';

  // One assessment's level as a chip: its status icon, colour and words, always together (design system T1). AI
  // assistance is information: the sparkles icon in its own colour, never a warning. The words are the level's category;
  // there is never a score or a percentage (ADR-0004).
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
    /** How many reason codes raised the level; plain muted text after the chip, as a count in a table is (T4). */
    reasons?: number;
    /** A reviewer dismissed the assessment; the level stays as it was assessed. */
    dismissed?: boolean;
    /**
     * Lists, tables and the board: the short words ("Some signs"), and calm states quiet (T3). Only an authenticity level
     * above the lowest that no reviewer has dismissed gets a filled badge: AI assistance is information, so it stays quiet.
     */
    compact?: boolean;
  } = $props();

  const shown = $derived(level ?? pendingLevel);
  const words = $derived(compact ? shown.short : shown.label);
  const quiet = $derived(compact && (shown.rank <= 0 || dismissed || kind === 'aiAssistance'));
  const description = $derived(
    `${assessmentNames[kind]}: ${shown.label}` +
      (reasons > 0 ? `, ${reasons} ${reasons === 1 ? 'reason' : 'reasons'}` : '') +
      (dismissed ? ', dismissed after review' : ''),
  );
</script>

<span class="inline-flex items-center gap-1.5" data-integrity-chip={kind} data-level={level ? shown.label : 'pending'} data-quiet={quiet || undefined}>
  <Badge variant={quiet ? 'quiet' : statuses[shown.status].tone} class={['h-6 gap-1.5', !quiet && 'pr-2.5 pl-2']}>
    <StatusIcon status={shown.status} />
    <span aria-hidden="true">{words}</span>
    <span class="sr-only">{description}</span>
  </Badge>
  {#if reasons > 0 && !quiet}
    <span aria-hidden="true" data-reasons class="text-caption text-muted-foreground tabular-nums">{reasons}</span>
  {/if}
  {#if dismissed}
    <span aria-hidden="true" class="text-caption text-muted-foreground">Dismissed</span>
  {/if}
</span>
