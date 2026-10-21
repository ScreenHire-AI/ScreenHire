<script lang="ts">
  import type { HttpValidationProblemDetails, JobDetail } from '@screenhire/api-client';
  import { ApiError, createUpdateJob } from '@screenhire/api-client/svelte-query';
  import { UpdateJobBody } from '@screenhire/api-client/zod';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { untrack } from 'svelte';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { employmentLabel } from '#lib/format.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import {
    checkSalary,
    checkWhere,
    closingDate,
    closingInstant,
    descriptionBody,
    descriptionText,
    periodOptions,
    periodValues,
    salaryBody,
    whereBody,
    whereFields,
    workplaceOptions,
    workplaceValues,
  } from '#lib/job-form.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';

  // "Edit details": the job's posting content after it was created (PUT /v1/jobs/{id}), on the form template. The
  // workflow has its own page. An open job's edit is live on the careers page at once; boards get it when the job is
  // published again.
  let { job, cancelHref, onsaved }: { job: JobDetail; cancelHref: string; onsaved?: (message: string) => void } = $props();

  const queryClient = useQueryClient();
  const update = createUpdateJob(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));

  const employmentTypes = UpdateJobBody.shape.employmentType.options;

  const schema = UpdateJobBody.pick({ title: true })
    .extend({
      title: z.string().trim().min(1, 'Give the job a title.').max(200, 'Keep the title to 200 characters or fewer.'),
      department: z.string().max(100, 'Keep the department to 100 characters or fewer.'),
      employmentType: z.enum(employmentTypes),
      workplaceType: z.enum(workplaceValues),
      countries: z.string(),
      city: z.string().max(100, 'Keep the city to 100 characters or fewer.'),
      country: z.string(),
      salaryMin: z.string(),
      salaryMax: z.string(),
      currency: z.string(),
      period: z.enum(periodValues),
      closingDate: z.string(),
      description: z.string(),
    })
    .superRefine((value, context) => {
      const issue = (path: string, message: string) => context.addIssue({ code: 'custom', path: [path], message });
      checkWhere(value, issue);
      checkSalary(value, issue);
      if (!value.description.trim()) issue('description', 'Add a description: candidates and boards need one.');
    });

  // What the form started from, so an untouched description keeps the job's own HTML.
  let original = { html: '', text: '' };

  function fieldsOf(detail: JobDetail) {
    original = { html: detail.description, text: descriptionText(detail.description) };
    return {
      title: detail.title,
      department: detail.department ?? '',
      employmentType: detail.employmentType,
      ...whereFields(detail),
      salaryMin: detail.salary?.min?.toString() ?? '',
      salaryMax: detail.salary?.max?.toString() ?? '',
      currency: detail.salary?.currency ?? 'GBP',
      period: detail.salary?.period ?? 'Year',
      closingDate: closingDate(detail.validThrough),
      description: original.text,
    };
  }

  // The form starts from the job as it is when the page opens. The page hands over a new `job` whenever the query
  // refetches (a live event, a board confirming the job), and that must not wipe what the person has typed.
  const { form, errors, enhance, submitting } = superForm(defaults(untrack(() => fieldsOf(job)), zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      const value = submitted.data;
      try {
        await update.mutateAsync({
          jobId: job.id,
          data: {
            title: value.title.trim(),
            department: value.department.trim() || null,
            employmentType: value.employmentType,
            ...whereBody(value, job.locations),
            // checkWhere has refused an empty choice already; this narrows the type, which an update requires.
            workplaceType: value.workplaceType || job.workplaceType,
            salary: salaryBody(value),
            validThrough: closingInstant(value.closingDate, job.validThrough),
            description: descriptionBody(value.description, original),
          },
        });
        onsaved?.(job.status === 'Open' ? 'Saved. The careers page shows it now; publish again to update the boards.' : 'Saved.');
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the job')];
        }
      }
    },
  });

  const describedBy = (id: string, error: unknown, hint?: string) => [hint, error ? id : undefined].filter(Boolean).join(' ') || undefined;
</script>

<form method="POST" use:enhance novalidate aria-label="Edit job details">
  <Panel title="Details">
    <div class="@container flex flex-col gap-4">
      <Field.Field data-invalid={$errors.title ? true : undefined}>
        <Field.Label for="job-title">Title</Field.Label>
        <Input id="job-title" name="title" maxlength={200} required bind:value={$form.title} aria-invalid={$errors.title ? 'true' : undefined} aria-describedby={describedBy('job-title-error', $errors.title)} />
        <Field.Error id="job-title-error" errors={$errors.title?.map((message) => ({ message }))} />
      </Field.Field>

      <div class="flex flex-col gap-4 @md:grid @md:grid-cols-2 @md:gap-3">
        <Field.Field data-invalid={$errors.department ? true : undefined}>
          <Field.Label for="job-department">Department</Field.Label>
          <Input id="job-department" name="department" maxlength={100} bind:value={$form.department} aria-invalid={$errors.department ? 'true' : undefined} aria-describedby={describedBy('job-department-error', $errors.department)} />
          <Field.Error id="job-department-error" errors={$errors.department?.map((message) => ({ message }))} />
        </Field.Field>
        <Field.Field>
          <Field.Label for="job-employment">Employment type</Field.Label>
          <select id="job-employment" name="employmentType" class={selectClass} bind:value={$form.employmentType}>
            {#each employmentTypes as type (type)}<option value={type}>{employmentLabel(type)}</option>{/each}
          </select>
        </Field.Field>
      </div>

      <Field.Field data-invalid={$errors.workplaceType ? true : undefined}>
        <Field.Label for="job-workplace">Workplace</Field.Label>
        <select id="job-workplace" name="workplaceType" class={selectClass} required bind:value={$form.workplaceType} aria-invalid={$errors.workplaceType ? 'true' : undefined} aria-describedby={describedBy('job-workplace-error', $errors.workplaceType)}>
          {#each workplaceOptions as option (option.value)}<option value={option.value}>{option.label}</option>{/each}
        </select>
        <Field.Error id="job-workplace-error" errors={$errors.workplaceType?.map((message) => ({ message }))} />
      </Field.Field>
      {#if $form.workplaceType === 'Remote'}
        <Field.Field data-invalid={$errors.countries ? true : undefined}>
          <Field.Label for="job-countries">Countries candidates can work from</Field.Label>
          <Input id="job-countries" name="countries" required autocomplete="off" bind:value={$form.countries} aria-invalid={$errors.countries ? 'true' : undefined} aria-describedby={describedBy('job-countries-error', $errors.countries, 'job-countries-hint')} />
          <Field.Description id="job-countries-hint">Two-letter codes, separated by commas, such as GB, IE.</Field.Description>
          <Field.Error id="job-countries-error" errors={$errors.countries?.map((message) => ({ message }))} />
        </Field.Field>
      {:else}
        <div class="flex flex-col gap-4 @md:grid @md:grid-cols-[2fr_1fr] @md:gap-3">
          <Field.Field data-invalid={$errors.city ? true : undefined}>
            <Field.Label for="job-city">City</Field.Label>
            <Input id="job-city" name="city" maxlength={100} bind:value={$form.city} aria-invalid={$errors.city ? 'true' : undefined} aria-describedby={describedBy('job-city-error', $errors.city)} />
            <Field.Error id="job-city-error" errors={$errors.city?.map((message) => ({ message }))} />
          </Field.Field>
          <Field.Field data-invalid={$errors.country ? true : undefined}>
            <Field.Label for="job-country">Country code</Field.Label>
            <Input id="job-country" name="country" required maxlength={2} autocomplete="off" bind:value={$form.country} aria-invalid={$errors.country ? 'true' : undefined} aria-describedby={describedBy('job-country-error', $errors.country)} />
            <Field.Error id="job-country-error" errors={$errors.country?.map((message) => ({ message }))} />
          </Field.Field>
        </div>
      {/if}

      <fieldset class="flex flex-col gap-2">
        <legend class="mb-1 text-body font-medium">Salary <span class="font-normal text-muted-foreground">(optional)</span></legend>
        <div class="grid grid-cols-2 gap-3 @lg:grid-cols-4">
          <Field.Field data-invalid={$errors.salaryMin ? true : undefined}>
            <Field.Label for="job-salary-min">From</Field.Label>
            <Input id="job-salary-min" name="salaryMin" inputmode="decimal" bind:value={$form.salaryMin} aria-invalid={$errors.salaryMin ? 'true' : undefined} aria-describedby={describedBy('job-salary-min-error', $errors.salaryMin)} />
            <Field.Error id="job-salary-min-error" errors={$errors.salaryMin?.map((message) => ({ message }))} />
          </Field.Field>
          <Field.Field data-invalid={$errors.salaryMax ? true : undefined}>
            <Field.Label for="job-salary-max">To</Field.Label>
            <Input id="job-salary-max" name="salaryMax" inputmode="decimal" bind:value={$form.salaryMax} aria-invalid={$errors.salaryMax ? 'true' : undefined} aria-describedby={describedBy('job-salary-max-error', $errors.salaryMax)} />
            <Field.Error id="job-salary-max-error" errors={$errors.salaryMax?.map((message) => ({ message }))} />
          </Field.Field>
          <Field.Field data-invalid={$errors.currency ? true : undefined}>
            <Field.Label for="job-currency">Currency</Field.Label>
            <Input id="job-currency" name="currency" maxlength={3} autocomplete="off" bind:value={$form.currency} aria-invalid={$errors.currency ? 'true' : undefined} aria-describedby={describedBy('job-currency-error', $errors.currency)} />
            <Field.Error id="job-currency-error" errors={$errors.currency?.map((message) => ({ message }))} />
          </Field.Field>
          <Field.Field>
            <Field.Label for="job-period">Period</Field.Label>
            <select id="job-period" name="period" class={selectClass} bind:value={$form.period}>
              {#each periodOptions as option (option.value)}<option value={option.value}>{option.label}</option>{/each}
            </select>
          </Field.Field>
        </div>
      </fieldset>

      <Field.Field data-invalid={$errors.closingDate ? true : undefined}>
        <Field.Label for="job-closing">Closing date <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
        <Input id="job-closing" name="closingDate" type="date" bind:value={$form.closingDate} aria-invalid={$errors.closingDate ? 'true' : undefined} aria-describedby={describedBy('job-closing-error', $errors.closingDate, 'job-closing-hint')} />
        <Field.Description id="job-closing-hint">The posting expires at the end of this day, UTC.</Field.Description>
        <Field.Error id="job-closing-error" errors={$errors.closingDate?.map((message) => ({ message }))} />
      </Field.Field>

      <Field.Field data-invalid={$errors.description ? true : undefined}>
        <Field.Label for="job-description">Description</Field.Label>
        <Textarea id="job-description" name="description" rows={10} bind:value={$form.description} aria-invalid={$errors.description ? 'true' : undefined} aria-describedby={describedBy('job-description-error', $errors.description, 'job-description-hint')} />
        <Field.Description id="job-description-hint">A blank line starts a paragraph. "- " starts a list item.</Field.Description>
        <Field.Error id="job-description-error" errors={$errors.description?.map((message) => ({ message }))} />
      </Field.Field>

    </div>
    {#snippet footer()}
      <div class="flex flex-wrap items-center justify-end gap-2">
        {#if $errors._errors}
          <p role="alert" class="mr-auto text-body text-destructive">{$errors._errors[0]}</p>
        {/if}
        <Button href={cancelHref} variant="outline" size="lg">Cancel</Button>
        <Button type="submit" size="lg" disabled={$submitting}>Save details</Button>
      </div>
    {/snippet}
  </Panel>
</form>
