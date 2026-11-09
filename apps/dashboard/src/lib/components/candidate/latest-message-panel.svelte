<script lang="ts" module>
  // A line that is only a greeting ("Hi Taylor,", "Dear Ms Quinn"): it says who the email is to, not what it says.
  const greeting = /^(hi|hello|hey|dear|good (morning|afternoon|evening))\b[^.!?:;]{0,40}[,!]?$/i;

  /** The first line of an email's text that says something, for the Latest message panel: not a lone greeting. */
  export function firstLine(text: string | null | undefined): string {
    const lines = (text ?? '').split('\n').map((line) => line.trim()).filter((line) => line.length > 0);
    return lines.find((line) => !greeting.test(line)) ?? lines[0] ?? '';
  }
</script>

<script lang="ts">
  import { createGetReceivedEmail, createGetSentEmail } from '@screenhire/api-client/svelte-query';
  import type { CandidateEmailEntry, TeamMember } from '@screenhire/api-client';
  import Panel from '#lib/components/panel.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import { personName } from '#lib/team.js';

  // The Overview's Latest message (design system section 21): the newest email about this application, either way, as who
  // sent it, how long ago and the first line of what it said. The Messages tab has the rest, so the panel is just a link
  // to it (W7). Nothing is drawn until there is one. The list carries no body, so the newest one's is read once.
  let {
    applicationId,
    emails,
    members,
    currentUserId,
  }: {
    applicationId: string;
    /** Every email to or from the candidate, newest first; undefined while they load. */
    emails: readonly CandidateEmailEntry[] | undefined;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
  } = $props();

  const latest = $derived(emails?.find((email) => email.applicationId === applicationId));
  const received = $derived(latest?.direction === 'Received');
  const reply = createGetReceivedEmail(
    () => latest?.id ?? '',
    () => ({ query: { enabled: Boolean(latest) && received, staleTime: Infinity } }),
  );
  const sent = createGetSentEmail(
    () => latest?.id ?? '',
    () => ({ query: { enabled: Boolean(latest) && !received, staleTime: Infinity } }),
  );

  const sender = $derived(
    !latest
      ? ''
      : received
        ? (reply.data?.data.fromName ?? latest.fromAddress ?? 'The candidate')
        : latest.actorUserId
          ? personName(latest.actorUserId, members, currentUserId)
          : 'The team',
  );
  const line = $derived(firstLine(received ? reply.data?.data.text : sent.data?.data.text));
</script>

{#if latest}
  <Panel title="Latest message">
    {#snippet actions()}
      <a href="?tab=messages" class="inline-flex min-h-6 items-center font-semibold text-primary underline underline-offset-2 pointer-coarse:min-h-11">All messages</a>
    {/snippet}
    <div class="flex min-w-0 flex-col gap-1.5">
      <p class="min-w-0 break-words">
        <span class="font-semibold">{sender === 'you' ? 'You' : sender}</span>
        {received ? 'replied' : 'emailed'}
        <RelativeTime at={latest.sentAt} class="inline text-muted-foreground" />
      </p>
      {#if line}<p class="max-w-measure line-clamp-2 break-words text-muted-foreground">{line}</p>{/if}
    </div>
  </Panel>
{/if}
