<script lang="ts">
  import BotIcon from '@lucide/svelte/icons/bot';
  import FileSearchIcon from '@lucide/svelte/icons/file-search';
  import FileTextIcon from '@lucide/svelte/icons/file-text';
  import GlobeIcon from '@lucide/svelte/icons/globe';
  import KeyboardIcon from '@lucide/svelte/icons/keyboard';
  import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
  import UserRoundIcon from '@lucide/svelte/icons/user-round';
  import type { AiAssistanceAssessmentView, AuthenticityAssessmentView, IntegritySignalView, SignalCategory } from '@screenhire/api-client';
  import { createGetApplicationIntegrity } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { page } from '$app/state';
  import IntegrityDismissDialog from '#lib/components/integrity-dismiss-dialog.svelte';
  import LevelScale from '#lib/components/level-scale.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SignalEvidence from '#lib/components/signal-evidence.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryBand from '#lib/components/summary-band.svelte';
  import SummaryCard from '#lib/components/summary-card.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import {
    aiAssistanceLevels,
    aiAssistanceOrder,
    aiPolicyLabels,
    assessmentNames,
    authenticityLevels,
    authenticityOrder,
    checkKinds,
    checkSummaries,
    dismissReasonLabels,
    refetchAfterReview,
    reviewerName,
    type AssessmentKind,
    type LevelView,
  } from '#lib/integrity.js';
  import { describeError } from '#lib/query.js';
  import { statuses } from '#lib/status.js';
  import { cn } from '#lib/utils.js';

  // A candidate's Integrity tab (ADR-0004, design system section 16, docs/architecture/integrity-engine.md section 7):
  // each assessment's level on its three-step scale, a summary card per kind of check, the reasons with their evidence
  // and counter-evidence, expanding, and a suggested next step. It decides nothing. A dismissal is a person's and goes
  // in the review log (`ReviewLog`), and nothing here rejects anyone or shows a score.
  let { applicationId }: { applicationId: string } = $props();

  const queryClient = useQueryClient();
  // IntegrityAssessed refetches it live (#lib/realtime.ts).
  const integrity = createGetApplicationIntegrity(() => applicationId);
  const detail = $derived(integrity.data?.data);

  let dismissOpen = $state(false);
  let dismissing = $state<{ id: string; kind: AssessmentKind } | null>(null);
  let announcement = $state('');
  // Which reasons are open; Expand all opens every one.
  let expanded = $state<Record<string, boolean>>({});

  const checkIcons: Record<SignalCategory, typeof GlobeIcon> = {
    Network: GlobeIcon,
    Identity: UserRoundIcon,
    Automation: BotIcon,
    Content: FileTextIcon,
    Document: FileSearchIcon,
    Behavioural: KeyboardIcon,
  };
  const checkTitle = (category: SignalCategory) => checkKinds.find((kind) => kind.category === category)?.title ?? category;
  const tinted: Record<string, string> = { review: 'border-status-review-mark', verify: 'border-status-verify-mark' };

  function startDismiss(kind: AssessmentKind, id: string) {
    dismissing = { id, kind };
    dismissOpen = true;
  }

  async function done(message: string) {
    announcement = message;
    await refetchAfterReview(queryClient);
  }

  const signalCount = (count: number) => `${count} ${count === 1 ? 'signal' : 'signals'}`;
</script>

{#snippet signalItem(signal: IntegritySignalView)}
  <li data-signal={signal.code}>
    <details bind:open={() => expanded[signal.id] ?? false, (open) => (expanded = { ...expanded, [signal.id]: open })} class="group">
      <summary class="flex min-h-11 cursor-pointer list-none flex-wrap items-center gap-x-2 gap-y-1 px-3 py-2.5 [&::-webkit-details-marker]:hidden">
        <span class="min-w-0 flex-1 font-semibold">{signal.title}</span>
        <Badge variant="muted">{checkTitle(signal.category)}</Badge>
        <span class="font-mono text-code text-muted-foreground">{signal.code}</span>
      </summary>
      <div class="flex flex-col gap-1.5 px-3 pb-3">
        <p class="max-w-measure text-secondary-foreground">{signal.explanation}</p>
        {#if signal.evidence.length > 0}
          <div class="flex flex-col gap-1.5">
            {#each signal.evidence as evidence, index (index)}<SignalEvidence {evidence} />{/each}
          </div>
        {/if}
        {#each signal.counterEvidence as counter, index (index)}
          <div class="flex flex-col gap-1 border-l-2 border-border pl-3" data-counter-evidence>
            <p class="max-w-measure"><span class="font-semibold">Counter-evidence:</span> {counter.explanation}</p>
            {#if counter.evidence}<SignalEvidence evidence={counter.evidence} />{/if}
          </div>
        {/each}
      </div>
    </details>
  </li>
{/snippet}

{#snippet signalList(signals: IntegritySignalView[])}
  <ul class="flex flex-col divide-y divide-border-subtle rounded-lg bg-secondary">
    {#each signals as signal (signal.id)}{@render signalItem(signal)}{/each}
  </ul>
{/snippet}

{#snippet reasonsList(heading: string, signals: IntegritySignalView[], count: boolean)}
  {@const allOpen = signals.every((signal) => expanded[signal.id])}
  <div class="flex flex-wrap items-center justify-between gap-2">
    <h3 class="flex items-center gap-2 text-caption font-semibold text-muted-foreground">{heading}{#if count}{' '}<Badge variant="count">{signals.length}</Badge>{/if}</h3>
    <Button
      variant="outline"
      onclick={() => (expanded = { ...expanded, ...Object.fromEntries(signals.map((signal) => [signal.id, !allOpen])) })}
    >
      {allOpen ? 'Collapse all' : 'Expand all'}
    </Button>
  </div>
  {@render signalList(signals)}
{/snippet}

{#snippet decisions()}
  <p class="text-caption text-muted-foreground">No automated decision has been made. Every action is logged.</p>
  <p role="status" class="text-muted-foreground empty:hidden">{announcement}</p>
{/snippet}

{#snippet assessment(kind: AssessmentKind, view: AuthenticityAssessmentView | AiAssistanceAssessmentView, level: LevelView, levels: readonly LevelView[], last: boolean)}
  {@const reasons = view.signals.filter((signal) => signal.countsTowardLevel)}
  {@const context = view.signals.filter((signal) => !signal.countsTowardLevel)}
  {@const fed = kind === 'authenticity' && level.rank > 0 && view.dismissal === null}
  <Panel title={assessmentNames[kind]} flush data-assessment={kind} footer={last ? decisions : undefined}>
    {#snippet actions()}
      {#if kind === 'aiAssistance'}<Badge variant="muted">Informational</Badge>{/if}
      <span class="text-caption text-muted-foreground max-md:hidden">Assessed <RelativeTime at={view.assessedAt} class="inline" /></span>
    {/snippet}
    <div class="flex flex-col gap-4 px-4.5 py-4 max-md:px-4">
      <LevelScale {levels} current={level} name={assessmentNames[kind]} />
      <p class="max-w-measure">{view.summary}</p>
    </div>
    {#if kind === 'authenticity'}
      <SummaryBand>
        {#each checkSummaries(view.signals) as check (check.category)}
          {@const found = check.signals.length > 0}
          <SummaryCard
            icon={checkIcons[check.category]}
            title={check.title}
            count={found && !fed ? signalCount(check.signals.length) : undefined}
            badge={found && fed ? fedBadge : undefined}
            description={found ? check.signals.map((signal) => signal.title).join('. ') : 'Nothing found.'}
            class={cn(found && fed && tinted[statuses[level.status].tone])}
          />
          {#snippet fedBadge()}
            <Badge variant={statuses[level.status].tone} class="h-6 gap-1.5 pr-2.5 pl-2"><TriangleAlertIcon aria-hidden="true" />{signalCount(check.signals.length)}</Badge>
          {/snippet}
        {/each}
      </SummaryBand>
    {/if}
    <div class="flex flex-col gap-3 px-4.5 py-4 max-md:px-4">
      <!-- Every result keeps its next step (section 16): a calm one as a quiet line, the others in an inset. -->
      {#if level.rank > 0}
        <p class="rounded-lg bg-secondary px-3 py-2 text-secondary-foreground"><span class="font-semibold">Suggested next step:</span> {view.nextStep}</p>
      {:else}
        <p class="text-muted-foreground">Suggested next step: {view.nextStep}</p>
      {/if}

      {#if reasons.length > 0}{@render reasonsList('Reasons', reasons, true)}{/if}
      {#if context.length > 0}{@render reasonsList('Context, not counted toward the level', context, false)}{/if}

      <!-- The candidate's own AI declaration is on the Application tab already, so only the job's policy is here (W4). -->
      {#if 'policy' in view}
        <p class="text-caption text-muted-foreground">Job policy: {aiPolicyLabels[view.policy]}</p>
      {/if}

      {#if view.dismissal}
        <p class="max-w-measure text-muted-foreground">
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
      <p class="text-caption text-muted-foreground">Ruleset {view.rulesetVersion}</p>
    </div>
  </Panel>
{/snippet}

<div class="flex min-w-0 flex-col gap-5" data-integrity>
  <!-- A failed background refetch keeps the evidence on screen, under the error. -->
  {#if integrity.isError}
    <Panel title="Integrity"><QueryError message={describeError(integrity.error, 'Could not load the integrity checks')} onretry={() => integrity.refetch()} inset /></Panel>
  {/if}
  {#if !detail}
    {#if !integrity.isError}
      <Panel title="Integrity checks">
        <div aria-hidden="true" class="flex flex-col gap-3">
          <Skeleton class="h-12 w-full" />
          <Skeleton class="h-4 w-full" />
          <Skeleton class="h-24 w-full" />
        </div>
      </Panel>
    {/if}
  {:else if detail.status === 'Pending' || (!detail.authenticity && !detail.aiAssistance)}
    <Panel title="Integrity checks" footer={decisions}>
      <div class="flex flex-wrap items-center gap-2 text-muted-foreground">
        <StatusBadge status="working" label="Checks pending" />
        <p>Checks are still running. This tab updates on its own when they finish.</p>
      </div>
    </Panel>
  {:else}
    {#if detail.authenticity}{@render assessment('authenticity', detail.authenticity, authenticityLevels[detail.authenticity.level], authenticityOrder.map((id) => authenticityLevels[id]), !detail.aiAssistance)}{/if}
    {#if detail.aiAssistance}{@render assessment('aiAssistance', detail.aiAssistance, aiAssistanceLevels[detail.aiAssistance.level], aiAssistanceOrder.map((id) => aiAssistanceLevels[id]), true)}{/if}
  {/if}
</div>

<IntegrityDismissDialog bind:open={dismissOpen} {applicationId} assessment={dismissing} ondone={done} />
