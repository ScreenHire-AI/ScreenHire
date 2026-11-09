<script lang="ts">
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import InfoIcon from '~icons/lucide/info';
  import SparklesIcon from '~icons/lucide/sparkles';
  import type { VerificationSignalView } from '@screenhire/api-client';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SignalEvidence, { isWideEvidence } from '#lib/components/signal-evidence.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { sentenceStart } from '#lib/history.js';
  import { explainReasonLabels, reviewerName } from '#lib/verification.js';

  // One finding on the Verification tab (design system section 16): its title, opening (A9) to what was observed in plain
  // words, its evidence as a facts row, any quote or comparison, the counter-evidence as the other side of the argument, and
  // the code, which is there for support and audit (W6). Only the exception is marked: a finding that never counts toward the
  // level says it is information only, with the sparkles mark for AI help and the grey info mark for context. The one review action is Mark explained; an explanation shows under the row so it reads while the finding is
  // closed, with Undo for its author (it cannot sit inside the summary, which is itself the control, A9).
  let {
    signal,
    ai = false,
    open,
    onopenchange,
    canExplain = false,
    canUndo = false,
    undoing = false,
    onexplain,
    onundo,
    currentUserId,
  }: {
    signal: VerificationSignalView;
    /** The finding belongs to AI help: information only, so it has no action. */
    ai?: boolean;
    open: boolean;
    onopenchange: (open: boolean) => void;
    canExplain?: boolean;
    /** The signed-in reviewer explained it and may still undo it. */
    canUndo?: boolean;
    undoing?: boolean;
    onexplain: () => void;
    onundo: () => void;
    currentUserId: string | undefined;
  } = $props();

  const short = $derived(signal.evidence.filter((evidence) => !isWideEvidence(evidence)));
  const wide = $derived(signal.evidence.filter(isWideEvidence));
  // AI help never counts; nor does context that had nothing independent beside it (a VPN alone).
  const informationOnly = $derived(ai || !signal.countsTowardLevel);
</script>

<!-- Open, the finding's title, its detail and any explanation share one soft background, with nothing boxed inside it. -->
<li data-signal={signal.code} data-explained={signal.explained ? '' : undefined} class="has-[details[open]]:bg-secondary/60">
  <details bind:open={() => open, onopenchange} class="group">
    <summary class="flex min-h-11 list-none flex-wrap items-center gap-x-2 gap-y-1 px-3 py-2.5 hover:bg-secondary/60 [&::-webkit-details-marker]:hidden">
      <span class="min-w-0 flex-1 font-semibold">{signal.title}</span>
      {#if informationOnly}
        <!-- The exception is marked, not the rule: AI help and context never change the level. The sparkles mean AI help only. -->
        <span data-information-only={ai ? 'ai' : 'context'} class={['inline-flex items-center gap-1 text-caption', ai ? 'text-status-ai-mark' : 'text-muted-foreground']}>
          {#if ai}<SparklesIcon aria-hidden="true" class="size-3.5" />{:else}<InfoIcon aria-hidden="true" class="size-3.5" />{/if}Information only
        </span>
      {/if}
      <!-- A state of the finding, so a badge on its row (W6); calm, so quiet (T3). -->
      {#if signal.explained}<StatusBadge status="done" label="Explained" quiet />{/if}
      <ChevronDownIcon aria-hidden="true" class="size-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-180" />
    </summary>
    <div class="flex flex-col gap-4 px-3 pt-1 pb-5" data-evidence>
      <!-- The reading size the design system gives verification explanations (section 4). -->
      <p class="max-w-measure text-reading">{signal.explanation}</p>
      {#if short.length > 0}
        <!-- The clip hides each line's first rule; its 4px margin keeps focus rings inside it. -->
        <div class="-m-1 overflow-hidden p-1">
          <dl class="flex flex-wrap gap-x-8 gap-y-3">
            {#each short as evidence, index (index)}<SignalEvidence {evidence} fact />{/each}
          </dl>
        </div>
      {/if}
      {#each wide as evidence, index (index)}<SignalEvidence {evidence} />{/each}
      {#each signal.counterEvidence as counter, index (index)}
        <div class="flex max-w-3xl flex-col gap-1 rounded-md border border-border-subtle bg-card px-3 py-2" data-counter-evidence>
          <p class="text-caption font-semibold text-muted-foreground">Counter-evidence</p>
          <p class="max-w-measure">{counter.explanation}</p>
          {#if counter.evidence}<SignalEvidence evidence={counter.evidence} />{/if}
        </div>
      {/each}
      <!-- The finding's own action shares the row with its code, on the left (F6). -->
      <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        {#if canExplain}<Button variant="outline" onclick={onexplain}>Mark explained</Button>{/if}
        <p class="ml-auto text-caption text-muted-foreground">Code <span class="font-mono text-code">{signal.code}</span></p>
      </div>
    </div>
  </details>
  {#if signal.explained}
    {@const explanation = signal.explained}
    <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 px-3 pb-2.5" data-explanation>
      <p class="min-w-0 max-w-measure text-muted-foreground">
        <!-- The row's Explained badge says what this is, so the line starts with who (W7). -->
        {sentenceStart(reviewerName(explanation.actorUserId, explanation.actorName, currentUserId))}, <RelativeTime at={explanation.explainedAt} class="inline" />:
        {explainReasonLabels[explanation.reason]}{explanation.note ? `. "${explanation.note}"` : '.'}
      </p>
      {#if canUndo}
        <Button variant="outline" disabled={undoing} onclick={onundo}>Undo</Button>
      {/if}
    </div>
  {/if}
</li>
