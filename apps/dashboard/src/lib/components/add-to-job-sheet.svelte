<script lang="ts">
  import UserPlusIcon from '~icons/lucide/user-plus';
  import type { TalentPoolMember } from '@screenhire/api-client';
  import { createAddFromTalentPool, createListJobs } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { untrack } from 'svelte';
  import { goto } from '$app/navigation';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath, candidatesPath, jobsPath, talentPoolPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';

  // Add to a job (docs/architecture/talent-pool.md), in a sheet: each person gets an application on the job, in its first
  // stage, and leaves the talent pool. The new application keeps the permission they gave, and the candidate's page
  // links it to the one they were kept from. One person goes straight to their new application.
  let {
    open = $bindable(false),
    people,
    ondone,
  }: {
    open?: boolean;
    people: readonly TalentPoolMember[];
    ondone?: (message: string) => void;
  } = $props();

  const queryClient = useQueryClient();
  const jobs = createListJobs();
  const targets = $derived((jobs.data?.data ?? []).filter((job) => job.status !== 'Closed'));
  const add = createAddFromTalentPool(() => ({
    mutation: {
      onSettled: () => Promise.all([talentPoolPath, jobsPath, applicationsPath, candidatesPath].map((path) => invalidatePath(queryClient, path))),
    },
  }));
  let jobId = $state('');
  let failure = $state('');

  // Starts fresh each time it opens.
  $effect(() => {
    if (!open) return;
    untrack(() => {
      jobId = '';
      failure = '';
    });
  });

  // The first job someone in the selection has not applied to, once the jobs have loaded, unless one is chosen.
  $effect(() => {
    const choices = targets;
    if (!open || choices.some((job) => job.id === untrack(() => jobId))) return;
    untrack(() => (jobId = choices.find((job) => !people.every((person) => person.appliedJobIds.includes(job.id)))?.id ?? ''));
  });

  const title = $derived(people.length === 1 ? `Add ${people[0]!.candidateName} to a job` : `Add ${people.length} to a job`);

  async function onsubmit(event: SubmitEvent) {
    event.preventDefault();
    failure = '';
    if (!jobId) {
      failure = 'Choose a job.';
      return;
    }
    try {
      const response = await add.mutateAsync({ data: { candidateIds: people.map((person) => person.candidateId), jobId } });
      const { applicationIds, skipped } = response.data;
      open = false;
      if (people.length === 1 && applicationIds.length === 1) {
        await goto(`/applications/${applicationIds[0]}`);
        return;
      }
      ondone?.(`Added ${applicationIds.length}.${skipped > 0 ? ` ${skipped} skipped: already applied or no longer in the pool.` : ''}`);
    } catch (error) {
      failure = describeError(error, 'Could not add them to the job');
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>{title}</Sheet.Title>
      <Sheet.Description>They start in the job's first stage and leave the talent pool.</Sheet.Description>
    </Sheet.Header>
    <form novalidate class="flex flex-col gap-4 px-4 pb-4" {onsubmit}>
      {#if targets.length === 0}
        <p class="text-muted-foreground">Every job is closed. Create one from <a href="/" class="inline-flex min-h-6 items-center underline underline-offset-2 pointer-coarse:min-h-11">Jobs</a> first.</p>
      {:else}
        <Field.Field>
          <Field.Label for="add-job">Job</Field.Label>
          <select id="add-job" class={selectClass} bind:value={jobId}>
            {#each targets as job (job.id)}<option value={job.id}>{job.title}</option>{/each}
          </select>
        </Field.Field>
      {/if}
      <FailureText message={failure} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        {#if targets.length > 0}
          <Button type="submit" size="lg" disabled={add.isPending}><UserPlusIcon aria-hidden="true" />Add to job</Button>
        {/if}
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
