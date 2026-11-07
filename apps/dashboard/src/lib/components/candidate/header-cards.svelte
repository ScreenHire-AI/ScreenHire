<script lang="ts">
  import MailIcon from '~icons/lucide/mail';
  import PhoneIcon from '~icons/lucide/phone';
  import type { ApplicationDetail, ApplicationInterview, CandidateEmailEntry, ScorecardEntry, TeamMember } from '@screenhire/api-client';
  import { createGetCandidate } from '@screenhire/api-client/svelte-query';
  import CopyButton from '#lib/components/copy-button.svelte';
  import MiniCard from '#lib/components/mini-card.svelte';
  import Rating from '#lib/components/rating.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { formatDateTime } from '#lib/dates.js';
  import { emailIssueStatus } from '#lib/email-issue.js';
  import { siteName } from '#lib/format.js';

  // The three mini cards under the pipeline tracker in a candidate's header (design system section 11, Mini card): how to
  // reach them, the application, and then either their rating once someone scored them or their other applications. The
  // third card never says "Not rated yet": a rating that does not exist is not shown, and it links to the candidate's
  // profile either way, because nothing else on the page does.
  let {
    detail,
    emails,
    scorecards,
    stageName,
    members,
    currentUserId,
    interview,
  }: {
    detail: ApplicationDetail;
    /** Every email to or from the candidate, once loaded: the newest is when they were last contacted. */
    emails: readonly CandidateEmailEntry[] | undefined;
    scorecards?: readonly ScorecardEntry[];
    stageName?: (stageId: string) => string;
    members?: readonly TeamMember[];
    currentUserId?: string;
    /** The booked interview, if there is one: the one fact about it the header shows, in the reader's own zone. */
    interview?: ApplicationInterview;
  } = $props();

  const candidate = createGetCandidate(() => detail.candidate.id);
  const others = $derived(candidate.data ? candidate.data.data.applications.filter((application) => application.id !== detail.id).length : undefined);
  const othersText = $derived(others === undefined ? '' : others === 0 ? 'No other jobs' : others === 1 ? '1 other job' : `${others} other jobs`);

  const flagged = $derived(emailIssueStatus(detail.candidate.emailIssue));
  const saved = $derived(Boolean(detail.keptInMindAt));
  // The newest of the candidate's emails, sent or received, about any of their applications.
  const lastContacted = $derived(emails?.reduce<string | undefined>((newest, email) => (!newest || Date.parse(email.sentAt) > Date.parse(newest) ? email.sentAt : newest), undefined));
  // On a phone each card is a fixed 16rem that snaps into the row; from 36rem the grid sizes them (see the row below).
  const cardClass = 'w-64 shrink-0 snap-start @xl:w-auto';
  const rated = $derived(detail.rating != null && detail.rating.state !== 'NotRated');
</script>

{#snippet profileLink()}
  <!-- The link is there from the first paint and keeps its words. -->
  <a href={`/candidates/${detail.candidate.id}`} class="inline-flex min-h-6 w-fit items-center underline underline-offset-2 pointer-coarse:min-h-11">Candidate profile</a>
{/snippet}

<!-- As many columns as hold a card of 16rem or more, so a card keeps its two lines: three across in a wide band, two at a
     laptop (F2). In a band narrower than 36rem, a phone's, the cards would stack to about 580px before the tabs, so they sit
     in one row that scrolls sideways, each snapping into place, and the next card's edge shows that there is more. The
     padding and the matching negative margin keep the links' focus rings inside the scrolling row. -->
<div
  class="-m-1 flex min-w-0 snap-x snap-mandatory gap-3 overflow-x-auto p-1 @xl:m-0 @xl:grid @xl:snap-none @xl:grid-cols-[repeat(auto-fit,minmax(16rem,1fr))] @xl:overflow-visible @xl:p-0"
  data-header-cards
>
  <MiniCard class={cardClass} title="Contact">
    <div class="flex min-w-0 items-center gap-1">
      <TruncatedText
        text={detail.candidate.email}
        href={`mailto:${detail.candidate.email}`}
        icon={MailIcon}
        underlined
        class="pointer-coarse:-my-3.5 pointer-coarse:py-3.5"
      />
      <CopyButton value={detail.candidate.email} label="Copy email address" />
      {#if flagged}<StatusBadge {...flagged} />{/if}
    </div>
    <!-- A saved candidate's second line is when they last heard from the team; the phone stays on their profile. -->
    {#if saved}
      <p class="min-h-6 text-muted-foreground">
        {#if emails}{#if lastContacted}Last contacted <RelativeTime at={lastContacted} class="inline" />{:else}Not contacted yet{/if}{/if}
      </p>
    {:else if detail.candidate.phone}
      <div class="flex min-w-0 items-center gap-1">
        <TruncatedText
          text={detail.candidate.phone}
          href={`tel:${detail.candidate.phone}`}
          icon={PhoneIcon}
          underlined
          class="pointer-coarse:-my-3.5 pointer-coarse:py-3.5"
        />
        <CopyButton value={detail.candidate.phone} label="Copy phone number" />
      </div>
    {/if}
  </MiniCard>

  <MiniCard class={cardClass} title="Application">
    <TruncatedText text={detail.jobTitle} href={`/jobs/${detail.jobId}`} underlined />
    <!-- When and where from, as one phrase that reads without a label. -->
    <p class="text-muted-foreground">
      {#if detail.source === 'CareersSite'}Applied <RelativeTime at={detail.submittedAt} class="inline" /> via {detail.sourceSite
          ? siteName(detail.sourceSite)
          : 'the careers page'}{:else}Added by a recruiter <RelativeTime at={detail.submittedAt} class="inline" />{/if}
    </p>
  </MiniCard>

  {#if rated}
    <MiniCard class={cardClass} title="Rating">
      <!-- From people's scorecards only (ADR-0013); it opens to each scorecard. -->
      <div class="flex min-w-0"><Rating rating={detail.rating} {scorecards} {stageName} {members} {currentUserId} /></div>
      {@render profileLink()}
    </MiniCard>
  {:else}
    <MiniCard class={cardClass} title="Also applied to">
      <p class="min-h-6 text-muted-foreground">{othersText}</p>
      {@render profileLink()}
    </MiniCard>
  {/if}

  {#if interview?.startsAt}
    <!-- A booked interview is a fact here and not said again in the Interview panel (W7). -->
    <MiniCard class={cardClass} title="Interview">
      <p class="min-h-6"><time datetime={interview.startsAt}>{formatDateTime(interview.startsAt)}</time></p>
      <p class="min-h-6 text-muted-foreground">{interview.durationMinutes} minutes with {interview.interviewers.length === 1 ? '1 interviewer' : `${interview.interviewers.length} interviewers`}</p>
    </MiniCard>
  {/if}
</div>
