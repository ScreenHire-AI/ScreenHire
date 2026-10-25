<script lang="ts">
  import type { TeamMember } from '@screenhire/api-client';
  import { createListMemberJobs } from '@screenhire/api-client/svelte-query';
  import QueryError from '#lib/components/query-error.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';

  // The jobs a member sees (ADR-0015), as a count (T4): "All jobs" in words, or "2 jobs" as a button whose sheet lists
  // the jobs whose hiring team they are on, each a link. They change through each job's own Hiring team.
  let { member }: { member: TeamMember } = $props();

  let open = $state(false);
  const jobs = createListMemberJobs(() => member.id, () => ({ query: { enabled: open } }));
  const label = $derived(member.jobCount === 1 ? '1 job' : `${member.jobCount} jobs`);
</script>

{#if member.seesAllJobs}
  <span>All jobs</span>
{:else if member.jobCount === 0}
  <span>{label}</span>
{:else}
  <Button variant="outline" class="tabular-nums" aria-haspopup="dialog" aria-label={`${label}: show the jobs`} onclick={() => (open = true)}>{label}</Button>
  <Sheet.Root bind:open>
    <Sheet.Content side="right" class="w-full gap-0 overflow-y-auto data-[side=right]:sm:max-w-md">
      <Sheet.Header class="border-b border-border-subtle">
        <Sheet.Title>{member.displayName}'s jobs</Sheet.Title>
        <Sheet.Description>The hiring teams they are on. Each job's Hiring team changes them.</Sheet.Description>
      </Sheet.Header>
      {#if jobs.isError}
        <div class="p-4"><QueryError message={describeError(jobs.error, 'Could not load their jobs')} onretry={() => jobs.refetch()} inset /></div>
      {:else if jobs.isPending}
        <div aria-hidden="true" class="p-4"><Skeleton class="h-4 w-48" /></div>
      {:else}
        <ul>
          {#each jobs.data.data as job (job.id)}
            <li class="border-b border-border-subtle last:border-0">
              <a href={`/jobs/${job.id}`} class="flex min-h-11 items-center px-4 underline-offset-2 hover:underline"><TruncatedText text={job.title} /></a>
            </li>
          {/each}
        </ul>
      {/if}
    </Sheet.Content>
  </Sheet.Root>
{/if}
