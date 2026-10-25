<script lang="ts">
  import BracesIcon from '@lucide/svelte/icons/braces';
  import SendIcon from '@lucide/svelte/icons/send';
  import type { EmailPreview as Preview, HttpValidationProblemDetails } from '@screenhire/api-client';
  import { ApiError, createListEmailTemplates, createPreviewApplicationEmail, createSendEmails } from '@screenhire/api-client/svelte-query';
  import { tick, untrack } from 'svelte';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import EditPreviewSwitch from '#lib/components/edit-preview-switch.svelte';
  import EmailPreview from '#lib/components/email-preview.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { cn } from '#lib/utils.js';
  import { describeQueued, emailCount, insertAt, sendableTemplates, sendableVariables, variableTag } from '#lib/email.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';

  // The email composer (design system section 11): start from one of the organization's templates or a blank email,
  // change it for this email only, insert variables, look at it as the first candidate receives it, and send. Each
  // candidate gets their own copy, rendered with their details, and it lands in their Activity and Messages as it went.
  // The template itself never changes here. The rejection and follow-up questions emails are never offered: only their
  // actions send them. In a sheet (`EmailComposerSheet`) for a selection or the candidate's header, and inline as the
  // box on the Messages tab.
  let {
    recipients,
    id = 'email-composer',
    label,
    inline = false,
    active = true,
    oncancel,
    onsent,
  }: {
    /** The applications to email, in the order the page shows them; the first one's candidate is the preview's. */
    recipients: readonly { id: string; candidateName: string }[];
    /** A prefix for the form's element ids, so two composers can share a page. */
    id?: string;
    /** The form's accessible name, such as "Email Casey Morgan". */
    label: string;
    /** In a page (the Messages tab) rather than a sheet: no To line and no Cancel. */
    inline?: boolean;
    /** Whether to load the templates, such as while the sheet is open. */
    active?: boolean;
    oncancel?: () => void;
    /** After the API queued the emails, with a line saying how many. */
    onsent?: (message: string) => void;
  } = $props();

  const templates = createListEmailTemplates(() => ({ query: { enabled: active } }));
  const choices = $derived(sendableTemplates(templates.data?.data.templates ?? []));
  const variables = $derived(sendableVariables(templates.data?.data.variables ?? []));
  const first = $derived(recipients[0]);

  // '' is a blank email: it has no template, and nothing turns it into one.
  let startFrom = $state('');
  const template = $derived(choices.find((choice) => choice.id === startFrom));

  const schema = z.object({
    subject: z.string().trim().min(1, 'Enter a subject.').max(200, 'Keep the subject to 200 characters or fewer.'),
    body: z.string().trim().min(1, 'Enter the message.').max(10000, 'Keep the message to 10,000 characters or fewer.'),
  });

  const send = createSendEmails();
  const { form, errors, enhance, submitting } = superForm(defaults({ subject: '', body: '' }, zod4(schema)), {
    id: untrack(() => id),
    SPA: true,
    validators: zod4(schema),
    // A sent email leaves the fields empty for the next one; a refused one keeps what the person wrote.
    resetForm: true,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      try {
        const response = await send.mutateAsync({
          data: {
            applicationIds: recipients.map((recipient) => recipient.id),
            templateId: startFrom || null,
            subject: submitted.data.subject,
            body: submitted.data.body,
          },
        });
        onsent?.(describeQueued(response.data.queued, response.data.skipped));
        startFrom = '';
        mode = 'edit';
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not send the email')];
        }
      }
    },
  });

  // Picking a template puts its wording in the fields, to change for this email; picking a blank email empties them.
  function choose(value: string) {
    startFrom = value;
    const chosen = choices.find((choice) => choice.id === value);
    $form.subject = chosen?.subject ?? '';
    $form.body = chosen?.body ?? '';
  }

  const changed = $derived(template !== undefined && ($form.subject !== template.subject || $form.body !== template.body));

  // Insert a variable where the caret was in the subject or the message, whichever had focus last. The menu gives focus
  // back to its button as it closes, so the field takes it after that.
  let subjectInput = $state<HTMLInputElement | null>(null);
  let bodyInput = $state<HTMLTextAreaElement | null>(null);
  let lastField: 'subject' | 'body' = 'body';
  let caret: number | undefined;
  async function insert(name: string) {
    const element = lastField === 'subject' ? subjectInput : bodyInput;
    const current = $form[lastField];
    const next = insertAt(current, element?.selectionStart ?? current.length, element?.selectionEnd ?? current.length, variableTag(name));
    $form[lastField] = next.text;
    caret = next.caret;
    await tick();
  }

  function focusAfterInsert(event: Event) {
    if (caret === undefined) return;
    event.preventDefault();
    const element = lastField === 'subject' ? subjectInput : bodyInput;
    element?.focus();
    element?.setSelectionRange(caret, caret);
    caret = undefined;
  }

  // Edit and Preview never share the screen (A8). The preview is the first candidate's email, rendered by the API from
  // the wording as it stands when the preview opens.
  let mode = $state<'edit' | 'preview'>('edit');
  const previewing = createPreviewApplicationEmail();
  let preview = $state<Preview | undefined>();
  let previewFailure = $state<string | undefined>();
  let sequence = 0;
  $effect(() => {
    if (mode !== 'preview' || !first) return;
    const draft = { applicationId: first.id, subject: $form.subject, body: $form.body };
    const ticket = ++sequence;
    preview = undefined;
    previewFailure = undefined;
    if (!draft.subject.trim() || !draft.body.trim()) {
      previewFailure = 'Write the subject and the message to see the email.';
      return;
    }

    untrack(() =>
      previewing.mutateAsync({ data: draft }).then(
        (response) => ticket === sequence && (preview = response.data),
        (error: unknown) => {
          if (ticket !== sequence) return;
          const problem = error instanceof ApiError && error.status === 400 ? (error.body as HttpValidationProblemDetails) : undefined;
          previewFailure = problem ? (Object.values(problem.errors ?? {}).flat()[0] ?? 'Fix the email to see it.') : describeError(error, 'Could not show the preview');
        },
      ),
    );
  });

  const describedBy = (field: string, error: unknown) => (error ? `${id}-${field}-error` : undefined);
</script>

<form method="POST" use:enhance novalidate aria-label={label} class="@container flex min-w-0 flex-col gap-4">
  {#if !inline}
    <p class="min-w-0 break-words">
      <span class="text-muted-foreground">To</span>
      <span class="font-medium">{recipients.length === 1 ? first?.candidateName : `${recipients.length} candidates`}</span>
    </p>
  {/if}

  <Field.Field>
    <Field.Label for={`${id}-start`}>Start from</Field.Label>
    {#if templates.isError}
      <FailureText message={describeError(templates.error, 'Could not load the templates')} />
    {:else}
      <select id={`${id}-start`} class={selectClass} value={startFrom} onchange={(event) => choose(event.currentTarget.value)} disabled={!templates.data}>
        <option value="">Blank email</option>
        {#each choices as choice (choice.id)}<option value={choice.id}>{choice.name}</option>{/each}
      </select>
    {/if}
  </Field.Field>

  <div class="flex flex-wrap items-center justify-between gap-2">
    <EditPreviewSwitch bind:mode />
    {#if mode === 'edit'}
      <DropdownMenu.Root>
        <DropdownMenu.Trigger>
          {#snippet child({ props })}
            <Button {...props} type="button" variant="outline" disabled={variables.length === 0}><BracesIcon aria-hidden="true" />Insert variable</Button>
          {/snippet}
        </DropdownMenu.Trigger>
        <DropdownMenu.Content align="end" class="min-w-52" onCloseAutoFocus={focusAfterInsert}>
          {#each variables as variable (variable.name)}
            <DropdownMenu.Item onSelect={() => insert(variable.name)}>{variable.label}</DropdownMenu.Item>
          {/each}
        </DropdownMenu.Content>
      </DropdownMenu.Root>
    {/if}
  </div>

  <!-- Hidden rather than removed while previewing, so the fields keep their caret and the form keeps its values. -->
  <div class={cn('flex flex-col gap-4', mode === 'preview' && 'hidden')}>
    <Field.Field data-invalid={$errors.subject ? true : undefined}>
      <Field.Label for={`${id}-subject`}>Subject</Field.Label>
      <Input
        id={`${id}-subject`}
        name="subject"
        maxlength={200}
        autocomplete="off"
        required
        bind:ref={subjectInput}
        bind:value={$form.subject}
        onfocus={() => (lastField = 'subject')}
        aria-invalid={$errors.subject ? 'true' : undefined}
        aria-describedby={describedBy('subject', $errors.subject)}
      />
      <Field.Error id={`${id}-subject-error`} errors={$errors.subject?.map((message) => ({ message }))} />
    </Field.Field>

    <Field.Field data-invalid={$errors.body ? true : undefined}>
      <Field.Label for={`${id}-body`}>Message</Field.Label>
      <Textarea
        id={`${id}-body`}
        name="body"
        rows={inline ? 6 : 12}
        class="field-sizing-fixed resize-y"
        maxlength={10000}
        required
        bind:ref={bodyInput}
        bind:value={$form.body}
        onfocus={() => (lastField = 'body')}
        aria-invalid={$errors.body ? 'true' : undefined}
        aria-describedby={describedBy('body', $errors.body)}
      />
      <Field.Error id={`${id}-body-error`} errors={$errors.body?.map((message) => ({ message }))} />
    </Field.Field>

    {#if changed && template}
      <p class="text-muted-foreground">Changed for this email only. The {template.name} template stays as it is.</p>
    {/if}
  </div>

  {#if mode === 'preview'}
    <section aria-labelledby={`${id}-preview`} class="flex min-w-0 flex-col gap-2">
      <h3 id={`${id}-preview`} class="text-heading">{recipients.length === 1 ? 'Preview' : `Preview for ${first?.candidateName ?? ''}`}</h3>
      <EmailPreview {preview} failure={previewFailure} />
    </section>
  {/if}

  <div class={cn('flex flex-wrap items-center justify-end gap-2', !inline && 'border-t border-border-subtle pt-4')}>
    <FailureText message={$errors._errors?.[0]} class="mr-auto" />
    {#if oncancel}
      <Button type="button" variant="outline" size="lg" onclick={oncancel}>Cancel</Button>
    {/if}
    <!-- Inline on the Messages tab, Send is outline: the page's primary is Move to stage (A1). Never disabled for empty
         fields, which say what they need when it is pressed (A3). -->
    <Button type="submit" size="lg" variant={inline ? 'outline' : 'default'} disabled={$submitting}>
      <SendIcon aria-hidden="true" />{recipients.length === 1 ? 'Send' : `Send ${emailCount(recipients.length)}`}
    </Button>
  </div>
</form>
