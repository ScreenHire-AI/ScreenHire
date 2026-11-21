<script lang="ts">
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import type { ListVerificationActivityParams, VerificationActivityCheck, VerificationActivityRun } from '@screenhire/api-client';
  import { createListVerificationActivity } from '@screenhire/api-client/svelte-query';
  import Self from '#lib/components/verification-area/activity-rows.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { needsAttention } from '#lib/status.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { activityCheckLabel, activityGroupLabels, activityOutcomeLabels, activityOutcomeStatuses, callOutcomeLabels, runLabel } from '#lib/activity-log.js';
  import { formatCost, formatTokens, keySourceLabels, pageSize } from '#lib/ai-usage.js';
  import { formatDateTime } from '#lib/dates.js';
  import { describeError } from '#lib/query.js';
  import { cn } from '#lib/utils.js';

  // One page of the activity log (newest first), one row per run of the checks over an application, and after them a button
  // that renders the next page the same way: the cursor is the `nextBefore` of the page above, so a page never needs to know the
  // ones before it. Rendered inside the table, one tbody per page. A row's Outcome is its status and nothing else; the row opens
  // (A9) to its checks: why one did not run, what one found in the candidate tab's words, and for the AI check, with `usage`, the
  // model, key, tokens, cost and the provider's request id (the API sends those only to who can see AI usage). Thresholds,
  // weights and rules never appear.
  let { filters, before }: { filters: ListVerificationActivityParams; before?: string } = $props();

  const activity = createListVerificationActivity(() => ({ ...filters, before, limit: pageSize }));
  const page = $derived(activity.data?.data);
  const columns = 3;
  let more = $state(false);
  let open = $state<Record<string, boolean>>({});

  const detailId = (run: VerificationActivityRun) => `activity-run-${run.id.replace(/[^a-z0-9]/gi, '-')}`;
  const outcomeLabel = (check: VerificationActivityCheck) =>
    check.kind === 'ModelCall' ? (callOutcomeLabels[check.outcome] ?? activityOutcomeLabels[check.outcome]) : activityOutcomeLabels[check.outcome];
</script>

{#snippet outcome(value: VerificationActivityRun['outcome'], label: string)}
  <StatusBadge status={activityOutcomeStatuses[value]} {label} quiet={!needsAttention(activityOutcomeStatuses[value])} />
{/snippet}

{#if activity.isError && !page}
  <tbody>
    <tr>
      <td colspan={columns} class="p-4"><QueryError message={describeError(activity.error, 'Could not load the activity')} onretry={() => activity.refetch()} inset /></td>
    </tr>
  </tbody>
{:else if !page}
  <tbody>
    <tr>
      <td colspan={columns} class="p-4">
        <div aria-hidden="true" class="flex flex-col gap-2"><Skeleton class="h-10 w-full" /><Skeleton class="h-10 w-full" /></div>
        <p class="sr-only" role="status">Loading the activity…</p>
      </td>
    </tr>
  </tbody>
{:else}
  <Table.Body>
    {#each page.runs as run (run.id)}
      {@const expanded = open[run.id] ?? false}
      <Table.Row data-run={run.id} class="cursor-pointer hover:bg-secondary/60" onclick={(event) => {
        // The whole row opens it; the button inside is the control for keyboards and screen readers.
        if (!(event.target as HTMLElement).closest('button, a')) open = { ...open, [run.id]: !expanded };
      }}>
        <Table.Cell class="whitespace-nowrap tabular-nums"><time datetime={run.at} title={formatDateTime(run.at)}>{formatDateTime(run.at)}</time></Table.Cell>
        <Table.Cell>
          <button
            type="button"
            aria-expanded={expanded}
            aria-controls={expanded ? detailId(run) : undefined}
            onclick={() => (open = { ...open, [run.id]: !expanded })}
            class="-mx-1 inline-flex min-h-6 items-center gap-1.5 rounded-sm px-1 text-left focus-visible:outline-2 focus-visible:outline-ring pointer-coarse:min-h-11"
          >
            <span>{runLabel(run)}</span>
            <ChevronDownIcon
              aria-hidden="true"
              class={cn('size-4 shrink-0 text-muted-foreground transition-transform duration-180 ease-in-out motion-reduce:transition-none', expanded && 'rotate-180')}
            />
          </button>
        </Table.Cell>
        <Table.Cell class="whitespace-nowrap">
          {@render outcome(run.outcome, run.checks === 0 ? outcomeLabel(run.items[0]!) : activityOutcomeLabels[run.outcome])}
        </Table.Cell>
      </Table.Row>
      {#if expanded}
        <Table.Row id={detailId(run)} data-run-detail class="hover:bg-transparent">
          <Table.Cell colspan={columns} class="bg-muted/40 px-4.5 py-3 whitespace-normal max-md:px-4">
            <ul class="flex flex-col divide-y divide-border-subtle" aria-label={`Checks in ${runLabel(run)}`}>
              {#each run.items as check (check.id)}
                <li class="flex flex-col gap-1 py-2.5 first:pt-0 last:pb-0" data-check={check.check}>
                  <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
                    <span class="font-medium">{activityCheckLabel(check.check)}</span>
                    {#if check.group}<span class="text-muted-foreground">{activityGroupLabels[check.group]}</span>{/if}
                    {@render outcome(check.outcome, outcomeLabel(check))}
                  </div>
                  {#if check.reason}<p class="max-w-measure text-muted-foreground">{check.reason}</p>{/if}
                  {#if check.findings.length > 0}
                    <ul class="flex list-disc flex-col gap-0.5 pl-5" aria-label="What it found">
                      {#each check.findings as finding (finding)}<li class="max-w-measure">{finding}</li>{/each}
                    </ul>
                  {/if}
                  {#if check.call}
                    <!-- What a model call cost and where to find it in the provider's own logs: only for who can see AI usage. -->
                    <dl class="flex flex-wrap gap-x-5 gap-y-1 text-muted-foreground" data-call>
                      <div class="flex gap-1.5"><dt>Model</dt><dd class="break-all text-foreground">{check.call.model}</dd></div>
                      <div class="flex gap-1.5"><dt>Key</dt><dd class="text-foreground">{keySourceLabels[check.call.keySource]}</dd></div>
                      <div class="flex gap-1.5">
                        <dt>Tokens</dt>
                        <dd class="text-foreground tabular-nums">
                          {check.call.inputTokens === null ? 'Not reported' : formatTokens(check.call.inputTokens)} in, {check.call.outputTokens === null
                            ? 'not reported'
                            : formatTokens(check.call.outputTokens)} out
                        </dd>
                      </div>
                      <div class="flex gap-1.5"><dt>Cost</dt><dd class="text-foreground tabular-nums">{formatCost(check.call.cost)}</dd></div>
                      <div class="flex gap-1.5">
                        <dt>Provider request id</dt>
                        <dd class="break-all text-foreground">{check.call.providerRequestId ?? 'None sent'}</dd>
                      </div>
                    </dl>
                  {/if}
                </li>
              {/each}
            </ul>
            <!-- Audit details come last (W10): the application to open, then the ruleset the run was read under. -->
            <div class="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1">
              {#if run.applicationId}
                <a href={`/applications/${run.applicationId}`} class="inline-flex min-h-6 items-center underline underline-offset-2 pointer-coarse:min-h-11">Open application</a>
              {/if}
              {#if run.rulesetVersion}<span class="text-caption text-muted-foreground">Ruleset {run.rulesetVersion}</span>{/if}
            </div>
          </Table.Cell>
        </Table.Row>
      {/if}
    {:else}
      {#if !before}
        <Table.Row><Table.Cell colspan={columns} class="text-muted-foreground">No activity matches.</Table.Cell></Table.Row>
      {/if}
    {/each}
  </Table.Body>
  {#if page.nextBefore}
    {#if more}
      <Self {filters} before={page.nextBefore} />
    {:else}
      <tbody>
        <tr>
          <td colspan={columns} class="p-4 text-center"><Button variant="outline" size="lg" onclick={() => (more = true)}>Show more</Button></td>
        </tr>
      </tbody>
    {/if}
  {/if}
{/if}
