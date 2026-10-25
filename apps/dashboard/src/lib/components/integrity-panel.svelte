<script lang="ts">
  import type { AiAssistanceAssessmentView, AuthenticityAssessmentView, IntegritySignalView } from '@screenhire/api-client';
  import { createGetApplicationIntegrity } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { page } from '$app/state';
  import IntegrityChip from '#lib/components/integrity-chip.svelte';
  import IntegrityDismissDialog from '#lib/components/integrity-dismiss-dialog.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SignalEvidence from '#lib/components/signal-evidence.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import {
    aiAssistanceLevels,
    aiPolicyLabels,
    assessmentNames,
    authenticityLevels,
    dismissReasonLabels,
    refetchAfterReview,
    reviewerName,
    type AssessmentKind,
    type LevelView,
  } from '#lib/integrity.js';
  import { describeError } from '#lib/query.js';

  // The integrity evidence panel (ADR-0004, docs/architecture/integrity-engine.md section 7): what was observed, why it
  // matters, what argues against it and a suggested next step. It decides nothing. A dismissal is a person's and goes in
  // the review log (`ReviewLog`), and the panel has no reject control.
  let { applicationId }: { applicationId: string } = $props();

  const queryClient = useQueryClient();
  // IntegrityAssessed refetches it live (#lib/realtime.ts).
  const integrity = createGetApplicationIntegrity(() => applicationId);
  const detail = $derived(integrity.data?.data);

  let dismissOpen = $state(false);
  let dismissing = $state<{ id: string; kind: AssessmentKind } | null>(null);
  let announcement = $state('');

  function startDismiss(kind: AssessmentKind, id: string) {
    dismissing = { id, kind };
    dismissOpen = true;
  }

  async function done(message: string) {
    announcement = message;
    await refetchAfterReview(queryClient);
  }
</script>

{#snippet signalItem(signal: IntegritySignalView)}
  <li class="flex flex-col gap-1 px-3 py-2.5" data-signal={signal.code}>
    <span class="font-mono text-code text-muted-foreground">{signal.code}</span>
    <span class="font-semibold">{signal.title}</span>
    <p class="text-secondary-foreground">{signal.explanation}</p>
    {#if signal.evidence.length > 0}
      <div class="mt-0.5 flex flex-col gap-1.5">
        {#each signal.evidence as evidence, index (index)}<SignalEvidence {evidence} />{/each}
      </div>
    {/if}
    {#each signal.counterEvidence as counter, index (index)}
      <div class="mt-0.5 flex flex-col gap-1 border-l-2 border-border pl-3" data-counter-evidence>
        <p><span class="font-semibold">Counter-evidence:</span> {counter.explanation}</p>
        {#if counter.evidence}<SignalEvidence evidence={counter.evidence} />{/if}
      </div>
    {/each}
  </li>
{/snippet}

{#snippet signalList(signals: IntegritySignalView[])}
  <ul class="flex flex-col divide-y divide-border-subtle rounded-lg bg-secondary">
    {#each signals as signal (signal.id)}{@render signalItem(signal)}{/each}
  </ul>
{/snippet}

{#snippet assessment(kind: AssessmentKind, view: AuthenticityAssessmentView | AiAssistanceAssessmentView, level: LevelView)}
  {@const reasons = view.signals.filter((signal) => signal.countsTowardLevel)}
  {@const context = view.signals.filter((signal) => !signal.countsTowardLevel)}
  <section aria-labelledby={`${kind}-heading`} data-assessment={kind} class="flex flex-col gap-3 px-4.5 py-4 max-md:px-4">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div class="flex items-center gap-2">
        <h3 id={`${kind}-heading`} class="text-heading">{assessmentNames[kind]}</h3>
        {#if kind === 'aiAssistance'}<Badge variant="muted">Informational</Badge>{/if}
      </div>
      <IntegrityChip {kind} {level} dismissed={view.dismissal !== null} />
    </div>
    <p>{view.summary}</p>
    <!-- Every result keeps its next step (section 16): a calm one as a quiet line, the others in an inset. -->
    {#if level.rank > 0}
      <p class="rounded-lg bg-secondary px-3 py-2 text-secondary-foreground"><span class="font-semibold">Suggested next step:</span> {view.nextStep}</p>
    {:else}
      <p class="text-muted-foreground">Suggested next step: {view.nextStep}</p>
    {/if}

    {#if reasons.length > 0}
      <h4 class="flex items-center gap-2 text-caption text-muted-foreground">Reasons <Badge variant="count">{reasons.length}</Badge></h4>
      {@render signalList(reasons)}
    {/if}
    {#if context.length > 0}
      <h4 class="text-caption text-muted-foreground">Context, not counted toward the level</h4>
      {@render signalList(context)}
    {/if}

    <!-- The candidate's own AI declaration is on the page already (Declarations), so only the job's policy is here. -->
    {#if 'policy' in view}
      <p class="text-caption text-muted-foreground">Job policy: {aiPolicyLabels[view.policy]}</p>
    {/if}

    {#if view.dismissal}
      <p class="text-muted-foreground">
        Dismissed by {reviewerName(view.dismissal.actorUserId, view.dismissal.actorName, page.data.user?.id)} <RelativeTime at={view.dismissal.dismissedAt} class="inline" />:
        {dismissReasonLabels[view.dismissal.reason]}{view.dismissal.note ? `. "${view.dismissal.note}"` : '.'}
      </p>
    {:else if level.rank > 0}
      <div>
        <Button variant="outline" size="lg" onclick={() => startDismiss(kind, view.id)}>
          Dismiss {kind === 'authenticity' ? 'authenticity' : 'AI assistance'} with a reason
        </Button>
      </div>
    {/if}
    <p class="text-caption text-muted-foreground">Assessed <RelativeTime at={view.assessedAt} class="inline" /> with ruleset {view.rulesetVersion}</p>
  </section>
{/snippet}

<Panel title="Integrity" flush>
  <!-- A failed background refetch keeps the evidence on screen, under the error. -->
  {#if integrity.isError}
    <div class="px-4.5 py-4 max-md:px-4">
      <QueryError message={describeError(integrity.error, 'Could not load the integrity checks')} onretry={() => integrity.refetch()} inset />
    </div>
  {/if}
  {#if !detail}
    {#if !integrity.isError}
      <div aria-hidden="true" class="flex flex-col gap-3 px-4.5 py-4 max-md:px-4">
        <div class="flex items-center justify-between gap-2">
          <Skeleton class="h-6 w-28" />
          <Skeleton class="h-6 w-40 rounded-full" />
        </div>
        <Skeleton class="h-4 w-full" />
        <Skeleton class="h-10 w-full" />
      </div>
    {/if}
  {:else if detail.status === 'Pending' || (!detail.authenticity && !detail.aiAssistance)}
    <div class="flex flex-wrap items-center gap-2 px-4.5 py-4 text-muted-foreground max-md:px-4">
      <StatusBadge status="working" label="Checks pending" />
      <p>Checks are still running. This panel updates on its own when they finish.</p>
    </div>
  {:else}
    <div class="flex flex-col divide-y divide-border-subtle">
      {#if detail.authenticity}{@render assessment('authenticity', detail.authenticity, authenticityLevels[detail.authenticity.level])}{/if}
      {#if detail.aiAssistance}{@render assessment('aiAssistance', detail.aiAssistance, aiAssistanceLevels[detail.aiAssistance.level])}{/if}
    </div>
  {/if}
  <p role="status" class="border-t border-border-subtle px-4.5 py-3 text-muted-foreground empty:hidden max-md:px-4">{announcement}</p>

  {#snippet footer()}
    <p class="text-caption text-muted-foreground">No automated decision has been made. Every action is logged.</p>
  {/snippet}
</Panel>

<IntegrityDismissDialog bind:open={dismissOpen} {applicationId} assessment={dismissing} ondone={done} />
