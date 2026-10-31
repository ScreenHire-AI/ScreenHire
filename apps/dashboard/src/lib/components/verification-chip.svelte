<script lang="ts">
  import StatusIcon from '#lib/components/status-icon.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { assessmentNames, pendingLevel, type AssessmentKind, type LevelView } from '#lib/verification.js';
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
    hideCalm = false,
  }: {
    kind: AssessmentKind;
    /** Null while the checks are pending. */
    level?: LevelView | null;
    /**
     * How many reason codes raised the level. Screen readers hear it in the chip's description; it is not shown, because a
     * bare number after the chip says nothing on its own, and the reasons themselves live on the Verification tab (W6).
     */
    reasons?: number;
    /** A reviewer dismissed the assessment; the level stays as it was assessed. */
    dismissed?: boolean;
    /**
     * Lists, tables and the board: the short words ("Some signs"), and calm states quiet (T3). Only an authenticity level
     * above the lowest that no reviewer has dismissed gets a filled badge: AI assistance is information, so it stays quiet.
     */
    compact?: boolean;
    /**
     * Calm says nothing (design system section 21): the lowest level of an assessment that no one needs to look at shows
     * no words, only its description for screen readers. A pending check is not calm, so it still shows.
     */
    hideCalm?: boolean;
  } = $props();

  const shown = $derived(level ?? pendingLevel);
  const words = $derived(compact ? shown.short : shown.label);
  const hidden = $derived(hideCalm && level !== null && shown.rank === 0);
  const quiet = $derived(compact && (shown.rank <= 0 || dismissed || kind === 'aiAssistance'));
  const description = $derived(
    `${assessmentNames[kind]}: ${shown.label}` +
      (reasons > 0 ? `, ${reasons} ${reasons === 1 ? 'reason' : 'reasons'}` : '') +
      (dismissed ? ', dismissed after review' : ''),
  );
</script>

{#if hidden}
  <span class="sr-only" data-verification-chip={kind} data-level={shown.label} data-calm>{description}</span>
{:else}
  <span class="inline-flex items-center gap-1.5" data-verification-chip={kind} data-level={level ? shown.label : 'pending'} data-quiet={quiet || undefined}>
    <Badge variant={quiet ? 'quiet' : statuses[shown.status].tone} class={['h-6 gap-1.5', !quiet && 'pr-2.5 pl-2']}>
      <StatusIcon status={shown.status} />
      <span aria-hidden="true">{words}</span>
      <span class="sr-only">{description}</span>
    </Badge>
    {#if dismissed}
      <span aria-hidden="true" class="text-caption text-muted-foreground">Dismissed</span>
    {/if}
  </span>
{/if}
