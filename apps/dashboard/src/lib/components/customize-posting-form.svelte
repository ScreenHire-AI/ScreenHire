<script lang="ts">
  import SendIcon from '~icons/lucide/send';
  import type { HttpValidationProblemDetails, PostingCustomization, PostingPreview } from '@screenhire/api-client';
  import { ApiError, createCustomizePosting, createPreviewPostingCustomization } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { untrack } from 'svelte';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import EditPreviewSwitch from '#lib/components/edit-preview-switch.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import RichTextEditor from '#lib/components/rich-text-editor.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';

  // The Customize sheet's form (design system section 11): what one board receives in place of the job's title and
  // description. Each field starts as what the board gets now, which is the job's text until it is customized, and a
  // field that reads as the job's own follows the job again. Saving runs the board's checks on the new text and posts to
  // this board only; a failing check is said here and nothing is sent.
  let {
    customization,
    jobId,
    onsaved,
    oncancel,
  }: {
    customization: PostingCustomization;
    jobId: string;
    /** After the board took the customization; the row shows the new state. */
    onsaved?: () => void;
    oncancel?: () => void;
  } = $props();

  const queryClient = useQueryClient();
  const save = createCustomizePosting(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));
  const previewing = createPreviewPostingCustomization();

  const schema = z.object({
    title: z.string().trim().max(200, 'Keep the title to 200 characters or fewer.'),
    description: z.string(),
  });
  // A copy: the sheet edits one customization, and a refetch of the job underneath it must not rewrite what is typed.
  const job = untrack(() => ({ version: customization.jobVersion, title: customization.title.job, description: customization.description.job }));
  const start = untrack(() => ({
    title: customization.title.override ?? customization.title.job,
    description: customization.description.override ?? customization.description.job,
  }));

  const { form, errors, enhance, submitting } = superForm(defaults(start, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      try {
        await save.mutateAsync({
          jobId,
          boardId: customization.boardId,
          data: { title: submitted.data.title, description: submitted.data.description, jobVersion: job.version },
        });
        onsaved?.();
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          // The board's checks arrive under `board`, which is not a field, so they become the form's own errors.
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, `Could not post to ${customization.boardName}`)];
        }
      }
    },
  });

  const titleCustom = $derived($form.title.trim() !== '' && $form.title.trim() !== job.title);
  const descriptionCustom = $derived($form.description !== job.description);

  let descriptionEditor = $state<ReturnType<typeof RichTextEditor> | null>(null);
  function useJobDescription() {
    $form.description = job.description;
  }

  // Edit and Preview never share the screen in a sheet (A8). The preview is the API's answer for the text as it stands
  // when the preview opens: what the board would receive, with what its checks say about it.
  let mode = $state<'edit' | 'preview'>('edit');
  let preview = $state<PostingPreview | undefined>();
  let previewFailure = $state<string | undefined>();
  let sequence = 0;
  $effect(() => {
    if (mode !== 'preview') return;
    const draft = { title: $form.title, description: $form.description };
    const ticket = ++sequence;
    preview = undefined;
    previewFailure = undefined;
    untrack(() =>
      previewing.mutateAsync({ jobId, boardId: customization.boardId, data: draft }).then(
        (response) => ticket === sequence && (preview = response.data),
        (error: unknown) => {
          if (ticket !== sequence) return;
          const problem = error instanceof ApiError && error.status === 400 ? (error.body as HttpValidationProblemDetails) : undefined;
          previewFailure = problem ? (Object.values(problem.errors ?? {}).flat()[0] ?? 'Fix the text to see it.') : describeError(error, 'Could not show the preview');
        },
      ),
    );
  });

  // A failed save says what the preview already says, so the footer leaves that out (W7).
  const saveErrors = $derived(($errors._errors ?? []).filter((message) => !(mode === 'preview' && preview?.issues.includes(message))));

  // Bare HTML would draw in the browser's serif. A sandboxed frame cannot reach the page's fonts, so this is the system's.
  const frameDocument = (html: string) => `<!doctype html><style>body{font-family:system-ui,sans-serif;line-height:1.6}</style>${html}`;

  const describedBy = (field: string, error: unknown) => (error ? `customize-${field}-error` : undefined);
</script>

<form method="POST" use:enhance novalidate aria-label={`Customize for ${customization.boardName}`} class="flex min-w-0 flex-col gap-4" data-customize-posting>
  {#if customization.changes.length > 0}
    <!-- The job moved on after this was customized: what changed, so the board's text can be reviewed (E2). -->
    <section aria-labelledby="customize-changes" data-customize-changes class="flex flex-col gap-2 rounded-lg bg-secondary p-3">
      <h3 id="customize-changes" class="text-heading">What changed in the job</h3>
      {#each customization.changes as change (change.field)}
        <div class="flex min-w-0 flex-col gap-1">
          <p class="font-semibold">{change.field}</p>
          <p class="break-words whitespace-pre-line text-muted-foreground"><span class="sr-only">Before: </span><span class="line-through">{change.before}</span></p>
          <p class="break-words whitespace-pre-line"><span class="sr-only">Now: </span>{change.after}</p>
        </div>
      {/each}
    </section>
  {/if}

  <EditPreviewSwitch bind:mode />

  <!-- Hidden rather than removed while previewing, so the fields keep their caret and the form keeps its values. -->
  <div class={cn('flex flex-col gap-4', mode === 'preview' && 'hidden')}>
    <!-- A field that differs from the job's says so by offering the way back, in the row under it (F6, W6). -->
    <Field.Field data-invalid={$errors.title ? true : undefined}>
      <Field.Label for="customize-title">Title</Field.Label>
      <Input
        id="customize-title"
        name="title"
        maxlength={200}
        autocomplete="off"
        bind:value={$form.title}
        aria-invalid={$errors.title ? 'true' : undefined}
        aria-describedby={describedBy('title', $errors.title)}
      />
      <div class="flex min-w-0 items-start justify-between gap-2">
        <Field.Error id="customize-title-error" errors={$errors.title?.map((message) => ({ message }))} />
        {#if titleCustom}
          <Button type="button" variant="outline" size="sm" class="ml-auto" onclick={() => ($form.title = job.title)}>Use the job's title</Button>
        {/if}
      </div>
    </Field.Field>

    <Field.Field data-invalid={$errors.description ? true : undefined}>
      <Field.Label id="customize-description-label" for="customize-description" onclick={() => descriptionEditor?.focus()}>Description</Field.Label>
      <RichTextEditor
        bind:this={descriptionEditor}
        bind:value={$form.description}
        id="customize-description"
        labelledBy="customize-description-label"
        describedBy={describedBy('description', $errors.description)}
        invalid={!!$errors.description}
        class="[&_.ProseMirror]:min-h-64"
      />
      <div class="flex min-w-0 items-start justify-between gap-2">
        <Field.Error id="customize-description-error" errors={$errors.description?.map((message) => ({ message }))} />
        {#if descriptionCustom}
          <Button type="button" variant="outline" size="sm" class="ml-auto" onclick={useJobDescription}>Use the job's description</Button>
        {/if}
      </div>
    </Field.Field>
  </div>

  {#if mode === 'preview'}
    <section aria-label="Preview" data-customize-preview class="flex min-w-0 flex-col gap-3">
      {#if previewFailure}
        <FailureText message={previewFailure} />
      {:else if !preview}
        <div aria-hidden="true" class="flex flex-col gap-3">
          <Skeleton class="h-5 w-2/3" />
          <Skeleton class="h-64 w-full rounded-lg" />
        </div>
        <p class="sr-only" role="status">Loading the preview…</p>
      {:else}
        {#each preview.issues as issue (issue)}<FailureText message={issue} />{/each}
        <p data-preview-title class="font-semibold break-words">{preview.title}</p>
        <!-- The description is sanitized HTML, but it renders in a sandboxed frame with no scripts, as an email does. -->
        <iframe title="Description preview" sandbox="" srcdoc={frameDocument(preview.description)} class="block h-80 w-full rounded-lg border border-border-subtle bg-card"></iframe>
      {/if}
    </section>
  {/if}

  <div class="flex flex-wrap items-center justify-end gap-2 border-t border-border-subtle pt-4">
    <div class="mr-auto flex min-w-0 flex-col gap-1">
      {#each saveErrors as message (message)}<FailureText {message} />{/each}
    </div>
    {#if oncancel}
      <Button type="button" variant="outline" size="lg" onclick={oncancel}>Cancel</Button>
    {/if}
    <!-- Never disabled for text that fails a check: pressing it says what the board needs (A3). -->
    <Button type="submit" size="lg" disabled={$submitting}><SendIcon aria-hidden="true" />Save and post</Button>
  </div>
</form>
