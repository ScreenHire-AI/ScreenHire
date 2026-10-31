<script lang="ts">
  import CircleXIcon from '@lucide/svelte/icons/circle-x';
  import { createCloseJob, createKeepInMind, createListJobApplications } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { page } from '$app/state';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import { can } from '#lib/permissions.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath, candidatesPath, jobsPath, talentPoolPath } from '#lib/realtime.js';
  import { canKeepInMind, keepBatchSize } from '#lib/talent-pool.js';
  import { cn } from '#lib/utils.js';

  // Confirms "Close & remove everywhere", the destructive menu item on the jobs list and the job page (design system A2).
  // A closed job is never reopened, so this is the one place that says what closing does to the boards. While the job
  // still has candidates who allowed it, a choice goes with the close (section 11, Confirm dialog): also keep them in
  // mind, so they leave the job without a rejection and join the talent pool (docs/architecture/talent-pool.md). It
  // never reads Verification output (ADR-0004); keeping only some of them is the job's selection bar, before closing.
  let { jobId, open = $bindable(false) }: { jobId: string | null; open?: boolean } = $props();

  const queryClient = useQueryClient();
  const close = createCloseJob(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));
  const keep = createKeepInMind(() => ({
    mutation: {
      onSettled: () => Promise.all([talentPoolPath, applicationsPath, candidatesPath].map((path) => invalidatePath(queryClient, path))),
    },
  }));
  const mayKeep = $derived(can(page.data.user, 'MoveCandidates'));
  const applications = createListJobApplications(
    () => jobId ?? '',
    () => ({ query: { enabled: open && Boolean(jobId) && mayKeep } }),
  );
  const keepable = $derived((applications.data?.data ?? []).filter((application) => canKeepInMind(application)));
  let alsoKeep = $state(false);
  let failure = $state('');

  $effect(() => {
    if (open) {
      failure = '';
      alsoKeep = false;
    }
  });

  async function confirm() {
    if (!jobId) return;
    failure = '';
    try {
      // In batches, so a job with more candidates than one request takes still closes. A batch that fails stops the
      // close; the ones kept stay kept, and closing again keeps the rest.
      const ids = alsoKeep ? keepable.map((application) => application.id) : [];
      for (let start = 0; start < ids.length; start += keepBatchSize) {
        await keep.mutateAsync({ data: { applicationIds: ids.slice(start, start + keepBatchSize) } });
      }
      await close.mutateAsync({ jobId });
      open = false;
    } catch (error) {
      failure = describeError(error, 'Could not close the job');
    }
  }
</script>

<Dialog.Root bind:open>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Close this job and remove it everywhere?</Dialog.Title>
      <Dialog.Description>The careers page stops taking applications and every board is asked to remove the job, for good.</Dialog.Description>
    </Dialog.Header>
    {#if mayKeep && keepable.length > 0}
      <label class="flex items-center gap-2 pointer-coarse:min-h-11">
        <input type="checkbox" bind:checked={alsoKeep} class="size-4 shrink-0 accent-primary" />
        Also keep in mind the {keepable.length === 1 ? 'candidate' : `${keepable.length} candidates`} who allowed it
      </label>
    {/if}
    <FailureText message={failure} />
    <Dialog.Footer>
      <Dialog.Close class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Keep it open</Dialog.Close>
      <Button variant="destructive" size="lg" disabled={close.isPending || keep.isPending} onclick={confirm}
        ><CircleXIcon aria-hidden="true" />Close &amp; remove everywhere</Button
      >
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
