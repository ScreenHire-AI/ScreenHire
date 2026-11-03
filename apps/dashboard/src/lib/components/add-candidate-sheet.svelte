<script lang="ts" module>
  /** A candidate just added, for the page's status line. */
  export interface AddedCandidate {
    applicationId: string;
    name: string;
    jobTitle: string;
  }
</script>

<script lang="ts">
  import UserPlusIcon from '~icons/lucide/user-plus';
  import type { HttpValidationProblemDetails, JobSummary } from '@screenhire/api-client';
  import { ApiError, createCreateApplication, createCreateResumeUpload, createListJobs } from '@screenhire/api-client/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';
  import { cn } from '#lib/utils.js';

  // A recruiter adds a candidate by hand (design system section 11, Add candidate sheet): the job, the name, the email,
  // an optional phone and an optional resume. The application starts in the job's first stage with the source Recruiter,
  // which the API sets. Nothing here is a careers form: no notice, no telemetry, no declarations, so the Verification
  // tab says which checks had nothing to look at.
  let {
    open = $bindable(false),
    job,
    onadded,
  }: {
    open?: boolean;
    /** The job page's own job: the picker is left out. */
    job?: Pick<JobSummary, 'id' | 'title'>;
    onadded?: (added: AddedCandidate) => void;
  } = $props();

  // A closed job takes no applications, so only drafts and open jobs are offered (A3). The list loads when the sheet
  // opens on a page with no job of its own.
  const jobs = createListJobs(() => ({ query: { enabled: open && !job } }));
  const offered = $derived(
    (jobs.data?.data ?? []).filter((candidate) => candidate.status !== 'Closed').toSorted((a, b) => a.title.localeCompare(b.title)),
  );

  const upload = createCreateResumeUpload();
  const create = createCreateApplication();

  const schema = z.object({
    jobId: z.string().min(1, 'Choose a job.'),
    firstName: z.string().trim().min(1, 'Enter a first name.').max(100, 'Keep the first name to 100 characters or fewer.'),
    lastName: z.string().trim().max(100, 'Keep the last name to 100 characters or fewer.'),
    // An empty address gets one message, not "Enter a valid email address." under it too.
    email: z.string().trim().min(1, { message: 'Enter an email address.', abort: true }).max(320, 'Keep the address to 320 characters or fewer.').email('Enter a valid email address.'),
    phone: z.string().trim().max(50, 'Keep the phone number to 50 characters or fewer.'),
    resume: z.string(),
  });
  const empty = () => ({ jobId: job?.id ?? '', firstName: '', lastName: '', email: '', phone: '', resume: '' });
  // The file types the job's resume question takes are checked by the API, which names them when a file does not fit.
  let resume = $state<File | null>(null);
  let fileInput = $state<HTMLInputElement | null>(null);

  // The API names the candidate's fields under `candidate.` and the file by its upload's fields; the form is flat.
  const field = (name: string) =>
    name.startsWith('candidate.') ? name.slice('candidate.'.length) : ['resumeUploadId', 'fileName', 'sizeBytes'].includes(name) ? 'resume' : name;

  const { form, errors, enhance, submitting, reset } = superForm(defaults(empty(), zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      const jobId = job?.id ?? submitted.data.jobId;
      const { firstName, lastName, email, phone } = submitted.data;
      try {
        let resumeUploadId: string | undefined;
        if (resume) {
          const ticket = (await upload.mutateAsync({ jobId, data: { fileName: resume.name, sizeBytes: resume.size } })).data;
          // The bytes go straight to storage through the create-only link, never through the API, so this is not an API
          // route and the generated client has no function for it (the careers form uploads the same way).
          const stored = await fetch(ticket.uploadUrl, { method: ticket.method, headers: ticket.headers, body: resume });
          if (!stored.ok) {
            submitted.valid = false;
            submitted.errors.resume = ['Could not upload the resume. Try again.'];
            return;
          }
          resumeUploadId = ticket.uploadId;
        }

        const created = (
          await create.mutateAsync({
            jobId,
            data: {
              candidate: { firstName: firstName.trim(), lastName: lastName.trim() || null, email: email.trim(), phone: phone.trim() || null },
              resumeUploadId: resumeUploadId ?? null,
            },
          })
        ).data;
        open = false;
        onadded?.({ applicationId: created.id, name: created.candidate.fullName, jobTitle: created.jobTitle });
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails, field);
        } else {
          // 409: a closed job, someone who already applied to it, or a candidate on a job this person does not see. The
          // API's words say which.
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not add the candidate')];
        }
      }
    },
  });

  $effect(() => {
    if (open) {
      reset({ data: empty() });
      resume = null;
      if (fileInput) fileInput.value = '';
    }
  });

  const describedBy = (id: string, messages: string[] | undefined) => (messages ? id : undefined);
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Add candidate</Sheet.Title>
      <Sheet.Description>{job ? `They join ${job.title} in its first stage.` : "They join the job's first stage."}</Sheet.Description>
    </Sheet.Header>
    <!-- The enctype only satisfies superforms, which refuses a form with a file input without it; in SPA mode the form
         never posts, and the resume goes to storage on its own (onUpdate). -->
    <form method="POST" enctype="multipart/form-data" use:enhance novalidate class="flex flex-col gap-5 px-4 pb-4" data-add-candidate>
      {#if !job}
        <Field.Field data-invalid={$errors.jobId ? true : undefined}>
          <Field.Label for="add-candidate-job">Job</Field.Label>
          <select
            id="add-candidate-job"
            name="jobId"
            class={selectClass}
            required
            bind:value={$form.jobId}
            aria-invalid={$errors.jobId ? 'true' : undefined}
            aria-describedby={describedBy('add-candidate-job-error', $errors.jobId)}
          >
            <option value="" disabled>{jobs.isPending ? 'Loading jobs…' : 'Choose a job'}</option>
            {#each offered as option (option.id)}<option value={option.id}>{option.title}</option>{/each}
          </select>
          <Field.Error id="add-candidate-job-error" errors={$errors.jobId?.map((message) => ({ message }))} />
        </Field.Field>
      {/if}
      <div class="flex flex-col gap-5">
        <Field.Field data-invalid={$errors.firstName ? true : undefined}>
          <Field.Label for="add-candidate-first">First name</Field.Label>
          <Input
            id="add-candidate-first"
            name="firstName"
            autocomplete="off"
            maxlength={100}
            required
            bind:value={$form.firstName}
            aria-invalid={$errors.firstName ? 'true' : undefined}
            aria-describedby={describedBy('add-candidate-first-error', $errors.firstName)}
          />
          <Field.Error id="add-candidate-first-error" errors={$errors.firstName?.map((message) => ({ message }))} />
        </Field.Field>
        <Field.Field data-invalid={$errors.lastName ? true : undefined}>
          <Field.Label for="add-candidate-last">Last name <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
          <Input
            id="add-candidate-last"
            name="lastName"
            autocomplete="off"
            maxlength={100}
            bind:value={$form.lastName}
            aria-invalid={$errors.lastName ? 'true' : undefined}
            aria-describedby={describedBy('add-candidate-last-error', $errors.lastName)}
          />
          <Field.Error id="add-candidate-last-error" errors={$errors.lastName?.map((message) => ({ message }))} />
        </Field.Field>
      </div>
      <Field.Field data-invalid={$errors.email ? true : undefined}>
        <Field.Label for="add-candidate-email">Email</Field.Label>
        <Input
          id="add-candidate-email"
          name="email"
          type="email"
          autocomplete="off"
          maxlength={320}
          required
          bind:value={$form.email}
          aria-invalid={$errors.email ? 'true' : undefined}
          aria-describedby={describedBy('add-candidate-email-error', $errors.email)}
        />
        <Field.Error id="add-candidate-email-error" errors={$errors.email?.map((message) => ({ message }))} />
      </Field.Field>
      <Field.Field data-invalid={$errors.phone ? true : undefined}>
        <Field.Label for="add-candidate-phone">Phone <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
        <Input
          id="add-candidate-phone"
          name="phone"
          type="tel"
          autocomplete="off"
          maxlength={50}
          bind:value={$form.phone}
          aria-invalid={$errors.phone ? 'true' : undefined}
          aria-describedby={describedBy('add-candidate-phone-error', $errors.phone)}
        />
        <Field.Error id="add-candidate-phone-error" errors={$errors.phone?.map((message) => ({ message }))} />
      </Field.Field>
      <Field.Field data-invalid={$errors.resume ? true : undefined}>
        <Field.Label for="add-candidate-resume">Resume <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
        <Input
          bind:ref={fileInput}
          id="add-candidate-resume"
          name="resume"
          type="file"
          onchange={(event) => {
            resume = event.currentTarget.files?.[0] ?? null;
            $errors.resume = undefined;
          }}
          aria-invalid={$errors.resume ? 'true' : undefined}
          aria-describedby={describedBy('add-candidate-resume-error', $errors.resume)}
        />
        <Field.Error id="add-candidate-resume-error" errors={$errors.resume?.map((message) => ({ message }))} />
      </Field.Field>
      <FailureText message={$errors._errors?.[0]} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={$submitting}><UserPlusIcon aria-hidden="true" />Add candidate</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
