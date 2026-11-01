<script lang="ts">
  import CircleSlashIcon from '~icons/lucide/circle-slash';
  import ClipboardPenIcon from '~icons/lucide/clipboard-pen';
  import MessageSquareCheckIcon from '~icons/lucide/message-square-check';
  import Undo2Icon from '~icons/lucide/undo-2';
  import type { VerificationReviewEntry } from '@screenhire/api-client';
  import { createGetApplicationVerification } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { page } from '$app/state';
  import ActivityList, { type Activity } from '#lib/components/activity-list.svelte';
  import VerificationNoteDialog from '#lib/components/verification-note-dialog.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { dismissReasonLabels, explainReasonLabels, refetchAfterReview, reviewerName } from '#lib/verification.js';
  import { can } from '#lib/permissions.js';
  import { describeError } from '#lib/query.js';

  // The people's side of an application's verification: every dismissal, explained reason, undo and note, newest first,
  // with who and when. It reads the verification panel's query, so the two share one request and update together.
  let { applicationId }: { applicationId: string } = $props();

  const queryClient = useQueryClient();
  const verification = createGetApplicationVerification(() => applicationId);
  const entries = $derived(verification.data?.data.reviewLog);

  let noteOpen = $state(false);
  let announcement = $state('');

  async function done(message: string) {
    announcement = message;
    await refetchAfterReview(queryClient);
  }

  function describeEntry(entry: VerificationReviewEntry): string {
    const about = entry.assessmentKind === 'Authenticity' ? 'authenticity' : entry.assessmentKind === 'AiAssistance' ? 'AI assistance' : null;
    const reason = entry.signalTitle ?? 'a reason';
    if (entry.type === 'Dismissed') return `Dismissed ${about ?? 'an assessment'}: ${entry.dismissReason ? dismissReasonLabels[entry.dismissReason] : 'no reason'}`;
    if (entry.type === 'ReasonExplained') return `Explained ${reason}: ${entry.explainReason ? explainReasonLabels[entry.explainReason] : 'no reason'}`;
    if (entry.type === 'ExplanationUndone') return `Undid the explanation of ${reason}`;
    return about ? `Note on ${about}` : 'Note';
  }

  const icons: Record<VerificationReviewEntry['type'], typeof ClipboardPenIcon> = {
    Dismissed: CircleSlashIcon,
    NoteAdded: ClipboardPenIcon,
    ReasonExplained: MessageSquareCheckIcon,
    ExplanationUndone: Undo2Icon,
  };

  const items = $derived<Activity[]>(
    (entries ?? []).toReversed().map((entry) => ({
      id: entry.id,
      icon: icons[entry.type],
      event: describeEntry(entry),
      actor: `by ${reviewerName(entry.actorUserId, entry.actorName, page.data.user?.id)}`,
      note: entry.note ?? undefined,
      at: entry.occurredAt,
    })),
  );
</script>

<Panel title="Review log" count={entries?.length}>
  {#snippet actions()}
    {#if entries && can(page.data.user, 'ReviewVerification')}<Button variant="outline" onclick={() => (noteOpen = true)}><ClipboardPenIcon aria-hidden="true" />Add review note</Button>{/if}
  {/snippet}
  <div class="flex flex-col gap-3">
    {#if verification.isError && !entries}
      <QueryError message={describeError(verification.error, 'Could not load the review log')} onretry={() => verification.refetch()} inset />
    {:else if entries && items.length === 0}
      <p class="text-muted-foreground">No review actions yet.</p>
    {:else}
      <ActivityList label="Review actions" items={entries && items} />
    {/if}
    <p role="status" class="text-muted-foreground empty:hidden">{announcement}</p>
  </div>
</Panel>

<VerificationNoteDialog bind:open={noteOpen} {applicationId} ondone={done} />
