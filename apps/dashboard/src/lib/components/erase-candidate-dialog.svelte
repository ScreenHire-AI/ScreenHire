<script lang="ts">
  import Trash2Icon from '~icons/lucide/trash-2';
  import { createEraseCandidate } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import { invalidatePath } from '#lib/query.js';
  import { applicationsPath, candidatesPath, jobsPath, talentPoolPath } from '#lib/realtime.js';

  // Erases a candidate (design system A2): the one place that says what erasing takes and what it keeps. It follows a
  // candidate's own request to be removed from Saved candidates, or a privacy request that reached the team, so the
  // person types the name to be sure. Needs Erase candidates and, on the server, access to every job.
  let {
    candidateId,
    candidateName,
    open = $bindable(false),
    ondone,
  }: {
    candidateId: string;
    candidateName: string;
    open?: boolean;
    /** Runs once the candidate is gone. */
    ondone: () => void;
  } = $props();

  const queryClient = useQueryClient();
  const erase = createEraseCandidate();

  async function confirm() {
    // The dialog offers the button only once the typed name matches, and the server checks it again.
    await erase.mutateAsync({ candidateId, data: { confirmation: candidateName } });
    await Promise.all([candidatesPath, applicationsPath, jobsPath, talentPoolPath].map((path) => invalidatePath(queryClient, path)));
    ondone();
  }
</script>

<ConfirmDialog
  bind:open
  title={`Erase ${candidateName}?`}
  description={`This erases ${candidateName} and all of their applications, files, answers, notes and verification data, for good. ScreenHire keeps only a line in the team log that someone was erased, with no details.`}
  confirmLabel="Erase candidate"
  failure="Could not erase the candidate"
  confirmText={candidateName}
  onconfirm={confirm}
>
  {#snippet icon()}<Trash2Icon aria-hidden="true" />{/snippet}
</ConfirmDialog>
