<script lang="ts">
  import DownloadIcon from '~icons/lucide/download';
  import type { AiKeySource, CheckGroup, ListVerificationActivityParams, VerificationActivityOutcome } from '@screenhire/api-client';
  import { getExportVerificationActivityUrl } from '@screenhire/api-client/svelte-query';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import ActivityRows from '#lib/components/verification-area/activity-rows.svelte';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { cn } from '#lib/utils.js';
  import { activityGroupLabels, activityOutcomeLabels } from '#lib/activity-log.js';
  import { keySourceLabels } from '#lib/ai-usage.js';
  import { canSeeUsage } from '#lib/verification-area.js';
  import type { PageProps } from './$types';

  // The Activity log tab: every check that ran and every model call the AI checks made, newest first, with the provider's own
  // request id on each call so a bill can be compared with the provider's dashboard. Everyone who sees verification reads
  // what ran; a call's model, cost and request id, and the CSV, are for who can see AI usage. Never about a candidate.
  let { data }: PageProps = $props();

  const usage = $derived(canSeeUsage(data.user));

  const ranges = [
    { value: '', label: 'Any time', days: null },
    { value: '7', label: 'Last 7 days', days: 7 },
    { value: '30', label: 'Last 30 days', days: 30 },
    { value: '90', label: 'Last 90 days', days: 90 },
  ] as const;
  let range = $state<string>('');
  // Fixed when the range is chosen, so the same range is the same query and the pages below it line up.
  let from = $state<string | undefined>(undefined);
  let group = $state<'' | CheckGroup>('');
  let keySource = $state<'' | AiKeySource>('');
  let outcome = $state<'' | VerificationActivityOutcome>('');

  function chooseRange(value: string) {
    range = value;
    const days = ranges.find((r) => r.value === value)?.days;
    from = days ? new Date(Date.now() - days * 24 * 60 * 60 * 1000).toISOString() : undefined;
  }

  const filters = $derived<ListVerificationActivityParams>({
    ...(from ? { from } : {}),
    ...(group ? { group } : {}),
    ...(usage && keySource ? { keySource } : {}),
    ...(outcome ? { outcome } : {}),
  });
  const exportUrl = $derived(getExportVerificationActivityUrl(filters));
  const groups = Object.keys(activityGroupLabels) as CheckGroup[];
  const outcomes = Object.keys(activityOutcomeLabels) as VerificationActivityOutcome[];
</script>

<svelte:head>
  <title>Activity log · Verification · ScreenHire</title>
</svelte:head>

<Panel label="Activity" flush>
  {#snippet actions()}
    {#if usage}
      <Button href={exportUrl} download variant="outline" size="default"><DownloadIcon aria-hidden="true" />Export CSV</Button>
    {/if}
  {/snippet}
  <!-- The filters: a row under the header that wraps by content on a phone rather than cutting a select off (section 7). -->
  <div data-filters class="flex shrink-0 flex-wrap items-center gap-2 border-b border-border-subtle px-4.5 py-2 max-md:px-4">
    <select class={cn(selectClass, 'w-auto shrink-0')} value={range} onchange={(event) => chooseRange(event.currentTarget.value)} aria-label="Filter by date">
      {#each ranges as option (option.value)}<option value={option.value}>{option.label}</option>{/each}
    </select>
    <select class={cn(selectClass, 'w-auto shrink-0')} bind:value={group} aria-label="Filter by group">
      <option value="">Any group</option>
      {#each groups as option (option)}<option value={option}>{activityGroupLabels[option]}</option>{/each}
    </select>
    {#if usage}
      <select class={cn(selectClass, 'w-auto shrink-0')} bind:value={keySource} aria-label="Filter by key">
        <option value="">Any key</option>
        <option value="BuiltIn">{keySourceLabels.BuiltIn}</option>
        <option value="OwnKey">{keySourceLabels.OwnKey}</option>
      </select>
    {/if}
    <select class={cn(selectClass, 'w-auto shrink-0')} bind:value={outcome} aria-label="Filter by outcome">
      <option value="">Any outcome</option>
      {#each outcomes as option (option)}<option value={option}>{activityOutcomeLabels[option]}</option>{/each}
    </select>
  </div>
  <Table.Root label="Activity table">
    <Table.Header>
      <Table.Row>
        <Table.Head>When</Table.Head>
        <Table.Head>Check</Table.Head>
        <Table.Head>Group</Table.Head>
        <Table.Head>Outcome</Table.Head>
        <Table.Head>Application</Table.Head>
        {#if usage}
          <Table.Head>Model</Table.Head>
          <Table.Head>Key</Table.Head>
          <Table.Head>Provider request id</Table.Head>
          <Table.Head class="text-right">In</Table.Head>
          <Table.Head class="text-right">Out</Table.Head>
          <Table.Head class="text-right">Cost</Table.Head>
        {/if}
      </Table.Row>
    </Table.Header>
    {#key JSON.stringify(filters)}
      <ActivityRows {filters} {usage} />
    {/key}
  </Table.Root>
</Panel>
