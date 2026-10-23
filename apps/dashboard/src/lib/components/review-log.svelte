<script lang="ts">
  import CircleSlashIcon from '@lucide/svelte/icons/circle-slash';
  import ClipboardPenIcon from '@lucide/svelte/icons/clipboard-pen';
  import type { IntegrityReviewEntry } from '@screenhire/api-client';
  import { createGetApplicationIntegrity } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { page } from '$app/state';
  import ActivityList, { type Activity } from '#lib/components/activity-list.svelte';
  import IntegrityNoteDialog from '#lib/components/integrity-note-dialog.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { dismissReasonLabels, refetchAfterReview, reviewerName } from '#lib/integrity.js';
  import { describeError } from '#lib/query.js';

  // The people's side of an application's integrity: every dismissal and note, newest first, with who and when. It
  // reads the integrity panel's query, so the two share one request and update together.
  let { applicationId }: { applicationId: string } = $props();

  const queryClient = useQueryClient();
  const integrity = createGetApplicationIntegrity(() => applicationId);
  const entries = $derived(integrity.data?.data.reviewLog);

  let noteOpen = $state(false);
  let announcement = $state('');

  async function done(message: string) {
    announcement = message;
    await refetchAfterReview(queryClient);
  }

  function describeEntry(entry: IntegrityReviewEntry): string {
    const about = entry.assessmentKind === 'Authenticity' ? 'authenticity' : entry.assessmentKind === 'AiAssistance' ? 'AI assistance' : null;
    if (entry.type === 'Dismissed') return `Dismissed ${about ?? 'an assessment'}: ${entry.dismissReason ? dismissReasonLabels[entry.dismissReason] : 'no reason'}`;
    return about ? `Note on ${about}` : 'Note';
  }

  const items = $derived<Activity[]>(
    (entries ?? []).toReversed().map((entry) => ({
      id: entry.id,
      icon: entry.type === 'Dismissed' ? CircleSlashIcon : ClipboardPenIcon,
      event: describeEntry(entry),
      actor: `by ${reviewerName(entry.actorUserId, entry.actorName, page.data.user?.id)}`,
      note: entry.note ?? undefined,
      at: entry.occurredAt,
    })),
  );
</script>

<Panel title="Review log">
  {#snippet actions()}
    {#if entries}<Button variant="outline" onclick={() => (noteOpen = true)}><ClipboardPenIcon aria-hidden="true" />Add review note</Button>{/if}
  {/snippet}
  <div class="flex flex-col gap-3">
    {#if integrity.isError && !entries}
      <QueryError message={describeError(integrity.error, 'Could not load the review log')} onretry={() => integrity.refetch()} inset />
    {:else if entries && items.length === 0}
      <p class="text-muted-foreground">No review actions yet.</p>
    {:else}
      <ActivityList label="Review actions" items={entries && items} />
    {/if}
    <p role="status" class="text-muted-foreground empty:hidden">{announcement}</p>
  </div>
</Panel>

<IntegrityNoteDialog bind:open={noteOpen} {applicationId} ondone={done} />
