<script lang="ts">
  import SendIcon from '@lucide/svelte/icons/send';
  import type { EmailPreview as Preview, TalentPoolMember } from '@screenhire/api-client';
  import { createInviteToApply, createListJobs, createPreviewTalentPoolInvitation } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { untrack } from 'svelte';
  import EmailPreview from '#lib/components/email-preview.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { talentPoolPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';

  // Invite to apply (docs/architecture/talent-pool.md), in a sheet (design system section 9): pick an open job, see the
  // email as the first person receives it, and send. The email is the organization's "Invitation to apply" template as
  // it stands (edited on Templates): it has the job's careers link and the candidate's own link to stop being kept, which
  // the worker makes as it sends. A job someone already applied to skips them.
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
  const openJobs = $derived((jobs.data?.data ?? []).filter((job) => job.status === 'Open'));
  const preview = createPreviewTalentPoolInvitation();
  const invite = createInviteToApply(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, talentPoolPath) } }));
  let jobId = $state('');
  let failure = $state('');
  let rendered = $state<Preview | undefined>(undefined);
  let previewFailure = $state('');

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
    const choices = openJobs;
    if (!open || choices.some((job) => job.id === untrack(() => jobId))) return;
    untrack(() => (jobId = choices.find((job) => !people.every((person) => person.appliedJobIds.includes(job.id)))?.id ?? ''));
  });

  // The preview follows the chosen job, for the first person in the selection. Only those are its dependencies: the
  // mutation's own state changes as it runs, and reading it here would start the preview again on every answer.
  $effect(() => {
    const first = people[0];
    const job = jobId;
    if (!open || !job || !first) return;
    untrack(() => {
      rendered = undefined;
      previewFailure = '';
      preview.mutateAsync({ data: { candidateId: first.candidateId, jobId: job } }).then(
        (response) => (rendered = response.data),
        (error: unknown) => (previewFailure = describeError(error, 'Could not show the email')),
      );
    });
  });

  const title = $derived(people.length === 1 ? `Invite ${people[0]!.candidateName} to apply` : `Invite ${people.length} to apply`);

  async function onsubmit(event: SubmitEvent) {
    event.preventDefault();
    failure = '';
    if (!jobId) {
      failure = 'Choose a job.';
      return;
    }
    try {
      const response = await invite.mutateAsync({ data: { candidateIds: people.map((person) => person.candidateId), jobId } });
      const { queued, skipped } = response.data;
      ondone?.(`Sending ${queued === 1 ? '1 invitation' : `${queued} invitations`}.${skipped > 0 ? ` ${skipped} skipped: already applied or no longer in the pool.` : ''}`);
      open = false;
    } catch (error) {
      failure = describeError(error, 'Could not send the invitations');
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto sm:max-w-xl">
    <Sheet.Header>
      <Sheet.Title>{title}</Sheet.Title>
      <Sheet.Description>An email with the job's link. It says how to stop being kept.</Sheet.Description>
    </Sheet.Header>
    <form novalidate class="flex flex-col gap-4 px-4 pb-4" {onsubmit}>
      {#if openJobs.length === 0}
        <p class="text-muted-foreground">No job is open. Post one first, then invite people to it.</p>
      {:else}
        <Field.Field>
          <Field.Label for="invite-job">Job</Field.Label>
          <select id="invite-job" class={selectClass} bind:value={jobId}>
            {#each openJobs as job (job.id)}<option value={job.id}>{job.title}</option>{/each}
          </select>
        </Field.Field>
        <EmailPreview preview={rendered} failure={previewFailure} />
      {/if}
      <FailureText message={failure} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        {#if openJobs.length > 0}
          <Button type="submit" size="lg" disabled={invite.isPending}>
            <SendIcon aria-hidden="true" />{people.length === 1 ? 'Send invitation' : `Send ${people.length} invitations`}
          </Button>
        {/if}
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
