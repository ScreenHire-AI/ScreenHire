<script lang="ts">
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import CalendarIcon from '~icons/lucide/calendar';
  import MailIcon from '~icons/lucide/mail';
  import PhoneIcon from '~icons/lucide/phone';
  import Trash2Icon from '~icons/lucide/trash-2';
  import {
    ApiError,
    createGetCandidate,
    createGetCandidateHistory,
    createListJobs,
    createListTeamMembers,
  } from '@screenhire/api-client/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActivityList from '#lib/components/activity-list.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import EraseCandidateDialog from '#lib/components/erase-candidate-dialog.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import MetaItem from '#lib/components/meta-item.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Notice from '#lib/components/notice.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { askedToBeRemovedAt, historyItems, inTimeOrder, progressOf } from '#lib/candidate-profile.js';
  import { formatDate } from '#lib/dates.js';
  import { can, seesAllJobs } from '#lib/permissions.js';
  import { describeError } from '#lib/query.js';
  import type { PageProps } from './$types';

  let { params }: PageProps = $props();

  // A candidate's profile (design system section 7, a detail page): the header band holds who they are, then every
  // application they made in the order they made it and their history across all of them. A person who applies to
  // several jobs, perhaps a year apart, is one candidate (CLAUDE.md: matched by email within the organization). The API
  // lists only the jobs the reader sees (ADR-0015). Erasing is here, in the More menu, for people who may.
  const candidate = createGetCandidate(() => params.candidateId);
  const history = createGetCandidateHistory(() => params.candidateId);
  const team = createListTeamMembers();
  const jobs = createListJobs();
  const detail = $derived(candidate.data?.data);
  const applications = $derived(detail ? inTimeOrder(detail.applications) : []);
  const currentUserId = $derived(page.data.user?.id);
  const jobName = (id: string | null | undefined) => jobs.data?.data.find((job) => job.id === id)?.title ?? 'another job';
  const items = $derived(history.data ? historyItems(history.data.data, team.data?.data, currentUserId, jobName) : undefined);
  const asked = $derived(askedToBeRemovedAt(history.data?.data));
  // The API needs the permission and a person who sees every job, because erasing reaches every application.
  const mayErase = $derived(can(page.data.user, 'EraseCandidates') && seesAllJobs(page.data.user));
  const notFound = $derived(candidate.error instanceof ApiError && candidate.error.status === 404);

  let eraseOpen = $state(false);
  // The dialog opens once the More menu has closed, so focus can go back to the menu's button.
  let eraseChosen = false;
</script>

<svelte:head>
  <title>{detail?.fullName ?? 'Candidate'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Candidates', href: '/candidates' }, { label: detail?.fullName ?? 'Candidate' }]} back />

<main class="flex flex-col">
  {#if candidate.isError}
    {#if notFound}
      <HeaderBand title="Candidate not found" />
      <div class="@container w-full px-4 py-5 md:px-6">
        <Panel title="Details">
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>They do not exist, or they belong to another organization.</Empty.Description>
            </Empty.Header>
            <Empty.Content><Button href="/candidates" variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to candidates</Button></Empty.Content>
          </Empty.Root>
        </Panel>
      </div>
    {:else}
      <HeaderBand title="Candidate" />
      <div class="@container w-full px-4 py-5 md:px-6">
        <Panel title="Could not load the candidate"><QueryError message={describeError(candidate.error, 'Could not load this candidate')} onretry={() => candidate.refetch()} inset /></Panel>
      </div>
    {/if}
  {:else if !detail}
    <div aria-hidden="true" class="@container border-b border-border bg-card">
      <div class="flex w-full flex-col gap-4 px-4 py-5 md:px-6">
        <Skeleton class="h-8 w-64 max-w-full" />
        <Skeleton class="h-5 w-96 max-w-full" />
      </div>
    </div>
    <div aria-hidden="true" class="@container grid w-full items-start gap-5 px-4 py-5 md:px-6 @5xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
      <Skeleton class="h-64 rounded-panel" />
      <Skeleton class="h-64 rounded-panel" />
    </div>
    <p class="sr-only" role="status">Loading the candidate…</p>
  {:else}
    <HeaderBand title={detail.fullName}>
      {#snippet details()}
        <TruncatedText text={detail.email} href={`mailto:${detail.email}`} icon={MailIcon} underlined class="pointer-coarse:-my-3.5 pointer-coarse:py-3.5" />
        {#if detail.phone}
          <TruncatedText text={detail.phone} href={`tel:${detail.phone}`} icon={PhoneIcon} underlined class="pointer-coarse:-my-3.5 pointer-coarse:py-3.5" />
        {/if}
      {/snippet}
      {#snippet actions()}
        {#if mayErase}
          <MoreMenu label="More actions" onclosed={() => eraseChosen && ((eraseChosen = false), (eraseOpen = true))}>
            <DropdownMenu.Item variant="destructive" onSelect={() => (eraseChosen = true)}><Trash2Icon aria-hidden="true" />Erase candidate</DropdownMenu.Item>
          </MoreMenu>
        {/if}
      {/snippet}
      {#snippet meta()}
        {#if applications.length > 0}
          <MetaItem label="First applied" icon={CalendarIcon}><span>First applied <DateText at={applications[0].submittedAt} class="text-foreground" /></span></MetaItem>
        {/if}
      {/snippet}
    </HeaderBand>

    <div class="@container flex w-full flex-col gap-5 px-4 py-5 md:px-6 md:py-6">
      {#if asked && mayErase}
        <!-- They used the link in a talent pool invitation to stop being kept. Erasing their data as well is a separate,
             deliberate step (A2), offered here because it is what they may be asking for. -->
        <Notice>
          They asked to be taken out of the talent pool on {formatDate(asked)}.
        </Notice>
      {/if}

      <div class="grid items-start gap-5 @5xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <Panel title="Applications" count={applications.length}>
          <ol aria-label="Applications, oldest first" class="flex flex-col divide-y divide-border-subtle">
            {#each applications as application (application.id)}
              {@const progress = progressOf(application, history.data?.data)}
              <li class="flex min-w-0 flex-col gap-1.5 py-3 first:pt-0 last:pb-0">
                <div class="flex min-w-0 items-center gap-3">
                  <span class="flex min-w-0 flex-1"><TruncatedText text={application.jobTitle} href={`/applications/${application.id}`} underlined /></span>
                  <Badge variant="muted">{progress.outcome}</Badge>
                </div>
                <dl class="flex min-w-0 flex-wrap items-center gap-x-5 gap-y-1 text-muted-foreground">
                  <MetaItem label="Applied"><span>Applied <DateText at={application.submittedAt} /></span></MetaItem>
                  <MetaItem label="How far it got"><span>Got as far as {progress.reached}</span></MetaItem>
                  {#if progress.endedAt}
                    <MetaItem label="How it ended"><span>Ended <DateText at={progress.endedAt} /></span></MetaItem>
                  {/if}
                </dl>
              </li>
            {/each}
          </ol>
        </Panel>

        <Panel title="History" count={items?.length}>
          {#if history.isError && !items}
            <QueryError message={describeError(history.error, 'Could not load the history')} onretry={() => history.refetch()} inset />
          {:else if items && items.length === 0}
            <p class="text-muted-foreground">Nothing has happened yet.</p>
          {:else}
            <ActivityList label="History across all applications" {items} />
          {/if}
        </Panel>
      </div>
    </div>

    <EraseCandidateDialog bind:open={eraseOpen} candidateId={detail.id} candidateName={detail.fullName} ondone={() => goto('/candidates')} />
  {/if}
</main>
