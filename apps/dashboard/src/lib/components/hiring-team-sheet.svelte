<script lang="ts">
  import CopyIcon from '~icons/lucide/copy';
  import SaveIcon from '~icons/lucide/save';
  import UserPlusIcon from '~icons/lucide/user-plus';
  import UsersRoundIcon from '~icons/lucide/users-round';
  import XIcon from '~icons/lucide/x';
  import { createGetJobHiringTeam, createListJobs, createListTeamMembers, createListTeams, createSetJobHiringTeam, getJobHiringTeam } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import Picker from '#lib/components/picker.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { accessWords, mergeTeam, teamMemberIds, teamOptions } from '#lib/hiring-team.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { cn } from '#lib/utils.js';

  // Hiring team (design system section 21, ADR-0015): the people who work on a job. Anyone on Only jobs they're on sees
  // the job through this list. Changes collect here and land on Save. Add a team brings a named team's people into the
  // list as they are now (Settings, Team, Teams), and Copy from another job that job's people; either can then be taken
  // off one by one, and only the list as shown is saved. Without the permission the sheet only lists the people and says
  // why (E3). Someone who sees only their own jobs cannot take themselves off one, so their own row has no remove button.
  let {
    open = $bindable(false),
    id,
    name,
    mayEdit,
    currentMemberId,
    seesAllJobs,
  }: {
    open?: boolean;
    /** The job's id. */
    id: string;
    /** The job's title, the sheet's one line. */
    name: string;
    /** Whether the signed-in person's role may change it: Edit jobs. */
    mayEdit: boolean;
    /** The signed-in person's membership id, to keep them on a job while they see only their own jobs. */
    currentMemberId?: string;
    seesAllJobs: boolean;
  } = $props();

  const queryClient = useQueryClient();
  const path = $derived(`/v1/jobs/${id}/hiring-team`);
  const team = createGetJobHiringTeam(() => id);
  const members = createListTeamMembers(() => ({ query: { enabled: open && mayEdit } }));
  const teams = createListTeams(() => ({ query: { enabled: open && mayEdit } }));
  const jobs = createListJobs(() => ({ query: { enabled: open && mayEdit } }));
  const afterSave = () => Promise.all([invalidatePath(queryClient, path), invalidatePath(queryClient, '/v1/organization/members')]);
  const saveJob = createSetJobHiringTeam(() => ({ mutation: { onSettled: afterSave } }));

  let draft = $state<string[]>([]);
  let copyFromJobId = $state<string | null>(null);
  let failure = $state('');
  let saving = $state(false);

  // The draft starts from the team as loaded, once per opening: a live refetch while the sheet is open leaves the
  // person's changes alone, and nothing can be saved before the team has loaded.
  let seeded = $state(false);
  $effect(() => {
    if (!open) {
      seeded = false;
    } else if (!seeded && team.data) {
      draft = team.data.data.map((person) => person.memberId);
      copyFromJobId = null;
      failure = '';
      seeded = true;
    }
  });

  const saved = $derived(team.data?.data ?? []);
  const memberList = $derived(members.data?.data ?? []);
  // Each person in the list, from the team as saved or, once added here, from the organization's members.
  const people = $derived(
    draft
      .map((memberId) => {
        const member = memberList.find((candidate) => candidate.id === memberId);
        const onTeam = saved.find((person) => person.memberId === memberId);
        return member
          ? { memberId, displayName: member.displayName, roleName: member.roleName, seesAllJobs: member.seesAllJobs }
          : onTeam && { memberId, displayName: onTeam.displayName, roleName: onTeam.roleName, seesAllJobs: onTeam.seesAllJobs };
      })
      .filter((person) => person !== undefined),
  );
  const shownPeople = $derived(open && mayEdit ? people : saved);
  const addable = $derived(
    memberList
      .filter((member) => !draft.includes(member.id))
      .map((member) => ({ id: member.id, label: member.displayName, hint: `${member.roleName}, ${accessWords(member).toLowerCase()}` })),
  );
  const teamList = $derived(teams.data?.data ?? []);
  const copyable = $derived((jobs.data?.data ?? []).filter((job) => job.id !== id).map((job) => ({ id: job.id, label: job.title })));
  const mayRemove = (memberId: string) => seesAllJobs || memberId !== currentMemberId;

  async function copyFrom(jobId: string) {
    failure = '';
    try {
      const response = await getJobHiringTeam(jobId);
      draft = mergeTeam(draft, response.data.map((person) => person.memberId));
      copyFromJobId = jobId;
    } catch (error) {
      failure = describeError(error, "Could not read that job's hiring team");
    }
  }

  async function save(event: SubmitEvent) {
    event.preventDefault();
    if (!seeded) return;
    failure = '';
    saving = true;
    try {
      await saveJob.mutateAsync({ jobId: id, data: { memberIds: draft, copyFromJobId } });
      open = false;
    } catch (error) {
      failure = describeError(error, 'Could not save the hiring team');
    } finally {
      saving = false;
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Hiring team</Sheet.Title>
      <Sheet.Description>{name}</Sheet.Description>
    </Sheet.Header>
    <form onsubmit={save} novalidate class="flex flex-col gap-5 px-4 pb-4">
      <p class="text-muted-foreground">Anyone set to Only jobs they're on sees this job through this list.</p>

      {#if team.isError}
        <QueryError message={describeError(team.error, 'Could not load the hiring team')} onretry={() => team.refetch()} inset />
      {:else if team.isPending}
        <div aria-hidden="true" class="flex flex-col gap-2"><Skeleton class="h-10 w-full" /><Skeleton class="h-10 w-full" /></div>
      {:else if shownPeople.length === 0}
        <p>Nobody yet.</p>
      {:else}
        <ul aria-label="People on the team" class="flex flex-col">
          {#each shownPeople as person (person.memberId)}
            <li class="flex min-w-0 items-center gap-3 border-b border-border-subtle py-2 last:border-b-0">
              <span class="flex min-w-0 flex-1 flex-col">
                <span class="truncate font-semibold">{person.displayName}</span>
                <span class="truncate text-caption text-muted-foreground">{person.roleName}</span>
              </span>
              <!-- Only the exception is marked (W6): seeing every job is the default. -->
              {#if !person.seesAllJobs}<Badge variant="muted" class="shrink-0">{accessWords(person)}</Badge>{/if}
              {#if mayEdit && mayRemove(person.memberId)}
                <IconButton
                  type="button"
                  label={`Remove ${person.displayName} from the hiring team`}
                  onclick={() => (draft = draft.filter((memberId) => memberId !== person.memberId))}
                >
                  <XIcon aria-hidden="true" />
                </IconButton>
              {/if}
            </li>
          {/each}
        </ul>
      {/if}

      {#if mayEdit}
        {#if members.isError}
          <QueryError message={describeError(members.error, 'Could not load the team')} onretry={() => members.refetch()} inset />
        {:else}
          <div class="flex flex-wrap gap-2">
            <Picker label="Add a person" icon={UserPlusIcon} options={addable} searchLabel="Search people" empty="Nobody else to add." onpick={(memberId) => (draft = mergeTeam(draft, [memberId]))} />
            <!-- A team adds its people only; nothing remembers it, so a later change to the team changes no job. -->
            <Picker
              label="Add a team"
              icon={UsersRoundIcon}
              options={teamOptions(teamList)}
              searchLabel="Search teams"
              empty={teams.isError ? 'Could not load the teams.' : 'No teams yet. Make one in Settings, Team.'}
              onpick={(teamId) => (draft = mergeTeam(draft, teamMemberIds(teamList, teamId)))}
            />
            <Picker label="Copy from another job" icon={CopyIcon} options={copyable} searchLabel="Search jobs" empty="No other job to copy from." onpick={copyFrom} />
          </div>
        {/if}
        <FailureText message={failure} />
        <div class="flex justify-end gap-2">
          <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
          <Button type="submit" size="lg" disabled={saving || !seeded}><SaveIcon aria-hidden="true" />Save</Button>
        </div>
      {:else}
        <p class="text-muted-foreground">
          Your role cannot change the hiring team. Ask someone who can edit jobs.
        </p>
      {/if}
    </form>
  </Sheet.Content>
</Sheet.Root>
