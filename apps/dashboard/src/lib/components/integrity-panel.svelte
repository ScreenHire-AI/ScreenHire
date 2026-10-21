<script lang="ts">
  import type {
    AiAssistanceAssessmentView,
    AuthenticityAssessmentView,
    IntegrityReviewEntry,
    IntegritySignalView,
  } from '@screenhire/api-client';
  import { createGetApplicationIntegrity } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { page } from '$app/state';
  import IntegrityChip from '#lib/components/integrity-chip.svelte';
  import IntegrityDismissDialog from '#lib/components/integrity-dismiss-dialog.svelte';
  import IntegrityNoteDialog from '#lib/components/integrity-note-dialog.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SignalEvidence from '#lib/components/signal-evidence.svelte';
  import StatusMark from '#lib/components/status-mark.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { formatDateTime, timeAgo } from '#lib/dates.js';
  import {
    aiAssistanceLevels,
    aiDeclarationLabels,
    aiPolicyLabels,
    assessmentNames,
    authenticityLevels,
    dismissReasonLabels,
    type AssessmentKind,
    type LevelView,
  } from '#lib/integrity.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath, jobsPath } from '#lib/realtime.js';

  // The integrity evidence panel (ADR-0004, docs/architecture/integrity-engine.md section 7): what was observed, why it
  // matters, what argues against it and a suggested next step. It decides nothing. Its actions are a person's and go
  // in the review log, and it has no reject control.
  let { applicationId }: { applicationId: string } = $props();

  const queryClient = useQueryClient();
  // IntegrityAssessed refetches it live (#lib/realtime.ts).
  const integrity = createGetApplicationIntegrity(() => applicationId);
  const detail = $derived(integrity.data?.data);

  let dismissOpen = $state(false);
  let dismissing = $state<{ id: string; kind: AssessmentKind } | null>(null);
  let noteOpen = $state(false);
  let announcement = $state('');

  function startDismiss(kind: AssessmentKind, id: string) {
    dismissing = { id, kind };
    dismissOpen = true;
  }

  // A dismissal or note changes this panel, the list chips (dismissed) and the "Needs review" counts, and no live
  // event announces it, so refetch them here.
  async function done(message: string) {
    announcement = message;
    await Promise.all([invalidatePath(queryClient, applicationsPath), invalidatePath(queryClient, jobsPath)]);
  }

  function actor(userId: string, name: string | null): string {
    if (userId === page.data.user?.id) return 'you';
    return name ?? 'a teammate';
  }

  function describeEntry(entry: IntegrityReviewEntry): string {
    const about = entry.assessmentKind === 'Authenticity' ? 'authenticity' : entry.assessmentKind === 'AiAssistance' ? 'AI assistance' : null;
    if (entry.type === 'Dismissed') return `Dismissed ${about ?? 'an assessment'}: ${entry.dismissReason ? dismissReasonLabels[entry.dismissReason] : 'no reason'}`;
    return about ? `Note on ${about}` : 'Note';
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
      <div class="mt-0.5 flex flex-col gap-1" data-counter-evidence>
        <p class="flex items-start gap-2 text-body text-status-clear">
          <StatusMark shape="circle" tone="clear" class="mt-1.5 size-[7px]" />
          <span><span class="font-bold">Counter-evidence</span> · {counter.explanation}</span>
        </p>
        {#if counter.evidence}<div class="pl-[15px]"><SignalEvidence evidence={counter.evidence} /></div>{/if}
      </div>
    {/each}
  </li>
{/snippet}

{#snippet assessment(kind: AssessmentKind, view: AuthenticityAssessmentView | AiAssistanceAssessmentView, level: LevelView)}
  {@const reasons = view.signals.filter((signal) => signal.countsTowardLevel)}
  {@const context = view.signals.filter((signal) => !signal.countsTowardLevel)}
  <section aria-labelledby={`${kind}-heading`} data-assessment={kind} class="flex flex-col gap-2.5 border-b border-border-subtle px-4 py-3.5">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h3 id={`${kind}-heading`} class="text-heading">
        {assessmentNames[kind]}
        {#if kind === 'aiAssistance'}<span class="font-medium text-muted-foreground">· informational</span>{/if}
      </h3>
      <IntegrityChip {kind} {level} dismissed={view.dismissal !== null} />
    </div>
    <p>{view.summary}</p>
    <p class="rounded-lg bg-secondary px-3 py-2 text-secondary-foreground">
      <span class="font-bold">Suggested next step:</span>
      {view.nextStep}
    </p>

    {#if reasons.length > 0}
      <h4 class="text-caption text-muted-foreground">
        Reasons · {reasons.length}
      </h4>
      <ul class="flex flex-col divide-y divide-border-subtle overflow-hidden rounded-[10px] border border-border-subtle bg-muted">
        {#each reasons as signal (signal.id)}{@render signalItem(signal)}{/each}
      </ul>
    {/if}
    {#if context.length > 0}
      <h4 class="text-caption text-muted-foreground">Context, not counted toward the level</h4>
      <ul class="flex flex-col divide-y divide-border-subtle overflow-hidden rounded-[10px] border border-border-subtle bg-muted">
        {#each context as signal (signal.id)}{@render signalItem(signal)}{/each}
      </ul>
    {/if}

    {#if 'policy' in view}
      <p class="text-caption text-muted-foreground">
        Job policy: {aiPolicyLabels[view.policy]} · Declared AI use: {aiDeclarationLabels[view.declaration]}
      </p>
    {/if}

    {#if view.dismissal}
      <p class="text-body text-muted-foreground">
        Dismissed by {actor(view.dismissal.actorUserId, view.dismissal.actorName)} <time datetime={view.dismissal.dismissedAt} title={formatDateTime(view.dismissal.dismissedAt)}>{timeAgo(view.dismissal.dismissedAt, Date.now())}</time>:
        {dismissReasonLabels[view.dismissal.reason]}{view.dismissal.note ? `. "${view.dismissal.note}"` : '.'}
      </p>
    {:else if level.rank > 0}
      <div>
        <Button variant="outline" size="sm" onclick={() => startDismiss(kind, view.id)}>
          Dismiss {kind === 'authenticity' ? 'authenticity' : 'AI assistance'} with a reason
        </Button>
      </div>
    {/if}
    <p class="text-caption text-muted-foreground">Assessed <time datetime={view.assessedAt} title={formatDateTime(view.assessedAt)}>{timeAgo(view.assessedAt, Date.now())}</time> · ruleset {view.rulesetVersion}</p>
  </section>
{/snippet}

<section aria-labelledby="integrity-heading" class="flex min-w-0 flex-col rounded-xl border border-border bg-card">
  <h2 id="integrity-heading" class="border-b border-border-subtle px-4 py-3 text-heading">Integrity</h2>

  <!-- A failed background refetch keeps the evidence on screen, under the error. -->
  {#if integrity.isError}
    <div class="px-4 py-3.5">
      <QueryError message={describeError(integrity.error, 'Could not load the integrity checks')} onretry={() => integrity.refetch()} />
    </div>
  {/if}
  {#if !detail}
    {#if !integrity.isError}
      <div aria-hidden="true" class="flex flex-col gap-2 px-4 py-3.5">
        <Skeleton class="h-5 w-48" />
        <Skeleton class="h-12 w-full" />
      </div>
    {/if}
  {:else if detail.status === 'Pending' || (!detail.authenticity && !detail.aiAssistance)}
    <p class="flex items-center gap-2 border-b border-border-subtle px-4 py-3.5 text-muted-foreground">
      <StatusMark shape="ring" tone="neutral" />
      Checks are still running. This panel updates on its own when they finish.
    </p>
  {:else}
    {#if detail.authenticity}{@render assessment('authenticity', detail.authenticity, authenticityLevels[detail.authenticity.level])}{/if}
    {#if detail.aiAssistance}{@render assessment('aiAssistance', detail.aiAssistance, aiAssistanceLevels[detail.aiAssistance.level])}{/if}
  {/if}

  {#if detail}
    <div class="flex flex-col gap-2.5 border-b border-border-subtle px-4 py-3.5">
      <div class="flex flex-wrap items-center gap-1.5">
        <!-- Disabled until identity verification ships (phase 2). A disabled button takes no focus, so the hint below is
             visible text as well as its description. -->
        <Button disabled aria-describedby="request-verification-hint">Request verification</Button>
        <Button variant="ghost" class="font-bold text-primary" onclick={() => (noteOpen = true)}>Add note</Button>
      </div>
      <p id="request-verification-hint" class="flex items-center gap-2 text-body text-muted-foreground">
        <span class="rounded-sm bg-secondary px-1.5 py-px text-caption text-secondary-foreground">Phase 2</span>
        {detail.requestVerification.reason ?? 'Identity verification is not available yet.'}
      </p>
      <p role="status" class="text-body text-muted-foreground empty:hidden">{announcement}</p>
    </div>

    <section aria-labelledby="review-log-heading" class="flex flex-col gap-2 border-b border-border-subtle px-4 py-3.5">
      <h3 id="review-log-heading" class="text-heading">Review log</h3>
      {#if detail.reviewLog.length === 0}
        <p class="text-muted-foreground">No review actions yet.</p>
      {:else}
        <ol class="flex flex-col gap-2.5">
          {#each detail.reviewLog.toReversed() as entry (entry.id)}
            <li class="flex flex-col gap-0.5" data-review-entry={entry.type}>
              <span class="font-semibold">
                {describeEntry(entry)}
                <span class="font-normal text-muted-foreground">by {actor(entry.actorUserId, entry.actorName)}</span>
              </span>
              {#if entry.note}<span class="text-secondary-foreground">"{entry.note}"</span>{/if}
              <time datetime={entry.occurredAt} title={formatDateTime(entry.occurredAt)} class="text-caption text-muted-foreground">{timeAgo(entry.occurredAt, Date.now())}</time>
            </li>
          {/each}
        </ol>
      {/if}
    </section>
  {/if}

  <p class="px-4 py-3 text-caption text-muted-foreground">No automated decision has been made. Every action is logged.</p>
</section>

<IntegrityDismissDialog bind:open={dismissOpen} {applicationId} assessment={dismissing} ondone={done} />
<IntegrityNoteDialog bind:open={noteOpen} {applicationId} ondone={done} />
