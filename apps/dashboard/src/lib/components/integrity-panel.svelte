<script lang="ts">
  import ArrowRightIcon from '@lucide/svelte/icons/arrow-right';
  import BotIcon from '@lucide/svelte/icons/bot';
  import FileSearchIcon from '@lucide/svelte/icons/file-search';
  import FileTextIcon from '@lucide/svelte/icons/file-text';
  import GlobeIcon from '@lucide/svelte/icons/globe';
  import InfoIcon from '@lucide/svelte/icons/info';
  import KeyboardIcon from '@lucide/svelte/icons/keyboard';
  import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
  import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
  import UserRoundIcon from '@lucide/svelte/icons/user-round';
  import {
    undoIntegrityReasonExplanation,
    type AiAssistanceAssessmentView,
    type AuthenticityAssessmentView,
    type ExplainReason,
    type IntegritySignalView,
    type SignalCategory,
  } from '@screenhire/api-client';
  import { createGetApplicationIntegrity } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { page } from '$app/state';
  import IntegrityDismissDialog from '#lib/components/integrity-dismiss-dialog.svelte';
  import IntegrityExplainDialog from '#lib/components/integrity-explain-dialog.svelte';
  import LevelScale from '#lib/components/level-scale.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SignalEvidence, { isWideEvidence } from '#lib/components/signal-evidence.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
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
    everyReasonExplained,
    explainReasonLabels,
    refetchAfterReview,
    reviewerName,
    type AssessmentKind,
    type LevelView,
  } from '#lib/integrity.js';
  import { can } from '#lib/permissions.js';
  import { describeError } from '#lib/query.js';
  import { statuses, type StatusTone } from '#lib/status.js';

  // A candidate's Integrity tab (ADR-0004, design system section 16, docs/architecture/integrity-engine.md section 7):
  // each assessment's level on its three-step scale, its suggested next step, then its reasons grouped by the kind of
  // check that found them, each expanding to its evidence and counter-evidence. It decides nothing. Marking a reason
  // explained and dismissing are a person's and go in the review log (`ReviewLog`); explaining every reason changes no
  // level, and only a dismissal sets an assessment aside. Nothing here rejects anyone or shows a score.
  let { applicationId }: { applicationId: string } = $props();

  const queryClient = useQueryClient();
  // IntegrityAssessed refetches it live (#lib/realtime.ts).
  const integrity = createGetApplicationIntegrity(() => applicationId);
  const detail = $derived(integrity.data?.data);

  let dismissOpen = $state(false);
  let dismissing = $state<{ id: string; kind: AssessmentKind } | null>(null);
  let dismissExplained = $state<{ id: string; title: string; reason: ExplainReason; note: string | null; actor: string }[]>([]);
  let explainOpen = $state(false);
  let explaining = $state<{ assessmentId: string; signalId: string; title: string } | null>(null);
  let undoing = $state<string | null>(null);
  let announcement = $state('');
  // Only a role that reviews integrity sees the review controls; the API refuses everyone else anyway.
  const reviewer = $derived(can(page.data.user, 'ReviewIntegrity'));
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
  // The next step's edge takes its level's colour, as the level's badge does.
  const edges: Record<StatusTone, string> = {
    clear: 'border-status-clear-mark',
    review: 'border-status-review-mark',
    verify: 'border-status-verify-mark',
    ai: 'border-status-ai-mark',
    neutral: 'border-border',
  };

  /** One row of an assessment's reasons: a kind of check and what it found, or the context that does not count. */
  interface ReasonGroup {
    key: string;
    icon: typeof GlobeIcon;
    title: string;
    caption?: string;
    signals: IntegritySignalView[];
    /** The count, in the level's colour with a warning when the check fed a level that needs a look. */
    count: 'alert' | 'plain' | 'none';
  }

  function reasonGroups(kind: AssessmentKind, signals: IntegritySignalView[], fed: boolean): ReasonGroup[] {
    // Authenticity lists its four checks even when one found nothing; AI assistance only the checks that found something.
    const checks = checkSummaries(signals).filter((check) => kind === 'authenticity' || check.signals.length > 0);
    const context = signals.filter((signal) => !signal.countsTowardLevel);
    return [
      ...checks.map((check) => ({
        key: check.category,
        icon: checkIcons[check.category],
        title: check.title,
        signals: check.signals,
        count: (check.signals.length === 0 ? 'none' : fed ? 'alert' : 'plain') as ReasonGroup['count'],
      })),
      ...(context.length > 0 ? [{ key: 'context', icon: InfoIcon, title: 'Context', caption: 'Not counted toward the level', signals: context, count: 'none' as const }] : []),
    ];
  }

  /** Where a reason sits: its assessment, and whether that assessment is still open to explaining and dismissing. */
  interface ReviewContext {
    assessmentId: string;
    open: boolean;
  }

  function startDismiss(kind: AssessmentKind, view: AuthenticityAssessmentView | AiAssistanceAssessmentView) {
    dismissing = { id: view.id, kind };
    // The reasons people explained, so the dialog lists their notes; they preselect nothing.
    dismissExplained = view.signals.flatMap((signal) =>
      signal.explained
        ? [{ id: signal.id, title: signal.title, reason: signal.explained.reason, note: signal.explained.note, actor: reviewerName(signal.explained.actorUserId, signal.explained.actorName, page.data.user?.id) }]
        : [],
    );
    dismissOpen = true;
  }

  function startExplain(assessmentId: string, signal: IntegritySignalView) {
    explaining = { assessmentId, signalId: signal.id, title: signal.title };
    explainOpen = true;
  }

  async function explained(message: string) {
    // An explained reason closes, so the ones still to read stand out.
    if (explaining) expanded = { ...expanded, [explaining.signalId]: false };
    await done(message);
  }

  async function undo(assessmentId: string, signal: IntegritySignalView) {
    undoing = signal.id;
    try {
      const response = await undoIntegrityReasonExplanation(applicationId, assessmentId, signal.id);
      await done(
        response.status === 200
          ? `Undid the explanation: ${signal.title}.`
          : response.status === 409
            ? 'This reason is no longer explained, or the assessment changed. The panel now shows the latest.'
            : "We couldn't undo the explanation just now. Try again in a minute.",
      );
    } finally {
      undoing = null;
    }
  }

  async function done(message: string) {
    announcement = message;
    await refetchAfterReview(queryClient);
  }

</script>

{#snippet signalItem(signal: IntegritySignalView, showCheck: boolean, review: ReviewContext)}
  {@const short = signal.evidence.filter((evidence) => !isWideEvidence(evidence))}
  {@const wide = signal.evidence.filter(isWideEvidence)}
  <!-- Open, the reason's title, its detail and any explanation share one soft background, with nothing boxed inside it. -->
  <li data-signal={signal.code} data-explained={signal.explained ? '' : undefined} class="has-[details[open]]:bg-secondary/60">
    <details bind:open={() => expanded[signal.id] ?? false, (open) => (expanded = { ...expanded, [signal.id]: open })} class="group">
      <!-- It opens in place, so it shows the chevron that says so, turning up when open (A9). -->
      <summary class="flex min-h-11 cursor-pointer list-none flex-wrap items-center gap-x-2 gap-y-1 px-3 py-2.5 hover:bg-secondary/60 [&::-webkit-details-marker]:hidden">
        <span class="min-w-0 flex-1 font-semibold">{signal.title}</span>
        {#if showCheck}<span class="text-caption text-muted-foreground">{checkTitle(signal.category)}</span>{/if}
        <!-- A state of the reason, so a badge on its row (W6); calm, so quiet (T3). -->
        {#if signal.explained}<StatusBadge status="done" label="Explained" quiet />{/if}
        <ChevronDownIcon aria-hidden="true" class="size-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-180" />
      </summary>
      <!-- What was observed, then the short evidence as a facts row (a label above each value, as in a header), any
           quote or comparison below it, the counter-evidence as the other side of the argument, and the code, which is
           there for support and audit (W6). -->
      <div class="flex flex-col gap-4 px-3 pt-1 pb-5" data-evidence>
        <!-- The reading size the design system gives integrity explanations (section 4). -->
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
        <!-- The reason's own action shares the row with its code, on the left (F6). -->
        <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
          {#if review.open && reviewer && !signal.explained}
            <Button variant="outline" onclick={() => startExplain(review.assessmentId, signal)}>Mark explained</Button>
          {/if}
          <p class="ml-auto text-caption text-muted-foreground">Code <span class="font-mono text-code">{signal.code}</span></p>
        </div>
      </div>
    </details>
    <!-- Who explained it and why, under the row so it reads while the reason is closed. Undo cannot sit inside the
         summary, which is itself the control (A9). -->
    {#if signal.explained}
      {@const explanation = signal.explained}
      <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 px-3 pb-2.5" data-explanation>
        <p class="min-w-0 max-w-measure text-muted-foreground">
          Explained by {reviewerName(explanation.actorUserId, explanation.actorName, page.data.user?.id)} <RelativeTime at={explanation.explainedAt} class="inline" />:
          {explainReasonLabels[explanation.reason]}{explanation.note ? `. "${explanation.note}"` : '.'}
        </p>
        {#if review.open && reviewer && explanation.actorUserId === page.data.user?.id}
          <Button variant="outline" disabled={undoing === signal.id} onclick={() => undo(review.assessmentId, signal)}>Undo</Button>
        {/if}
      </div>
    {/if}
  </li>
{/snippet}

{#snippet expandAll(signals: IntegritySignalView[])}
  {@const allOpen = signals.every((signal) => expanded[signal.id])}
  <Button variant="outline" onclick={() => (expanded = { ...expanded, ...Object.fromEntries(signals.map((signal) => [signal.id, !allOpen])) })}>
    {allOpen ? 'Collapse all' : 'Expand all'}
  </Button>
{/snippet}

<!-- An assessment's reasons, grouped by the kind of check that found them, then the context that does not count: each
     group's icon, name and how many signals it found, then those reasons, each opening to its evidence (A9). One
     structure, so a check and its reasons never repeat each other (W6), and no heading above it: the list says what it
     is. A check that found nothing says so in one quiet line. -->
{#snippet groupList(groups: ReasonGroup[], level: LevelView, review: ReviewContext)}
  <ul class="flex flex-col divide-y divide-border-subtle rounded-lg border border-border-subtle" aria-label="Reasons" data-checks>
    {#each groups as group (group.key)}
      {@const Icon = group.icon}
      <!-- On a wide page the group's name and count sit in a column beside its reasons, so a group takes no row of its
           own; on a narrow one they stack. -->
      <li class="grid @3xl:grid-cols-[14rem_minmax(0,1fr)]" data-check={group.key}>
        <div class="flex min-h-11 items-center gap-3 px-3 py-2 @3xl:items-start @3xl:pt-2.5">
          <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground @3xl:-mt-1"><Icon class="size-4" /></span>
          <div class="flex min-w-0 flex-col gap-0.5">
            <!-- The count right after the name, so the two read as one: "Network 2". -->
            <div class="flex flex-wrap items-center gap-2">
              <h3 class="min-w-0 font-semibold">{group.title}</h3>
              {#if group.count === 'alert'}
                <Badge variant={statuses[level.status].tone} class="h-6 gap-1.5 pr-2.5 pl-2 @3xl:-mt-0.5"><TriangleAlertIcon aria-hidden="true" />{group.signals.length}<span class="sr-only">{' '}{group.signals.length === 1 ? 'signal' : 'signals'}</span></Badge>
              {:else if group.count === 'plain'}
                <Badge variant="count">{group.signals.length}<span class="sr-only">{' '}{group.signals.length === 1 ? 'signal' : 'signals'}</span></Badge>
              {/if}
            </div>
            {#if group.caption}<p class="text-caption text-muted-foreground">{group.caption}</p>{/if}
          </div>
        </div>
        {#if group.signals.length > 0}
          <ul class="flex flex-col divide-y divide-border-subtle pl-10 @3xl:pl-0">
            {#each group.signals as signal (signal.id)}{@render signalItem(signal, group.key === 'context', review)}{/each}
          </ul>
        {:else}
          <p class="px-3 pb-2.5 pl-13 text-muted-foreground @3xl:py-2.5 @3xl:pl-3">Nothing found</p>
        {/if}
      </li>
    {/each}
  </ul>
{/snippet}

{#snippet decisions()}
  <p class="text-caption text-muted-foreground">No automated decision has been made. Every action is logged.</p>
  <p role="status" class="text-muted-foreground empty:hidden">{announcement}</p>
{/snippet}

{#snippet assessment(kind: AssessmentKind, view: AuthenticityAssessmentView | AiAssistanceAssessmentView, level: LevelView, levels: readonly LevelView[], last: boolean)}
  {@const reasons = view.signals.filter((signal) => signal.countsTowardLevel)}
  {@const fed = kind === 'authenticity' && level.rank > 0 && view.dismissal === null}
  {@const concern = level.rank > 0 && view.dismissal === null}
  <!-- A calm authenticity result has no list of empty checks: its summary already says nothing was found (W7). -->
  {@const groups = kind === 'authenticity' && reasons.length === 0 ? reasonGroups(kind, [], false).filter((group) => group.key === 'context' || group.signals.length > 0) : reasonGroups(kind, view.signals, fed)}
  {@const all = groups.flatMap((group) => group.signals)}
  <!-- One skeleton for every assessment (section 16): its name, level and, for AI assistance, the job's policy as pills
       in the header; what to do and why; its reasons; then Dismiss and the audit details. The next step leads, as a
       callout in the level's colour, when the level needs a look; a calm result is one quiet line. No labels: each part
       says what it is. -->
  <Panel title={assessmentNames[kind]} data-assessment={kind} footer={last ? decisions : undefined}>
    {#snippet tools()}
      <!-- An authenticity level that needs a look is already the candidate's status in the page header, so here it is
           only its place on the three-step scale (W7). A calm or dismissed one, and AI assistance, show nowhere else. -->
      {#if kind !== 'authenticity' || level.rank === 0 || view.dismissal}<StatusBadge status={level.status} label={level.label} />{/if}
      <LevelScale {levels} current={level} name={assessmentNames[kind]} compact />
      <!-- The job's policy, so a reader sees why AI help is or is not a concern. The candidate's own declaration is on
           the Application tab (W7). -->
      {#if 'policy' in view}<Badge variant="muted"><span class="sr-only">Job policy:</span> AI {aiPolicyLabels[view.policy].toLocaleLowerCase()}</Badge>{/if}
    {/snippet}
    {#snippet actions()}
      {#if kind === 'aiAssistance'}<Badge variant="muted">Informational</Badge>{/if}
      {#if all.length > 0}{@render expandAll(all)}{/if}
    {/snippet}
    <div class="flex flex-col gap-5">
      <!-- The plain-language summary and suggested next step every result carries (CLAUDE.md non-negotiable 1). -->
      {#if concern}
        <div data-summary class={['flex gap-3 rounded-md border-l-4 bg-secondary/60 px-4 py-3', edges[statuses[level.status].tone]]}>
          <ArrowRightIcon aria-hidden="true" class="mt-0.5 size-5 shrink-0" />
          <div class="flex min-w-0 flex-col gap-1">
            <p class="max-w-measure font-semibold" data-next-step>{view.nextStep}</p>
            <p class="max-w-measure text-muted-foreground">{view.summary}</p>
          </div>
        </div>
      {:else}
        <p data-summary class="max-w-measure text-muted-foreground">{view.summary} <span data-next-step>{view.nextStep}</span></p>
      {/if}

      {#if groups.length > 0}{@render groupList(groups, level, { assessmentId: view.id, open: concern })}{/if}

      <!-- Dismiss, or who dismissed it, then when it was assessed and under which rules: audit details, so last. -->
      <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        {#if view.dismissal}
          <p class="max-w-measure text-muted-foreground">
            Dismissed by {reviewerName(view.dismissal.actorUserId, view.dismissal.actorName, page.data.user?.id)} <RelativeTime at={view.dismissal.dismissedAt} class="inline" />:
            {dismissReasonLabels[view.dismissal.reason]}{view.dismissal.note ? `. "${view.dismissal.note}"` : '.'}
          </p>
        {:else if level.rank > 0 && reviewer}
          <!-- One button: it reads Dismiss all once every reason that counts is explained, never a second Dismiss (W7).
               Either way it is one dismissal with its own reason. -->
          <Button variant="outline" size="lg" onclick={() => startDismiss(kind, view)}>{everyReasonExplained(view.signals) ? 'Dismiss all' : 'Dismiss'}</Button>
        {:else if level.rank > 0}
          <!-- Read-only says why (E3). -->
          <p class="max-w-measure text-muted-foreground">Your role can read these checks. A reviewer explains reasons and dismisses.</p>
        {:else}
          <span></span>
        {/if}
        <!-- Two facts with their own gap, not a dot-separated string (W3). -->
        <p class="flex flex-wrap gap-x-4 text-caption text-muted-foreground" data-audit>
          <span>Assessed <RelativeTime at={view.assessedAt} class="inline" /></span><span>Ruleset {view.rulesetVersion}</span>
        </p>
      </div>
    </div>
  </Panel>
{/snippet}

<div class="flex min-w-0 flex-col gap-5" data-integrity>
  <!-- A failed background refetch keeps the evidence on screen, under the error. -->
  {#if integrity.isError}
    <Panel title="Verification"><QueryError message={describeError(integrity.error, 'Could not load the verification checks')} onretry={() => integrity.refetch()} inset /></Panel>
  {/if}
  {#if !detail}
    {#if !integrity.isError}
      <Panel title="Verification checks">
        <div aria-hidden="true" class="flex flex-col gap-3">
          <Skeleton class="h-12 w-full" />
          <Skeleton class="h-4 w-full" />
          <Skeleton class="h-24 w-full" />
        </div>
      </Panel>
    {/if}
  {:else if detail.status === 'Pending' || (!detail.authenticity && !detail.aiAssistance)}
    <Panel title="Verification checks" footer={decisions}>
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

<IntegrityDismissDialog bind:open={dismissOpen} {applicationId} assessment={dismissing} explained={dismissExplained} ondone={done} />
<IntegrityExplainDialog bind:open={explainOpen} {applicationId} reason={explaining} ondone={explained} />
