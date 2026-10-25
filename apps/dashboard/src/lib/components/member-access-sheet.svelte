<script lang="ts">
  import SaveIcon from '@lucide/svelte/icons/save';
  import CheckIcon from '@lucide/svelte/icons/check';
  import type { JobAccess, PermissionSummary, RoleSummary, TeamMember } from '@screenhire/api-client';
  import { createChangeMemberJobAccess, createChangeMemberRole, createListMemberJobs } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { mayGiveJobAccess, roleKey, seesAllJobsWith } from '#lib/settings.js';
  import { cn } from '#lib/utils.js';

  // A member's access (design system section 22, ADR-0015): one role, for what they can do, and which jobs they see:
  // all of them, or only the jobs whose hiring team they are on. The jobs themselves are read only here; each job's
  // Hiring team changes them, so a job has one home for its people (W6). Owners and Admins see all jobs whatever the
  // choice, so it is not offered for them. Someone who sees only their own jobs cannot give All jobs, so the choice is
  // left out for them rather than disabled (A3). The API stays the judge (TeamPolicy).
  let {
    open = $bindable(false),
    member,
    roles,
    permissions,
    actorSeesAllJobs,
    onsaved,
  }: {
    open?: boolean;
    member: TeamMember | null;
    /** The roles the signed-in person may give (assignableRoles); the member's own is one, or the sheet is not offered. */
    roles: readonly RoleSummary[];
    /** The catalogue's permissions, for what the chosen role can do. */
    permissions: readonly PermissionSummary[];
    actorSeesAllJobs: boolean;
    /** Called with a sentence for screen readers once the access is saved. */
    onsaved?: (announcement: string) => void;
  } = $props();

  const queryClient = useQueryClient();
  const membersPath = '/v1/organization/members';
  const changeRole = createChangeMemberRole(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, membersPath) } }));
  const changeAccess = createChangeMemberJobAccess(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, membersPath) } }));
  const jobs = createListMemberJobs(() => member?.id ?? '', () => ({ query: { enabled: open && member !== null } }));

  let roleChoice = $state('');
  let accessChoice = $state<JobAccess>('All');
  let failure = $state('');
  let saving = $state(false);

  $effect(() => {
    if (open && member) {
      roleChoice = roleKey(member);
      accessChoice = member.jobAccess;
      failure = '';
    }
  });

  const firstName = $derived(member?.displayName.split(' ')[0] ?? '');
  const chosen = $derived(roles.find((role) => roleKey(role) === roleChoice));
  const alwaysAll = $derived(chosen ? seesAllJobsWith(chosen.role, 'HiringTeamOnly') : false);
  const mayOfferAll = $derived(chosen ? mayGiveJobAccess(actorSeesAllJobs, chosen.role, 'All') : actorSeesAllJobs);
  const can = $derived(chosen ? permissions.filter((permission) => chosen.permissions.includes(permission.permission)) : []);
  const jobList = $derived(jobs.data?.data ?? []);

  async function save(event: SubmitEvent) {
    event.preventDefault();
    if (!member || !chosen) return;
    failure = '';
    saving = true;
    try {
      if (roleChoice !== roleKey(member)) {
        await changeRole.mutateAsync({ memberId: member.id, data: { role: chosen.role, customRoleId: chosen.customRoleId } });
      }
      if (!alwaysAll && accessChoice !== member.jobAccess) {
        await changeAccess.mutateAsync({ memberId: member.id, data: { jobAccess: accessChoice } });
      }
      open = false;
      onsaved?.(`${member.displayName}'s access was saved.`);
    } catch (error) {
      failure = describeError(error, `Could not save ${member.displayName}'s access`);
    } finally {
      saving = false;
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>{member ? `${member.displayName}'s access` : 'Access'}</Sheet.Title>
      <Sheet.Description>{member?.email}</Sheet.Description>
    </Sheet.Header>
    {#if member}
      <form onsubmit={save} novalidate class="flex flex-col gap-5 px-4 pb-4">
        <div class="flex flex-col gap-1.5">
          <div class="flex items-baseline justify-between gap-3">
            <label for="access-role" class="text-body font-medium">Role</label>
            <a href="/settings/roles/compare" class="text-caption underline underline-offset-2 pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center">Compare roles</a>
          </div>
          <select id="access-role" class={selectClass} bind:value={roleChoice} aria-describedby="access-role-description">
            {#each roles as role (roleKey(role))}
              <option value={roleKey(role)}>{role.name}</option>
            {/each}
          </select>
          <span id="access-role-description" class="text-caption text-muted-foreground">{chosen?.description}</span>
        </div>

        <div class="flex flex-col gap-2">
          <span id="access-jobs-label" class="text-body font-medium">Jobs</span>
          {#if alwaysAll}
            <p class="text-muted-foreground">Owners and Admins see all jobs.</p>
          {:else if !mayOfferAll && member.jobAccess === 'All'}
            <p class="text-muted-foreground">All jobs. You see only the jobs you're on, so an owner or admin changes this.</p>
          {:else}
            <div role="radiogroup" aria-labelledby="access-jobs-label" class="flex flex-col gap-2">
              {#if mayOfferAll}
                <label
                  class="flex cursor-pointer items-start gap-3 rounded-lg border border-input p-3 has-checked:border-primary has-checked:bg-accent has-focus-visible:ring-2 has-focus-visible:ring-ring"
                >
                  <input type="radio" name="jobAccess" value="All" bind:group={accessChoice} class="mt-0.5 size-4 shrink-0 accent-primary" />
                  <span class="flex min-w-0 flex-col gap-0.5">
                    <span class="font-semibold">All jobs</span>
                    <span class="text-muted-foreground">Including jobs added later.</span>
                  </span>
                </label>
              {/if}
              <label
                class="flex cursor-pointer items-start gap-3 rounded-lg border border-input p-3 has-checked:border-primary has-checked:bg-accent has-focus-visible:ring-2 has-focus-visible:ring-ring"
              >
                <input type="radio" name="jobAccess" value="HiringTeamOnly" bind:group={accessChoice} class="mt-0.5 size-4 shrink-0 accent-primary" />
                <span class="flex min-w-0 flex-col gap-0.5">
                  <span class="font-semibold">Only jobs they're on</span>
                  <span class="text-muted-foreground">Each job's hiring team decides.</span>
                </span>
              </label>
            </div>
          {/if}
        </div>

        <section aria-labelledby="access-jobs-on" class="flex flex-col gap-2">
          <h3 id="access-jobs-on" class="text-body font-medium">Hiring teams {firstName} is on</h3>
          {#if jobs.isError}
            <QueryError message={describeError(jobs.error, 'Could not load their jobs')} onretry={() => jobs.refetch()} inset />
          {:else if jobs.isPending}
            <Skeleton class="h-6 w-48" />
          {:else if jobList.length === 0}
            <p class="text-muted-foreground">None yet.</p>
          {:else}
            <ul class="flex flex-wrap gap-1.5">
              {#each jobList as job (job.id)}
                <li><a href={`/jobs/${job.id}`} class="inline-flex min-h-6 items-center rounded-full border border-border px-2.5 text-caption font-semibold underline-offset-2 hover:underline pointer-coarse:min-h-11">{job.title}</a></li>
              {/each}
            </ul>
          {/if}
          <p class="text-caption text-muted-foreground">To add or remove {firstName}, use the Hiring team on each job's page.</p>
        </section>

        {#if chosen}
          <section aria-labelledby="access-can" class="flex flex-col gap-2">
            <h3 id="access-can" class="text-body font-medium">What {firstName} can do</h3>
            <ul class="flex flex-col gap-1.5">
              {#each can as permission (permission.permission)}
                <li class="flex gap-2"><CheckIcon aria-hidden="true" class="mt-0.5 size-4 shrink-0 text-status-clear" />{permission.name}</li>
              {/each}
            </ul>
          </section>
        {/if}

        <FailureText message={failure} />
        <div class="flex justify-end gap-2">
          <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
          <Button type="submit" size="lg" disabled={saving}><SaveIcon aria-hidden="true" />Save access</Button>
        </div>
      </form>
    {/if}
  </Sheet.Content>
</Sheet.Root>
