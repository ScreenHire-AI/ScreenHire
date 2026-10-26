<script lang="ts">
  import SendIcon from '@lucide/svelte/icons/send';
  import type { CandidateEmailEntry, TeamMember } from '@screenhire/api-client';
  import SentEmail from '#lib/components/candidate/sent-email.svelte';
  import EmailComposer from '#lib/components/email-composer.svelte';
  import ExpandableRow from '#lib/components/expandable-row.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { actorText } from '#lib/activity.js';
  import { describeError } from '#lib/query.js';

  // Every email to the candidate, about any of their applications, newest first (design system section 21), with the
  // email composer on top for people who can send. Each row opens to the email exactly as it went. Emails sent before
  // ScreenHire kept each one have only their subject. Candidates cannot reply into ScreenHire yet, so this tab says
  // nothing about replies.
  let {
    emails,
    error,
    onretry,
    members,
    currentUserId,
    recipient,
    onsent,
  }: {
    /** Newest first, as the API sends them; undefined while they load. */
    emails: readonly CandidateEmailEntry[] | undefined;
    error?: unknown;
    onretry: () => void;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    /** This application, to email from the box; only when this deployment sends email and the person may. */
    recipient?: { id: string; candidateName: string };
    onsent?: (message: string) => void;
  } = $props();

  let open = $state<string | undefined>();
  let sent = $state('');
  // The job of each email, only when the candidate has more than one application to tell apart.
  const manyJobs = $derived(new Set(emails?.map((email) => email.applicationId)).size > 1);
</script>

<!-- The tab says how many there are; the panel does not repeat it (W4). -->
<Panel title="Messages">
  <div class="flex flex-col gap-5">
    {#if recipient}
      <section aria-labelledby="messages-compose" class="flex flex-col gap-3 border-b border-border-subtle pb-5">
        <h3 id="messages-compose" class="text-heading">Email {recipient.candidateName}</h3>
        <EmailComposer
          id="messages-composer"
          label={`Email ${recipient.candidateName}`}
          recipients={[recipient]}
          inline
          onsent={(message) => {
            sent = message;
            onsent?.(message);
          }}
        />
        <p role="status" class="text-muted-foreground empty:hidden">{sent}</p>
      </section>
    {/if}

    {#if error}
      <QueryError message={describeError(error, 'Could not load the messages')} {onretry} inset />
    {:else if emails === undefined}
      <div aria-hidden="true" class="flex flex-col gap-3">
        <Skeleton class="h-10 w-full" />
        <Skeleton class="h-10 w-full" />
      </div>
    {:else if emails.length === 0}
      <Empty.Root>
        <Empty.Header>
          <Empty.Description>No emails to this candidate yet.</Empty.Description>
        </Empty.Header>
      </Empty.Root>
    {:else}
      <ol aria-label="Emails" class="@container flex flex-col divide-y divide-border-subtle">
        {#each emails as email (email.id)}
          <li class="flex flex-col gap-3 py-2 first:pt-0 last:pb-0">
            <!-- One email open at a time. -->
            <ExpandableRow id={`email-${email.id}`} bind:expanded={() => open === email.id, (next) => (open = next ? email.id : undefined)}>
              {#snippet icon()}<SendIcon />{/snippet}
              {#snippet summary()}
                <span class="font-semibold break-words">{email.subject}</span>
                <span class="flex min-w-0 flex-wrap items-center gap-x-2 text-caption text-muted-foreground">
                  <span>{actorText(email, members, currentUserId) ?? 'Sent'}</span>
                  <RelativeTime at={email.sentAt} />
                </span>
              {/snippet}
              {#snippet trailing()}
                {#if manyJobs}<Badge variant="muted" class="hidden shrink-0 @md:inline-flex">{email.jobTitle}</Badge>{/if}
              {/snippet}
              {#if manyJobs}<p class="text-muted-foreground @md:hidden">{email.jobTitle}</p>{/if}
              {#if email.kept}
                <SentEmail emailId={email.id} />
              {:else}
                <p class="text-muted-foreground">Only the subject was kept: this email went out before ScreenHire kept a copy of each one.</p>
              {/if}
            </ExpandableRow>
          </li>
        {/each}
      </ol>
    {/if}
  </div>
</Panel>
