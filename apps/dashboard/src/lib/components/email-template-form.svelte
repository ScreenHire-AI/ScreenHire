<script lang="ts">
  import BracesIcon from '@lucide/svelte/icons/braces';
  import SendIcon from '@lucide/svelte/icons/send';
  import type { EmailPreview as Preview, EmailTemplateEntry, EmailTemplateVariableEntry, HttpValidationProblemDetails } from '@screenhire/api-client';
  import { ApiError, createPreviewEmailTemplate, createSendTestEmail } from '@screenhire/api-client/svelte-query';
  import { tick, untrack, type Snippet } from 'svelte';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import EditPreviewLayout from '#lib/components/edit-preview-layout.svelte';
  import EmailPreview from '#lib/components/email-preview.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import RichTextEditor from '#lib/components/rich-text-editor.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { insertAt, variableTag } from '#lib/email.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';
  import { isBlank } from '#lib/rich-text.js';

  // An email template's wording in the email template editor (design system section 7, builder; A8): Edit shows a name
  // for the organization's own, the subject and the rich-text message in Liquid over a fixed list of variables, each a
  // chip in the message (docs/architecture/rich-text.md); Preview shows the
  // email as the sample candidate receives it, with a test send to yourself. Side by side where both fit, else the Edit /
  // Preview switch, with the window button that opens the preview in a second window (`onpopout`).
  // The form stays mounted in both, so the page's Save (`form={formId}`) submits it from either; a submit with problems
  // goes back to Edit, where they show. Without `editable` the wording reads as labels and values (E3). The API checks
  // the Liquid again and says what is wrong.
  let {
    template,
    variables,
    sendingEnabled,
    editable = true,
    mode = $bindable('edit'),
    onpopout,
    formId,
    failure,
    onsave,
    rendered = $bindable(),
    dirty = $bindable(false),
  }: {
    /** The template to edit; none for a new one of the organization's own. */
    template?: EmailTemplateEntry;
    variables: readonly EmailTemplateVariableEntry[];
    /** Whether this deployment can send email, which a test send needs (A3). */
    sendingEnabled: boolean;
    editable?: boolean;
    mode?: 'edit' | 'preview';
    /** Opens the preview in a second window that follows the wording. */
    onpopout?: () => void;
    /** The form's id, for the page's Save button. */
    formId: string;
    /** What failed, for `describeError`: "Could not save the template". */
    failure: string;
    onsave: (value: { name: string | null; subject: string; body: string }) => Promise<unknown>;
    /** The email as it renders now, for the pop-out preview window. */
    rendered?: { preview: Preview | undefined; failure: string | undefined };
    /** Whether the wording differs from what was last saved. */
    dirty?: boolean;
  } = $props();

  const builtIn = untrack(() => template?.builtIn ?? false);
  const templateId = untrack(() => template?.id);
  const usable = $derived(
    variables.filter(
      (variable) =>
        (!variable.rejectOnly || template?.rejectOnly) &&
        (!variable.questionnaireOnly || template?.questionnaireOnly) &&
        (!variable.invitationOnly || template?.invitationOnly),
    ),
  );

  const schema = z.object({
    name: builtIn ? z.string() : z.string().trim().min(1, 'Enter a name.').max(100, 'Keep the name to 100 characters or fewer.'),
    subject: z.string().trim().min(1, 'Enter a subject.').max(200, 'Keep the subject to 200 characters or fewer.'),
    // The API checks the length of the sanitized HTML, which is what it stores.
    body: z.string().refine((body) => !isBlank(body), 'Enter the message.'),
  });

  const initial = untrack(() => ({ name: template?.builtIn ? '' : (template?.name ?? ''), subject: template?.subject ?? '', body: template?.body ?? '' }));
  let saved = $state({ ...initial });
  const { form, errors, enhance } = superForm(defaults(initial, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) {
        mode = 'edit';
        return;
      }
      try {
        await onsave({ name: builtIn ? null : submitted.data.name.trim(), subject: submitted.data.subject, body: submitted.data.body });
        saved = { ...submitted.data };
      } catch (error) {
        mode = 'edit';
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, failure)];
        }
      }
    },
  });

  $effect(() => {
    dirty = $form.name !== saved.name || $form.subject !== saved.subject || $form.body !== saved.body;
  });

  // Insert a variable where the caret was in the subject or the message, whichever had focus last: `{{ name }}` text in
  // the subject, a chip in the message.
  let subjectInput = $state<HTMLInputElement | null>(null);
  let bodyEditor = $state<ReturnType<typeof RichTextEditor> | null>(null);
  let lastField: 'subject' | 'body' = 'body';
  // Insert variable is a menu, as in the email composer (section 11): the pick goes in at the caret of the field last
  // used, and the focus returns there once the menu has closed.
  let caret: number | undefined;
  let inserted = false;
  function insert(name: string) {
    inserted = true;
    if (lastField === 'body') {
      bodyEditor?.insertVariable(name);
      return;
    }

    const current = $form.subject;
    const next = insertAt(current, subjectInput?.selectionStart ?? current.length, subjectInput?.selectionEnd ?? current.length, variableTag(name));
    $form.subject = next.text;
    caret = next.caret;
  }

  async function focusAfterInsert(event: Event) {
    if (!inserted) return;
    event.preventDefault();
    inserted = false;
    const at = caret;
    caret = undefined;
    await tick();
    if (lastField === 'body') {
      bodyEditor?.focus();
    } else if (at !== undefined) {
      subjectInput?.focus();
      subjectInput?.setSelectionRange(at, at);
    }
  }

  // The preview follows the wording as it is typed, a moment after typing stops, in either mode, so Preview and the
  // pop-out window are ready. A slower answer to older wording never replaces a newer one. With no wording yet there is
  // nothing to show or test, and a line says so (E1, A3).
  const previewing = createPreviewEmailTemplate();
  let preview = $state<Preview | undefined>();
  let previewFailure = $state<string | undefined>();
  let sequence = 0;
  const blank = $derived(!$form.subject.trim() && isBlank($form.body));
  $effect(() => {
    const draft = { subject: $form.subject, body: $form.body, templateId };
    const ticket = ++sequence;
    if (blank) return;
    const timer = setTimeout(async () => {
      try {
        const response = await previewing.mutateAsync({ data: draft });
        if (ticket !== sequence) return;
        preview = response.data;
        previewFailure = undefined;
      } catch (error) {
        if (ticket !== sequence) return;
        const problem = error instanceof ApiError && error.status === 400 ? (error.body as HttpValidationProblemDetails) : undefined;
        previewFailure = problem ? Object.values(problem.errors ?? {}).flat()[0] ?? 'Fix the template to see it.' : describeError(error, 'Could not show the preview');
      }
    }, 300);
    return () => clearTimeout(timer);
  });
  $effect(() => {
    rendered = { preview, failure: previewFailure };
  });

  const test = createSendTestEmail();
  let tested = $state('');
  let testFailure = $state('');
  async function sendTest() {
    tested = '';
    testFailure = '';
    try {
      const response = await test.mutateAsync({ data: { subject: $form.subject, body: $form.body, templateId } });
      tested = `Sent a test to ${response.data.to}.`;
    } catch (error) {
      const problem = error instanceof ApiError && error.status === 400 ? (error.body as HttpValidationProblemDetails) : undefined;
      testFailure = problem ? (Object.values(problem.errors ?? {}).flat()[0] ?? 'Fix the template first.') : describeError(error, 'Could not send the test');
    }
  }

  const describedBy = (id: string, error: unknown, hint?: string) => [hint, error ? id : undefined].filter(Boolean).join(' ') || undefined;
</script>

{#snippet wording()}
  <Panel title="Wording">
    {#if editable}
      <div class="flex flex-col gap-4">
        {#if !builtIn}
          <Field.Field data-invalid={$errors.name ? true : undefined}>
            <Field.Label for="email-template-name">Name</Field.Label>
            <Input
              id="email-template-name"
              name="name"
              maxlength={100}
              autocomplete="off"
              required
              bind:value={$form.name}
              aria-invalid={$errors.name ? 'true' : undefined}
              aria-describedby={describedBy('email-template-name-error', $errors.name)}
            />
            <Field.Error id="email-template-name-error" errors={$errors.name?.map((message) => ({ message }))} />
          </Field.Field>
        {/if}

        <Field.Field data-invalid={$errors.subject ? true : undefined}>
          <Field.Label for="email-template-subject">Subject</Field.Label>
          <Input
            id="email-template-subject"
            name="subject"
            maxlength={200}
            autocomplete="off"
            required
            bind:ref={subjectInput}
            bind:value={$form.subject}
            onfocus={() => (lastField = 'subject')}
            aria-invalid={$errors.subject ? 'true' : undefined}
            aria-describedby={describedBy('email-template-subject-error', $errors.subject)}
          />
          <Field.Error id="email-template-subject-error" errors={$errors.subject?.map((message) => ({ message }))} />
        </Field.Field>

        <Field.Field data-invalid={$errors.body ? true : undefined}>
          <Field.Label id="email-template-body-label" for="email-template-body" onclick={() => bodyEditor?.focus()}>Message</Field.Label>
          <RichTextEditor
            bind:this={bodyEditor}
            bind:value={$form.body}
            id="email-template-body"
            labelledBy="email-template-body-label"
            describedBy={describedBy('email-template-body-error', $errors.body)}
            invalid={!!$errors.body}
            variables={usable}
            onfocus={() => (lastField = 'body')}
            class="[&_.ProseMirror]:min-h-64"
          />
          <Field.Error id="email-template-body-error" errors={$errors.body?.map((message) => ({ message }))} />
        </Field.Field>

        <DropdownMenu.Root>
          <DropdownMenu.Trigger>
            {#snippet child({ props })}
              <Button {...props} type="button" variant="outline" class="self-start"><BracesIcon aria-hidden="true" />Insert variable</Button>
          {/snippet}
          </DropdownMenu.Trigger>
          <DropdownMenu.Content align="start" class="min-w-52" onCloseAutoFocus={focusAfterInsert}>
            {#each usable as variable (variable.name)}
              <DropdownMenu.Item onSelect={() => insert(variable.name)}>{variable.label}</DropdownMenu.Item>
            {/each}
          </DropdownMenu.Content>
        </DropdownMenu.Root>
        <FailureText message={$errors._errors?.[0]} />
      </div>
    {:else}
      <!-- Read only: labels and values, never disabled inputs (section 7). -->
      <dl class="flex flex-col gap-4">
        <div class="flex flex-col gap-1">
          <dt class="text-caption font-semibold text-muted-foreground">Subject</dt>
          <dd class="break-words">{$form.subject}</dd>
        </div>
        <div class="flex flex-col gap-1">
          <dt id="email-template-body-label" class="text-caption font-semibold text-muted-foreground">Message</dt>
          <dd>
            <RichTextEditor
              value={$form.body}
              id="email-template-body"
              labelledBy="email-template-body-label"
              editable={false}
              variables={usable}
              class="border-transparent [&_.ProseMirror]:min-h-0 [&_.ProseMirror]:p-0"
            />
          </dd>
        </div>
      </dl>
    {/if}
  </Panel>
{/snippet}
{#snippet result(popout: Snippet)}
  <Panel title="Preview">
    {#snippet actions()}
      {#if sendingEnabled && editable}
        <Button type="button" variant="outline" onclick={sendTest} disabled={blank || test.isPending}><SendIcon aria-hidden="true" />Send me a test</Button>
      {/if}
      {@render popout()}
  {/snippet}
    <div class="flex flex-col gap-3">
      {#if blank}
        <p class="text-muted-foreground">Write a subject and a message to see the email.</p>
      {:else}
        <EmailPreview {preview} failure={previewFailure} />
      {/if}
      <p role="status" class="text-muted-foreground empty:hidden">{tested}</p>
      <FailureText message={testFailure} />
    </div>
  </Panel>
{/snippet}

<form id={formId} method="POST" use:enhance novalidate aria-label={template ? 'Edit email template' : 'New email template'} class="flex w-full flex-col gap-5">
  <EditPreviewLayout bind:mode {onpopout} editor={wording} preview={result} />
</form>
