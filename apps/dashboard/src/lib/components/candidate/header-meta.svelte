<script lang="ts">
  import BriefcaseIcon from '~icons/lucide/briefcase';
  import MailIcon from '~icons/lucide/mail';
  import PhoneIcon from '~icons/lucide/phone';
  import type { ApplicationDetail, CandidateEmailEntry } from '@screenhire/api-client';
  import MetaItem from '#lib/components/meta-item.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { emailIssueStatus } from '#lib/email-issue.js';
  import { siteName } from '#lib/format.js';

  // A candidate's meta line, under their name (design system section 11, Fact lines): the email and phone as links, the job
  // as a link, and "Applied 11 days ago via the careers page". Each reads on its own with an icon, the label for screen
  // readers only (W9). The email is the Email action too, so there are no copy buttons. A saved candidate's phone stays on
  // their profile, and the line says when they last heard from the team instead. Render it inside `HeaderBand`'s `meta`.
  let {
    detail,
    emails,
  }: {
    detail: ApplicationDetail;
    /** Every email to or from the candidate, once loaded: the newest is when they were last contacted. */
    emails: readonly CandidateEmailEntry[] | undefined;
  } = $props();

  const flagged = $derived(emailIssueStatus(detail.candidate.emailIssue));
  const saved = $derived(Boolean(detail.keptInMindAt));
  // The newest of the candidate's emails, sent or received, about any of their applications.
  const lastContacted = $derived(emails?.reduce<string | undefined>((newest, email) => (!newest || Date.parse(email.sentAt) > Date.parse(newest) ? email.sentAt : newest), undefined));
</script>

<MetaItem label="Email" icon={MailIcon}>
  <TruncatedText text={detail.candidate.email} href={`mailto:${detail.candidate.email}`} underlined class="pointer-coarse:-my-3.5 pointer-coarse:py-3.5" />
  {#if flagged}<StatusBadge {...flagged} />{/if}
</MetaItem>
{#if saved}
  {#if emails}
    <MetaItem label="Last contacted">
      {#if lastContacted}<span class="text-muted-foreground">Last contacted <RelativeTime at={lastContacted} class="inline" /></span>{:else}<span class="text-muted-foreground">Not contacted yet</span>{/if}
    </MetaItem>
  {/if}
{:else if detail.candidate.phone}
  <MetaItem label="Phone" icon={PhoneIcon}>
    <TruncatedText text={detail.candidate.phone} href={`tel:${detail.candidate.phone}`} underlined class="pointer-coarse:-my-3.5 pointer-coarse:py-3.5" />
  </MetaItem>
{/if}
<MetaItem label="Job" icon={BriefcaseIcon}>
  <TruncatedText text={detail.jobTitle} href={`/jobs/${detail.jobId}`} underlined class="pointer-coarse:-my-3.5 pointer-coarse:py-3.5" />
</MetaItem>
<MetaItem label="Applied">
  <!-- When and where from, as one phrase that reads without a label. -->
  <span class="text-muted-foreground">
    {#if detail.source === 'CareersSite'}Applied <RelativeTime at={detail.submittedAt} class="inline" /> via {detail.sourceSite
        ? siteName(detail.sourceSite)
        : 'the careers page'}{:else}Added by a recruiter <RelativeTime at={detail.submittedAt} class="inline" />{/if}
  </span>
</MetaItem>
{#if detail.inTalentPool}
  <!-- Saved stays once they leave the list, so this says they are still on it. A badge is never a link (A5). -->
  <MetaItem label="Saved candidates"><TruncatedText text="In Saved candidates" href="/candidates?list=saved" underlined /></MetaItem>
{/if}
