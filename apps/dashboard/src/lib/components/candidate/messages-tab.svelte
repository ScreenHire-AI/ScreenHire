<script lang="ts">
  import MailIcon from '@lucide/svelte/icons/mail';
  import type { ApplicationTimelineEntry, TeamMember } from '@screenhire/api-client';
  import ActivityList, { type Activity } from '#lib/components/activity-list.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { actorText, eventIcons } from '#lib/activity.js';
  import { describeError } from '#lib/query.js';

  // The emails the candidate has been sent, newest first (design system section 22): the subject they received, who
  // sent it and when. The body is not kept yet; the email task adds it, the composer and the reply box here.
  let {
    entries,
    error,
    onretry,
    members,
    currentUserId,
    onemail,
  }: {
    /** The timeline, oldest first as the API sends it; undefined while it loads. */
    entries: readonly ApplicationTimelineEntry[] | undefined;
    error?: unknown;
    onretry: () => void;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    /** Opens the email sheet; only when this deployment sends email and the person may. */
    onemail?: () => void;
  } = $props();

  const items = $derived<Activity[] | undefined>(
    entries
      ?.filter((entry) => entry.type === 'EmailSent')
      .toReversed()
      .map((entry) => ({
        id: entry.id,
        icon: eventIcons.EmailSent,
        event: entry.emailSubject ?? 'An update',
        actor: actorText(entry, members, currentUserId),
        at: entry.occurredAt,
      })),
  );
</script>

<Panel title="Messages" count={items?.length}>
  {#if error}
    <QueryError message={describeError(error, 'Could not load the messages')} {onretry} inset />
  {:else if items && items.length === 0}
    <Empty.Root>
      <Empty.Header>
        <Empty.Description>No emails to this candidate yet.</Empty.Description>
      </Empty.Header>
      {#if onemail}
        <Empty.Content><Button variant="outline" size="lg" onclick={onemail}><MailIcon aria-hidden="true" />Email</Button></Empty.Content>
      {/if}
    </Empty.Root>
  {:else}
    <ActivityList label="Emails" {items} />
  {/if}
</Panel>
