<script lang="ts">
  import type { HttpValidationProblemDetails } from '@screenhire/api-client';
  import { ApiError, createCreateJob, createListHiringTemplates } from '@screenhire/api-client/svelte-query';
  import { CreateJobBody } from '@screenhire/api-client/zod';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { goto } from '$app/navigation';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { applyValidationProblem, paragraphs } from '#lib/forms.js';
  import { checkWhere, whereBody, workplaceOptions, workplaceValues } from '#lib/job-form.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';

  // A new job is a draft with its own copy of a published template version's workflow (workflow-engine.md §1).
  const queryClient = useQueryClient();
  const create = createCreateJob(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));
  const templates = createListHiringTemplates();
  const usable = $derived((templates.data?.data ?? []).filter((template) => template.latestPublishedVersionId));

  // The select's "none" is an empty string, which the body's uuid would refuse; it becomes null on the way out.
  // Where the work happens is asked up front because publishing needs it (JobPublishValidator); so is a description
  // when no template brings one. The rest of the posting is the job page's "Edit details".
  const schema = CreateJobBody.pick({ title: true })
    .extend({
      title: z.string().trim().min(1, 'Give the job a title.'),
      templateVersionId: z.string(),
      workplaceType: z.enum(workplaceValues),
      countries: z.string(),
      city: z.string().max(100, 'Keep the city to 100 characters or fewer.'),
      country: z.string(),
      description: z.string(),
    })
    .superRefine((value, context) => {
      const issue = (path: string, message: string) => context.addIssue({ code: 'custom', path: [path], message });
      checkWhere(value, issue);
      if (!value.templateVersionId && !value.description.trim()) issue('description', 'Describe the job, or choose a template that has a description.');
    });
  const empty = { title: '', templateVersionId: '', workplaceType: '', countries: '', city: '', country: '', description: '' } as const;
  const { form, errors, enhance, submitting } = superForm(defaults({ ...empty }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      const { title, templateVersionId, description } = submitted.data;
      try {
        const response = await create.mutateAsync({
          data: {
            title: title.trim(),
            templateVersionId: templateVersionId || null,
            ...whereBody(submitted.data),
            description: description.trim() ? paragraphs(description) : null,
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
              aria-describedby={describedBy('new-job-template-error', $errors.templateVersionId)}
            >
              <option value="">None: a basic pipeline</option>
              {#each usable as template (template.id)}
                <option value={template.latestPublishedVersionId}>{template.name} (v{template.latestPublishedVersion})</option>
              {/each}
            </select>
            <Field.Error id="new-job-template-error" errors={$errors.templateVersionId?.map((message) => ({ message }))} />
          </Field.Field>
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
            <Field.Field data-invalid={$errors.countries ? true : undefined}>
              <Field.Label for="new-job-countries">Countries candidates can work from</Field.Label>
              <Input
                id="new-job-countries"
                name="countries"
                required
                autocomplete="off"
                bind:value={$form.countries}
                aria-invalid={$errors.countries ? 'true' : undefined}
                aria-describedby={describedBy('new-job-countries-error', $errors.countries, 'new-job-countries-hint')}
              />
              <Field.Description id="new-job-countries-hint">Two-letter codes, separated by commas, such as GB, IE.</Field.Description>
              <Field.Error id="new-job-countries-error" errors={$errors.countries?.map((message) => ({ message }))} />
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
                    aria-describedby={describedBy('new-job-city-error', $errors.city)}
                  />
                  <Field.Error id="new-job-city-error" errors={$errors.city?.map((message) => ({ message }))} />
                </Field.Field>
                <Field.Field data-invalid={$errors.country ? true : undefined}>
                  <Field.Label for="new-job-country">Country code</Field.Label>
                  <Input
                    id="new-job-country"
                    name="country"
                    required
                    maxlength={2}
                    autocomplete="off"
                    bind:value={$form.country}
                    aria-invalid={$errors.country ? 'true' : undefined}
                    aria-describedby={describedBy('new-job-country-error', $errors.country)}
                  />
                  <Field.Error id="new-job-country-error" errors={$errors.country?.map((message) => ({ message }))} />
                </Field.Field>
              </div>
            </div>
          {/if}
          <Field.Field data-invalid={$errors.description ? true : undefined}>
            <Field.Label for="new-job-description">Description</Field.Label>
            <Textarea
              id="new-job-description"
              name="description"
              rows={6}
              bind:value={$form.description}
              aria-invalid={$errors.description ? 'true' : undefined}
              aria-describedby={describedBy('new-job-description-error', $errors.description, $form.templateVersionId ? 'new-job-description-hint' : undefined)}
            />
            {#if $form.templateVersionId}
              <Field.Description id="new-job-description-hint">Optional: empty keeps the template's description.</Field.Description>
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
            <Button type="submit" size="lg" disabled={$submitting}>Create job</Button>
          </div>
        {/snippet}
      </Panel>
    </form>
  </div>
</main>
