<script lang="ts">
  import { createCloseJob } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';

  // Confirms "Close & remove everywhere", the destructive menu item on the jobs list and the job page (design system A2).
  // A closed job is never reopened, so this is the one place that says what closing does to the boards.
  let { jobId, open = $bindable(false) }: { jobId: string | null; open?: boolean } = $props();

  const queryClient = useQueryClient();
  const close = createCloseJob(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));
  let failure = $state('');

  $effect(() => {
    if (open) failure = '';
  });

  async function confirm() {
    if (!jobId) return;
    failure = '';
    try {
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
      <Dialog.Description>
        The careers page stops taking applications and every board is asked to remove the job. This cannot be undone.
      </Dialog.Description>
    </Dialog.Header>
    <FailureText message={failure} />
    <Dialog.Footer>
      <Dialog.Close class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Keep it open</Dialog.Close>
      <Button variant="destructive" size="lg" disabled={close.isPending} onclick={confirm}>Close &amp; remove everywhere</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
