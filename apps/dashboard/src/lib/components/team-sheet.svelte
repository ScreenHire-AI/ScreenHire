<script lang="ts">
  import SaveIcon from '~icons/lucide/save';
  import UserPlusIcon from '~icons/lucide/user-plus';
  import XIcon from '~icons/lucide/x';
  import type { HttpValidationProblemDetails, TeamDetail } from '@screenhire/api-client';
  import { ApiError, createCreateTeam, createListTeamMembers, createUpdateTeam } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import IconButton from '#lib/components/icon-button.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import Picker from '#lib/components/picker.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { accessWords, mergeTeam } from '#lib/hiring-team.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { teamPath, teamsPath } from '#lib/settings.js';
  import { cn } from '#lib/utils.js';

  // A named team (Settings, Team, Teams): a name and some of the organization's people, each with their role. Adding the
  // team to a job copies these people into its hiring team, so a change here changes no job, which the sheet says. New
  // team and Edit team are this one sheet; the people collect here and land on Save, with the name (section 7, form).
  let {
    open = $bindable(false),
    team,
    onsaved,
  }: {
    open?: boolean;
    /** The team to edit, or null for a new one. */
    team: TeamDetail | null;
    /** Says what was saved, for the page's screen-reader line. */
    onsaved?: (message: string) => void;
  } = $props();

  const queryClient = useQueryClient();
  const members = createListTeamMembers(() => ({ query: { enabled: open } }));
  // A team change is also a line in the Team page's history.
  const afterSave = () => Promise.all([invalidatePath(queryClient, teamsPath), invalidatePath(queryClient, teamPath)]);
  const create = createCreateTeam(() => ({ mutation: { onSettled: afterSave } }));
  const update = createUpdateTeam(() => ({ mutation: { onSettled: afterSave } }));

  const schema = z.object({
    name: z.string().trim().min(1, 'Give the team a name.').max(140, 'Keep the name to 140 characters or fewer.'),
    memberIds: z.array(z.string()),
  });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '', memberIds: [] as string[] }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    dataType: 'json',
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      const data = { name: submitted.data.name.trim(), memberIds: submitted.data.memberIds };
      try {
        if (team) {
          await update.mutateAsync({ teamId: team.id, data });
        } else {
          await create.mutateAsync({ data });
        }
        open = false;
        onsaved?.(team ? `${data.name} was saved.` : `${data.name} was added.`);
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the team')];
        }
      }
    },
  });

  // Starts from the team as it is, once per opening, so a refetch while the sheet is open leaves the person's changes alone.
  let seeded = false;
  $effect(() => {
    if (!open) {
      seeded = false;
    } else if (!seeded) {
      reset({ data: { name: team?.name ?? '', memberIds: team?.members.map((person) => person.memberId) ?? [] } });
      seeded = true;
    }
  });

  const memberList = $derived(members.data?.data ?? []);
  // Each person chosen, by the organization's members, or by the team as loaded until they arrive.
  const people = $derived(
    $form.memberIds
      .map((memberId) => memberList.find((member) => member.id === memberId) ?? team?.members.find((person) => person.memberId === memberId))
      .filter((person) => person !== undefined)
      .map((person) => ({ memberId: 'memberId' in person ? person.memberId : person.id, displayName: person.displayName, roleName: person.roleName, seesAllJobs: person.seesAllJobs })),
  );
  const addable = $derived(
    memberList
      .filter((member) => !$form.memberIds.includes(member.id))
      .map((member) => ({ id: member.id, label: member.displayName, hint: `${member.roleName}, ${accessWords(member).toLowerCase()}` })),
  );
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>{team ? 'Edit team' : 'New team'}</Sheet.Title>
      <Sheet.Description>Add a team to a job to put its people on the job's hiring team. Changing the team later changes no job.</Sheet.Description>
    </Sheet.Header>
    <form method="POST" use:enhance novalidate aria-label={team ? 'Edit team' : 'New team'} class="flex flex-col gap-5 px-4 pb-4">
      <Field.Field data-invalid={$errors.name ? true : undefined}>
        <Field.Label for="team-name">Name</Field.Label>
        <Input
          id="team-name"
          name="name"
          maxlength={140}
          required
          autocomplete="off"
          bind:value={$form.name}
          aria-invalid={$errors.name ? 'true' : undefined}
          aria-describedby={$errors.name ? 'team-name-error' : undefined}
        />
        <Field.Error id="team-name-error" errors={$errors.name?.map((message) => ({ message }))} />
      </Field.Field>

      <div class="flex flex-col gap-2">
        <span class="text-body font-medium">People</span>
        {#if people.length === 0}
          <p class="text-muted-foreground">Nobody yet.</p>
        {:else}
          <ul aria-label="People on the team" class="flex flex-col">
            {#each people as person (person.memberId)}
              <li class="flex min-w-0 items-center gap-3 border-b border-border-subtle py-2 last:border-b-0">
                <span class="flex min-w-0 flex-1 flex-col">
                  <span class="truncate font-semibold">{person.displayName}</span>
                  <span class="truncate text-caption text-muted-foreground">{person.roleName}</span>
                </span>
                <!-- Only the exception is marked (W6): seeing every job is the default. -->
                {#if !person.seesAllJobs}<Badge variant="muted" class="shrink-0">{accessWords(person)}</Badge>{/if}
                <IconButton
                  type="button"
                  label={`Remove ${person.displayName} from the team`}
                  onclick={() => ($form.memberIds = $form.memberIds.filter((memberId) => memberId !== person.memberId))}
                >
                  <XIcon aria-hidden="true" />
                </IconButton>
              </li>
            {/each}
          </ul>
        {/if}
        {#if members.isError}
          <QueryError message={describeError(members.error, 'Could not load the people')} onretry={() => members.refetch()} inset />
        {:else}
          <div>
            <Picker
              label="Add a person"
              icon={UserPlusIcon}
              options={addable}
              searchLabel="Search people"
              empty="Nobody else to add."
              onpick={(memberId) => ($form.memberIds = mergeTeam($form.memberIds, [memberId]))}
            />
          </div>
        {/if}
        <Field.Error errors={$errors.memberIds?._errors?.map((message) => ({ message }))} />
      </div>

      <FailureText message={$errors._errors?.[0]} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />{team ? 'Save' : 'Add team'}</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
