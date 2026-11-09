<script lang="ts">
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import ChevronDownIcon from '~icons/lucide/chevron-down';
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
    groupDidNotRun,
    groupStateText,
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
  // groups of checks with what each found, and a panel for each group that found something, each finding opening to its
  // evidence and counter-evidence. It decides nothing. Marking a finding explained and dismissing are a person's, and
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
  // Calm: no finding in any group. It is one line, the groups that did not run, and what was checked.
  const calm = $derived(panels.length === 0);
  // Said outright: a group that did not run, or whose signal a trusted domain or network stopped, so a quiet result is never
  // read as a clean one.
  const noted = $derived(groups.filter((group) => groupDidNotRun(group) || (group.note && group.found + group.notes === 0)));
  const ran = $derived(groups.filter((group) => !groupDidNotRun(group)));
  // The next step's edge takes its level's colour, as the level's badge does.
  const edges: Record<StatusTone, string> = {
    clear: 'border-status-clear-mark',
    review: 'border-status-review-mark',
    verify: 'border-status-verify-mark',
    ai: 'border-status-ai-mark',
    neutral: 'border-border',
  };
  const lastPanel = $derived(panels.at(-1)?.group);
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

{#snippet decisions()}
  <p class="text-caption text-muted-foreground">No automated decision has been made. Every action is logged.</p>
  {#if audited}
    <!-- When it was assessed and under which rules: audit details, so last (W10). Two facts with their own gap (W3). -->
    <p class="flex flex-wrap gap-x-4 text-caption text-muted-foreground" data-audit>
      <span>Assessed <RelativeTime at={audited.assessedAt} class="inline" /></span><span>Ruleset {audited.rulesetVersion}</span>
    </p>
  {/if}
{/snippet}

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
    <Panel title="Verification" footer={decisions}>
      <div class="flex flex-wrap items-center gap-2 text-muted-foreground">
        <StatusBadge status="working" label="Checks pending" />
        <p>Checks are still running. This tab updates on its own when they finish.</p>
      </div>
    </Panel>
  {:else}
    <Panel title="Verification" data-verification-summary footer={calm ? decisions : undefined}>
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

        {#if calm}
          {#if noted.length > 0}
            <ul class="flex flex-col gap-3" data-not-run>
              {#each noted as group (group.group)}
                <li class="flex flex-col gap-1" data-group={group.group}>
                  <div class="flex flex-wrap items-center gap-2">
                    <h3 class="min-w-0 font-semibold">{group.label}</h3>
                    <StatusBadge status="notInEffect" label={groupDidNotRun(group) || group.state === 'Paused' ? groupStateText(group) : 'Not counted'} quiet />
                  </div>
                  {#if group.note}<p class="max-w-measure text-muted-foreground">{group.note}</p>{/if}
                </li>
              {/each}
            </ul>
          {/if}
          <details class="group" data-checked>
            <summary class="flex min-h-11 list-none items-center justify-between gap-2 font-semibold [&::-webkit-details-marker]:hidden">
              What was checked
              <ChevronDownIcon aria-hidden="true" class="size-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-180" />
            </summary>
            <ul class="flex flex-col gap-1.5 pt-1 pb-2">
              {#each ran as group (group.group)}
                <li class="max-w-measure text-muted-foreground">
                  <span class="font-semibold text-foreground">{group.label}:</span> {group.checked}
                </li>
              {/each}
            </ul>
          </details>
        {:else}
          <VerificationGroupMap {groups} {findings} />
        {/if}
        <p role="status" class="text-muted-foreground empty:hidden">{announcement}</p>
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
        footer={group.group === lastPanel ? decisions : undefined}
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

<VerificationDismissDialog bind:open={dismissOpen} {applicationId} assessment={authenticity ? { id: authenticity.id, kind: 'authenticity' } : null} explained={dismissExplained} ondone={done} />
<VerificationExplainDialog bind:open={explainOpen} {applicationId} reason={explaining} ondone={explained} />
