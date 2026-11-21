<script lang="ts">
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import CircleCheckIcon from '~icons/lucide/circle-check';
  import { undoVerificationReasonExplanation, type CheckGroup } from '@screenhire/api-client';
  import { createGetApplicationVerification } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { page } from '$app/state';
  import Hint from '#lib/components/hint.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import VerificationDismissDialog from '#lib/components/verification-dismiss-dialog.svelte';
  import VerificationExplainDialog from '#lib/components/verification-explain-dialog.svelte';
  import VerificationFinding from '#lib/components/verification-finding.svelte';
  import VerificationGroupMap from '#lib/components/verification-group-map.svelte';
  import {
    aiAssistanceLevels,
    aiPolicyLabels,
    authenticityLevels,
    dismissReasonLabels,
    everyReasonExplained,
    findingsByGroup,
    groupAnchor,
    refetchAfterReview,
    reviewerName,
    type ExplainedReasonItem,
    type Finding,
  } from '#lib/verification.js';
  import { can } from '#lib/permissions.js';
  import { describeError } from '#lib/query.js';
  import { statuses, type StatusTone } from '#lib/status.js';

  // A candidate's Verification tab (ADR-0004, design system section 16, docs/architecture/verification-engine.md section 7).
  // The level is the header's mark, shown once; here are its suggested next step, AI help as information, a map of the six
  // groups of checks with what each found (the same tiles whether anything was found or not, so a clear result shows what was
  // looked at), and a panel for each group that found something, each finding opening to its evidence and counter-evidence.
  // When it was checked sits in the panel's header, the ruleset in that fact's tooltip, and the panel closes on the line that
  // no automated decision has been made (W10). It decides nothing. Marking a finding explained and dismissing are a person's, and
  // every one is an entry in the candidate's Activity; explaining every finding changes no level, and only a dismissal sets
  // the result aside. Nothing here rejects anyone or shows a score.
  let { applicationId }: { applicationId: string } = $props();

  const queryClient = useQueryClient();
  // VerificationAssessed refetches it live (#lib/realtime.ts).
  const verification = createGetApplicationVerification(() => applicationId);
  const detail = $derived(verification.data?.data);

  let dismissOpen = $state(false);
  let dismissExplained = $state<ExplainedReasonItem[]>([]);
  let explainOpen = $state(false);
  let explaining = $state<{ assessmentId: string; signalId: string; title: string } | null>(null);
  let undoing = $state<string | null>(null);
  let announcement = $state('');
  // Only a role that reviews verification sees the review controls; the API refuses everyone else anyway.
  const reviewer = $derived(can(page.data.user, 'ReviewVerification'));
  // Which findings are open; Expand all opens every one of a group.
  let expanded = $state<Record<string, boolean>>({});

  const authenticity = $derived(detail?.authenticity ?? null);
  const aiAssistance = $derived(detail?.aiAssistance ?? null);
  const level = $derived(authenticity ? authenticityLevels[authenticity.level] : undefined);
  // The result still needs a look and nobody set it aside: its findings can be explained and it can be dismissed.
  const concern = $derived(Boolean(authenticity && level && level.rank > 0 && !authenticity.dismissal));
  const findings = $derived(findingsByGroup(authenticity, aiAssistance));
  const groups = $derived(detail?.checkGroups ?? []);
  // A group with findings gets a panel, in the groups' order. Nothing else does.
  const panels = $derived(groups.filter((group) => findings[group.group].length > 0));
  // Clear: no finding in any group. It says so with a check, never "Verified" or "Genuine": the checks found nothing unusual,
  // which says nothing more about the person. The tiles say which groups found nothing, were off or did not run.
  const calm = $derived(panels.length === 0);
  // The next step's edge takes its level's colour, as the level's badge does.
  const edges: Record<StatusTone, string> = {
    clear: 'border-status-clear-mark',
    review: 'border-status-review-mark',
    verify: 'border-status-verify-mark',
    ai: 'border-status-ai-mark',
    neutral: 'border-border',
  };
  const audited = $derived(authenticity ?? aiAssistance);

  const canExplain = (finding: Finding) => reviewer && concern && finding.kind === 'authenticity' && finding.signal.countsTowardLevel && !finding.signal.explained;
  const canUndo = (finding: Finding) =>
    reviewer && concern && finding.kind === 'authenticity' && finding.signal.explained?.actorUserId === page.data.user?.id;

  function startDismiss() {
    // The findings people explained, in the panel's order, so the dialog lists their notes; they preselect nothing.
    dismissExplained = (authenticity?.signals ?? []).flatMap((signal) =>
      signal.explained
        ? [{ id: signal.id, title: signal.title, reason: signal.explained.reason, note: signal.explained.note, actor: reviewerName(signal.explained.actorUserId, signal.explained.actorName, page.data.user?.id) }]
        : [],
    );
    dismissOpen = true;
  }

  function startExplain(assessmentId: string, finding: Finding) {
    explaining = { assessmentId, signalId: finding.signal.id, title: finding.signal.title };
    explainOpen = true;
  }

  async function explained(message: string, recorded: boolean) {
    // An explained finding closes, so the ones still to read stand out. A refused one stays as the reader left it.
    if (recorded && explaining) expanded = { ...expanded, [explaining.signalId]: false };
    await done(message);
  }

  async function undo(finding: Finding) {
    undoing = finding.signal.id;
    try {
      const response = await undoVerificationReasonExplanation(applicationId, finding.assessmentId, finding.signal.id);
      await done(
        response.status === 200
          ? `Undid the explanation: ${finding.signal.title}.`
          : response.status === 409
            ? 'This finding is no longer explained, or the result changed. The tab now shows the latest.'
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

  const groupName = (group: CheckGroup) => groups.find((candidate) => candidate.group === group)?.label ?? group;
</script>

<!-- AI help is information: its badge and the job's policy, and for Some and Strong signs its summary and next step. It has no
     action and never changes the level. For No signs the summary and next step are for screen readers, since the badge says it. -->
{#snippet aiRow()}
  {#if aiAssistance}
    {@const ai = aiAssistanceLevels[aiAssistance.level]}
    <div class="flex flex-col gap-1.5" data-ai-row>
      <div class="flex flex-wrap items-center gap-2">
        <Hint content="Information only: it never changes the verification level.">
          {#snippet children({ props })}
            <span {...props} class="inline-flex rounded-sm">
              <StatusBadge status="ai" label={ai.label} />
              {#if ai.rank === 0}<span class="sr-only">. {aiAssistance.summary} {aiAssistance.nextStep}</span>{/if}
            </span>
          {/snippet}
        </Hint>
        <Badge variant="muted"><span class="sr-only">Job policy:</span> AI {aiPolicyLabels[aiAssistance.policy].toLocaleLowerCase()}</Badge>
      </div>
      {#if ai.rank > 0}
        <p class="max-w-measure text-muted-foreground" data-ai-summary>{aiAssistance.summary} {aiAssistance.nextStep}</p>
      {/if}
    </div>
  {/if}
{/snippet}

<!-- When the checks last assessed the application, with the ruleset one hover or focus away (W10). -->
{#snippet checked()}
  {#if audited}
    <p class="flex items-center gap-1 text-caption text-muted-foreground" data-audit>
      <Hint content={`Ruleset ${audited.rulesetVersion}`}>
        {#snippet children({ props })}<span {...props} class="rounded-sm underline decoration-dotted underline-offset-2">Checked</span>{/snippet}
      </Hint>
      <RelativeTime at={audited.assessedAt} class="inline" />
    </p>
  {/if}
{/snippet}

<div class="flex min-w-0 flex-col gap-5" data-verification>
  <!-- A failed background refetch keeps the evidence on screen, under the error. -->
  {#if verification.isError}
    <Panel title="Verification"><QueryError message={describeError(verification.error, 'Could not load the verification checks')} onretry={() => verification.refetch()} inset /></Panel>
  {/if}
  {#if !detail}
    {#if !verification.isError}
      <Panel title="Verification">
        <!-- The loaded shape (E1): the next step's callout, Dismiss, the AI row and the map's tiles. -->
        <div aria-hidden="true" class="flex flex-col gap-5">
          <Skeleton class="h-20 w-full" />
          <Skeleton class="h-9 w-24" />
          <Skeleton class="h-7 w-64" />
          <div class="grid grid-cols-[repeat(auto-fill,minmax(min(18rem,100%),1fr))] gap-2">
            {#each [1, 2, 3, 4, 5, 6] as tile (tile)}<Skeleton class="h-11 w-full" />{/each}
          </div>
        </div>
      </Panel>
    {/if}
  {:else if detail.status === 'Pending' || (!authenticity && !aiAssistance)}
    <Panel title="Verification">
      <div class="flex flex-wrap items-center gap-2 text-muted-foreground">
        <StatusBadge status="working" label="Checks pending" />
        <p>Checks are still running. This tab updates on its own when they finish.</p>
      </div>
      <p class="mt-4 text-caption text-muted-foreground" data-decisions>No automated decision has been made. Every action is logged.</p>
    </Panel>
  {:else}
    <Panel title="Verification" data-verification-summary actions={audited ? checked : undefined}>
      <div class="flex flex-col gap-5">
        <!-- The suggested next step every result carries (CLAUDE.md non-negotiable 1). It leads, in a callout in the level's colour,
             when the level needs a look; otherwise it is one quiet line, the summary then its next step (W10). -->
        {#if authenticity && level}
          {#if concern}
            <div data-summary class={['flex gap-3 rounded-md border-l-4 bg-secondary/60 px-4 py-3', edges[statuses[level.status].tone]]}>
              <ArrowRightIcon aria-hidden="true" class="mt-0.5 size-5 shrink-0" />
              <div class="flex min-w-0 flex-col gap-1">
                <p class="max-w-measure font-semibold" data-next-step>{authenticity.nextStep}</p>
                <p class="max-w-measure text-muted-foreground">{authenticity.summary}</p>
              </div>
            </div>
          {:else if calm && !authenticity.dismissal}
            <div data-summary data-clear class="flex items-start gap-2.5">
              <CircleCheckIcon aria-hidden="true" class="mt-0.5 size-5 shrink-0 text-status-clear-mark" />
              <div class="flex min-w-0 flex-col gap-0.5">
                <p class="font-semibold">Nothing unusual found</p>
                <p class="max-w-measure text-muted-foreground" data-next-step>{authenticity.nextStep}</p>
              </div>
            </div>
          {:else}
            <p data-summary class="max-w-measure text-muted-foreground">{authenticity.summary} <span data-next-step>{authenticity.nextStep}</span></p>
          {/if}

          <!-- Dismiss, or who dismissed it. One button: it reads Dismiss all once every finding that counts is explained, never a
               second Dismiss (W7). Either way it is one dismissal with its own reason. -->
          {#if authenticity.dismissal}
            <p class="max-w-measure text-muted-foreground" data-dismissal>
              Dismissed by {reviewerName(authenticity.dismissal.actorUserId, authenticity.dismissal.actorName, page.data.user?.id)} <RelativeTime at={authenticity.dismissal.dismissedAt} class="inline" />:
              {dismissReasonLabels[authenticity.dismissal.reason]}{authenticity.dismissal.note ? `. "${authenticity.dismissal.note}"` : '.'}
            </p>
          {:else if concern && reviewer}
            <div><Button variant="outline" size="lg" onclick={startDismiss}>{everyReasonExplained(authenticity.signals) ? 'Dismiss all' : 'Dismiss'}</Button></div>
          {:else if concern}
            <!-- Read-only says why (E3). -->
            <p class="max-w-measure text-muted-foreground">Your role can read these checks; ask a reviewer to explain or dismiss them.</p>
          {/if}
        {/if}

        {@render aiRow()}

        <VerificationGroupMap {groups} {findings} />
        <p role="status" class="text-muted-foreground empty:hidden">{announcement}</p>
        <!-- The panel's closing line (W10): what was decided, which is nothing. -->
        <p class="text-caption text-muted-foreground" data-decisions>No automated decision has been made. Every action is logged.</p>
      </div>
    </Panel>

    {#each panels as group (group.group)}
      {@const list = findings[group.group]}
      {@const allOpen = list.every((finding) => expanded[finding.signal.id])}
      <!-- A panel per group that found something, titled with the group's name and nothing else: no icon, no description. -->
      <Panel
        id={groupAnchor(group.group)}
        tabindex={-1}
        title={groupName(group.group)}
        data-check-group={group.group}
        class="scroll-mt-4 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
      >
        {#snippet actions()}
          <!-- Expand all is for a group with more than one finding; one finding opens on its own. -->
          {#if list.length > 1}
            <Button variant="outline" onclick={() => (expanded = { ...expanded, ...Object.fromEntries(list.map((finding) => [finding.signal.id, !allOpen])) })}>
              {allOpen ? 'Collapse all' : 'Expand all'}
            </Button>
          {/if}
        {/snippet}
        <div class="flex flex-col gap-3">
          {#if group.note}<p class="max-w-measure text-muted-foreground">{group.note}</p>{/if}
          <ul class="flex flex-col divide-y divide-border-subtle rounded-lg border border-border-subtle" aria-label="Findings">
            {#each list as finding (finding.signal.id)}
              <VerificationFinding
                signal={finding.signal}
                ai={finding.kind === 'aiAssistance'}
                open={expanded[finding.signal.id] ?? false}
                onopenchange={(open) => (expanded = { ...expanded, [finding.signal.id]: open })}
                canExplain={canExplain(finding)}
                canUndo={canUndo(finding)}
                undoing={undoing === finding.signal.id}
                onexplain={() => startExplain(finding.assessmentId, finding)}
                onundo={() => undo(finding)}
                currentUserId={page.data.user?.id}
              />
            {/each}
          </ul>
        </div>
      </Panel>
    {/each}
  {/if}

</div>

<VerificationDismissDialog bind:open={dismissOpen} {applicationId} assessment={authenticity ? { id: authenticity.id, kind: 'authenticity', level: authenticity.level } : null} explained={dismissExplained} ondone={done} />
<VerificationExplainDialog bind:open={explainOpen} {applicationId} reason={explaining} ondone={explained} />
