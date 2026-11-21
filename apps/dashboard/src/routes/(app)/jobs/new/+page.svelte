<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import UserPlusIcon from '~icons/lucide/user-plus';
  import XIcon from '~icons/lucide/x';
  import type { HttpValidationProblemDetails } from '@screenhire/api-client';
  import {
    ApiError,
    createCreateJob,
    createGetSuggestedTeam,
    createListHiringTemplates,
    createListTeamMembers,
    createListTeams,
  } from '@screenhire/api-client/svelte-query';
  import { CreateJobBody } from '@screenhire/api-client/zod';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { untrack } from 'svelte';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import CountryPicker from '#lib/components/country-picker.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import Panel from '#lib/components/panel.svelte';
  import Picker from '#lib/components/picker.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RichTextEditor from '#lib/components/rich-text-editor.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { countryOptions } from '#lib/countries.js';
  import { locales } from '#lib/dates.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { accessWords, mergeTeam, peopleWords } from '#lib/hiring-team.js';
  import { checkWhere, whereBody, workplaceOptions, workplaceValues } from '#lib/job-form.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { isBlank } from '#lib/rich-text.js';

  // A new job is a draft with its own copy of a published template version's workflow (workflow-engine.md §1).
  const queryClient = useQueryClient();
  const create = createCreateJob(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));
  const templates = createListHiringTemplates();
  let descriptionEditor = $state<ReturnType<typeof RichTextEditor> | null>(null);
  // An archived template is out of the pickers: no new job starts from it.
  const usable = $derived((templates.data?.data ?? []).filter((template) => template.latestPublishedVersionId && !template.archived));

  // Who's hiring (sign-in-and-tenancy.md, "Job access"): a named team and anyone else, copied into the job's hiring team with
  // the person creating it. The team starts as the one the last job from the same template or department used, until the
  // person picks one themselves. With nobody but the creator in the organization there is nobody to choose, so the step
  // is not shown and the job starts with its creator alone.
  const members = createListTeamMembers();
  const teams = createListTeams();
  const memberList = $derived(members.data?.data ?? []);
  const teamList = $derived(teams.data?.data ?? []);
  const askWho = $derived(members.isSuccess && memberList.length > 1);
  // A failed list shows the step with its error and a retry, so nobody creates the job without knowing who is on it.
  const showWho = $derived(askWho || members.isError);
  const selfId = $derived(memberList.find((member) => member.userId === page.data.user?.id)?.id);
  let teamChosen = $state(false);

  // The select's "none" is an empty string, which the body's uuid would refuse; it becomes null on the way out.
  // Where the work happens is asked up front because publishing needs it (JobPublishValidator); so is a description
  // when no template brings one. The rest of the posting is the job page's "Edit details".
  const schema = CreateJobBody.pick({ title: true })
    .extend({
      title: z.string().trim().min(1, 'Give the job a title.'),
      templateVersionId: z.string(),
      workplaceType: z.enum(workplaceValues),
      countries: z.array(z.string()),
      city: z.string().max(100, 'Keep the city to 100 characters or fewer.'),
      country: z.string(),
      description: z.string(),
      teamId: z.string(),
      memberIds: z.array(z.string()),
    })
    .superRefine((value, context) => {
      const issue = (path: string, message: string) => context.addIssue({ code: 'custom', path: [path], message });
      checkWhere(value, issue);
      if (!value.templateVersionId && isBlank(value.description)) issue('description', 'Describe the job, or choose a template that has a description.');
    });
  const empty = {
    title: '',
    templateVersionId: '',
    workplaceType: '' as const,
    countries: [] as string[],
    city: '',
    country: '',
    description: '',
    teamId: '',
    memberIds: [] as string[],
  };
  const countries = countryOptions(locales);
  const { form, errors, enhance, submitting } = superForm(defaults({ ...empty }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      const { title, templateVersionId, description, teamId, memberIds } = submitted.data;
      try {
        const response = await create.mutateAsync({
          data: {
            title: title.trim(),
            templateVersionId: templateVersionId || null,
            ...whereBody(submitted.data),
            // An empty editor sends nothing, so a template's description still applies.
            description: isBlank(description) ? null : description,
            // Nothing when the step is not shown: the job starts with its creator.
            ...(askWho ? { teamId: teamId || null, memberIds } : {}),
          },
        });
        await goto(`/jobs/${response.data.id}`);
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not create the job')];
        }
      }
    },
  });

  const describedBy = (id: string, error: unknown, hint?: string) => [hint, error ? id : undefined].filter(Boolean).join(' ') || undefined;

  const suggestion = createGetSuggestedTeam(
    () => ({ templateVersionId: $form.templateVersionId || undefined }),
    () => ({ query: { enabled: askWho && !!$form.templateVersionId } }),
  );
  const suggestedId = $derived($form.templateVersionId ? (suggestion.data?.data.teamId ?? '') : '');
  // Follows the suggestion, for the chosen template, until the person picks a team; a deleted team suggests nothing.
  $effect(() => {
    const next = teamList.some((team) => team.id === suggestedId) ? suggestedId : '';
    if (!teamChosen) untrack(() => ($form.teamId = next));
  });
  const chosenTeam = $derived(teamList.find((team) => team.id === $form.teamId));
  const teamHint = $derived.by(() => {
    if (!chosenTeam) return '';
    const people = chosenTeam.members.filter((person) => person.memberId !== selfId).map((person) => person.displayName);
    const names = people.length === 0 ? 'Nobody else is on it.' : `${people.join(', ')}.`;
    return !teamChosen && chosenTeam.id === suggestedId ? `Used on the last job like this one. ${names}` : names;
  });
  const extras = $derived(
    $form.memberIds.map((memberId) => memberList.find((member) => member.id === memberId)).filter((member) => member !== undefined),
  );
  // The chosen team's people come with it, so they are not offered again.
  const onTeam = $derived(new Set(chosenTeam?.members.map((person) => person.memberId) ?? []));
  const addable = $derived(
    memberList
      .filter((member) => member.id !== selfId && !$form.memberIds.includes(member.id) && !onTeam.has(member.id))
      .map((member) => ({ id: member.id, label: member.displayName, hint: `${member.roleName}, ${accessWords(member).toLowerCase()}` })),
  );
</script>

<svelte:head>
  <title>New job · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Jobs', href: '/' }, { label: 'New job' }]} back />

<main class="flex flex-col">
  <HeaderBand form title="New job" />

  <!-- The form template (section 7): one 688px column, labels above fields, actions at the end. -->
  <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
    <form method="POST" use:enhance novalidate aria-label="New job">
      <Panel title="Details">
        <div class="flex flex-col gap-4">
          <Field.Field data-invalid={$errors.title ? true : undefined}>
            <Field.Label for="new-job-title">Title</Field.Label>
            <Input
              id="new-job-title"
              name="title"
              maxlength={200}
              required
              bind:value={$form.title}
              aria-invalid={$errors.title ? 'true' : undefined}
              aria-describedby={describedBy('new-job-title-error', $errors.title)}
            />
            <Field.Error id="new-job-title-error" errors={$errors.title?.map((message) => ({ message }))} />
          </Field.Field>
          <Field.Field data-invalid={$errors.templateVersionId ? true : undefined}>
            <Field.Label for="new-job-template">Hiring template</Field.Label>
            <select
              id="new-job-template"
              name="templateVersionId"
              class={selectClass}
              bind:value={$form.templateVersionId}
              aria-invalid={$errors.templateVersionId ? 'true' : undefined}
              aria-describedby={describedBy('new-job-template-error', $errors.templateVersionId, 'new-job-template-hint')}
            >
              <option value="">No template, a basic pipeline</option>
              {#each usable as template (template.id)}
                <option value={template.latestPublishedVersionId}>{template.name} (v{template.latestPublishedVersion})</option>
              {/each}
            </select>
            <Field.Description id="new-job-template-hint">A template sets the stages, questions and emails.</Field.Description>
            <Field.Error id="new-job-template-error" errors={$errors.templateVersionId?.map((message) => ({ message }))} />
          </Field.Field>
          {#if showWho}
            <fieldset class="flex min-w-0 flex-col gap-3">
              <legend class="mb-2 text-body font-semibold">Who's hiring?</legend>
              <!-- Without the people nothing here can be sent, so a failed list shows only its error and Try again. -->
              {#if members.isError}
                <QueryError message={describeError(members.error, 'Could not load the team')} onretry={() => members.refetch()} inset />
              {:else}
                {#if teams.isError}
                  <QueryError message={describeError(teams.error, 'Could not load the teams')} onretry={() => teams.refetch()} inset />
                {:else if teamList.length > 0}
                  <Field.Field data-invalid={$errors.teamId ? true : undefined}>
                    <Field.Label for="new-job-team">Team</Field.Label>
                    <select
                      id="new-job-team"
                      name="teamId"
                      class={selectClass}
                      bind:value={$form.teamId}
                      onchange={() => (teamChosen = true)}
                      aria-invalid={$errors.teamId ? 'true' : undefined}
                      aria-describedby={describedBy('new-job-team-error', $errors.teamId, teamHint ? 'new-job-team-hint' : undefined)}
                    >
                      <option value="">No team</option>
                      {#each teamList as team (team.id)}<option value={team.id}>{team.name} ({peopleWords(team.members.length)})</option>{/each}
                    </select>
                    {#if teamHint}<Field.Description id="new-job-team-hint">{teamHint}</Field.Description>{/if}
                    <Field.Error id="new-job-team-error" errors={$errors.teamId?.map((message) => ({ message }))} />
                  </Field.Field>
                {/if}
                {#if extras.length > 0}
                  <ul aria-label="Also hiring" class="flex flex-col">
                    {#each extras as person (person.id)}
                      <li class="flex min-w-0 items-center gap-3 border-b border-border-subtle py-2 last:border-b-0">
                        <span class="flex min-w-0 flex-1 flex-col">
                          <span class="truncate font-semibold">{person.displayName}</span>
                          <span class="truncate text-caption text-muted-foreground">{person.roleName}</span>
                        </span>
                        {#if !person.seesAllJobs}<Badge variant="muted" class="shrink-0">{accessWords(person)}</Badge>{/if}
                        <IconButton
                          type="button"
                          label={`Remove ${person.displayName}`}
                          onclick={() => ($form.memberIds = $form.memberIds.filter((memberId) => memberId !== person.id))}
                        >
                          <XIcon aria-hidden="true" />
                        </IconButton>
                      </li>
                    {/each}
                  </ul>
                {/if}
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
                <Field.Error errors={$errors.memberIds?._errors?.map((message) => ({ message }))} />
              {/if}
            </fieldset>
          {/if}
          <Field.Field data-invalid={$errors.workplaceType ? true : undefined}>
            <Field.Label for="new-job-workplace">Workplace</Field.Label>
            <select
              id="new-job-workplace"
              name="workplaceType"
              class={selectClass}
              required
              bind:value={$form.workplaceType}
              aria-invalid={$errors.workplaceType ? 'true' : undefined}
              aria-describedby={describedBy('new-job-workplace-error', $errors.workplaceType)}
            >
              <option value="">Select</option>
              {#each workplaceOptions as option (option.value)}<option value={option.value}>{option.label}</option>{/each}
            </select>
            <Field.Error id="new-job-workplace-error" errors={$errors.workplaceType?.map((message) => ({ message }))} />
          </Field.Field>
          {#if $form.workplaceType === 'Remote'}
            <Field.Field data-invalid={$errors.countries?._errors ? true : undefined}>
              <Field.Label for="new-job-countries">Countries candidates can work from</Field.Label>
              <CountryPicker
                id="new-job-countries"
                bind:value={$form.countries}
                invalid={!!$errors.countries?._errors}
                describedby={describedBy('new-job-countries-error', $errors.countries?._errors)}
              />
              <Field.Error id="new-job-countries-error" errors={$errors.countries?._errors?.map((message) => ({ message }))} />
            </Field.Field>
          {:else if $form.workplaceType}
            <div class="@container">
              <div class="flex flex-col gap-4 @md:grid @md:grid-cols-[2fr_1fr] @md:gap-3">
                <Field.Field data-invalid={$errors.city ? true : undefined}>
                  <Field.Label for="new-job-city">City</Field.Label>
                  <Input
                    id="new-job-city"
                    name="city"
                    maxlength={100}
                    bind:value={$form.city}
                    aria-invalid={$errors.city ? 'true' : undefined}
                    aria-describedby={describedBy('new-job-city-error', $errors.city, 'new-job-city-hint')}
                  />
                  <Field.Description id="new-job-city-hint">Some job boards refuse an on-site or hybrid job without a city.</Field.Description>
                  <Field.Error id="new-job-city-error" errors={$errors.city?.map((message) => ({ message }))} />
                </Field.Field>
                <Field.Field data-invalid={$errors.country ? true : undefined}>
                  <Field.Label for="new-job-country">Country</Field.Label>
                  <!-- Picked by name, never typed as a code (W8); the value is the ISO code the API takes. -->
                  <select
                    id="new-job-country"
                    name="country"
                    class={selectClass}
                    required
                    bind:value={$form.country}
                    aria-invalid={$errors.country ? 'true' : undefined}
                    aria-describedby={describedBy('new-job-country-error', $errors.country)}
                  >
                    <option value="">Select</option>
                    {#each countries as option (option.value)}<option value={option.value}>{option.label}</option>{/each}
                  </select>
                  <Field.Error id="new-job-country-error" errors={$errors.country?.map((message) => ({ message }))} />
                </Field.Field>
              </div>
            </div>
          {/if}
          <Field.Field data-invalid={$errors.description ? true : undefined}>
            <Field.Label id="new-job-description-label" for="new-job-description" onclick={() => descriptionEditor?.focus()}>Description</Field.Label>
            <RichTextEditor
              bind:this={descriptionEditor}
              bind:value={$form.description}
              id="new-job-description"
              labelledBy="new-job-description-label"
              describedBy={describedBy('new-job-description-error', $errors.description, $form.templateVersionId ? 'new-job-description-hint' : undefined)}
              invalid={!!$errors.description}
            />
            {#if $form.templateVersionId}
              <Field.Description id="new-job-description-hint">Leave it empty to start from the template's description, and edit it before you publish.</Field.Description>
            {/if}
            <Field.Error id="new-job-description-error" errors={$errors.description?.map((message) => ({ message }))} />
          </Field.Field>
        </div>
        {#snippet footer()}
          <div class="flex flex-wrap items-center justify-end gap-2">
            {#if $errors._errors}
              <FailureText message={$errors._errors[0]} class="mr-auto" />
            {/if}
            <Button href="/" variant="outline" size="lg">Cancel</Button>
            <Button type="submit" size="lg" disabled={$submitting}><PlusIcon aria-hidden="true" />Create job</Button>
          </div>
        {/snippet}
      </Panel>
    </form>
  </div>
</main>
