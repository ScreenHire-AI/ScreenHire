<script lang="ts">
  import SaveIcon from '@lucide/svelte/icons/save';
  import SendIcon from '@lucide/svelte/icons/send';
  import type { EmailPreview as Preview, EmailTemplateEntry, EmailTemplateVariableEntry, HttpValidationProblemDetails } from '@screenhire/api-client';
  import { ApiError, createPreviewEmailTemplate, createSendTestEmail } from '@screenhire/api-client/svelte-query';
  import { tick, untrack } from 'svelte';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import EmailPreview from '#lib/components/email-preview.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { insertAt, variableTag } from '#lib/email.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';

  // An email template's wording on the form template (design system section 7): a name for the
  // organization's own, the subject and the message in Liquid over a fixed list of variables, then the email as the
  // sample candidate receives it, and a test send to yourself. The API checks the Liquid again and says what is wrong.
  let {
    template,
    variables,
    sendingEnabled,
    submitLabel,
    cancelHref,
    failure,
    onsave,
  }: {
    /** The template to edit; none for a new one of the organization's own. */
    template?: EmailTemplateEntry;
    variables: readonly EmailTemplateVariableEntry[];
    /** Whether this deployment can send email, which a test send needs (A3). */
    sendingEnabled: boolean;
    submitLabel: string;
    cancelHref: string;
    /** What failed, for `describeError`: "Could not save the template". */
    failure: string;
    onsave: (value: { name: string | null; subject: string; body: string }) => Promise<unknown>;
  } = $props();

  const builtIn = untrack(() => template?.builtIn ?? false);
  const templateId = untrack(() => template?.id);
  const usable = $derived(variables.filter((variable) => !variable.rejectOnly || template?.rejectOnly));

  const schema = z.object({
    name: builtIn ? z.string() : z.string().trim().min(1, 'Enter a name.').max(100, 'Keep the name to 100 characters or fewer.'),
    subject: z.string().trim().min(1, 'Enter a subject.').max(200, 'Keep the subject to 200 characters or fewer.'),
    body: z.string().trim().min(1, 'Enter the message.').max(10000, 'Keep the message to 10,000 characters or fewer.'),
  });

  const initial = untrack(() => ({ name: template?.builtIn ? '' : (template?.name ?? ''), subject: template?.subject ?? '', body: template?.body ?? '' }));
  const { form, errors, enhance, submitting } = superForm(defaults(initial, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      try {
        await onsave({ name: builtIn ? null : submitted.data.name.trim(), subject: submitted.data.subject, body: submitted.data.body });
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, failure)];
        }
      }
    },
  });

  // Insert a variable where the caret was in the subject or the message, whichever had focus last.
  let subjectInput = $state<HTMLInputElement | null>(null);
  let bodyInput = $state<HTMLTextAreaElement | null>(null);
  let lastField: 'subject' | 'body' = 'body';
  async function insert(name: string) {
    const element = lastField === 'subject' ? subjectInput : bodyInput;
    const current = $form[lastField];
    const next = insertAt(current, element?.selectionStart ?? current.length, element?.selectionEnd ?? current.length, variableTag(name));
    $form[lastField] = next.text;
    await tick();
    element?.focus();
    element?.setSelectionRange(next.caret, next.caret);
  }

  // The preview follows the wording as it is typed, a moment after typing stops. A slower answer to older wording never
  // replaces a newer one.
  const previewing = createPreviewEmailTemplate();
  let preview = $state<Preview | undefined>();
  let previewFailure = $state<string | undefined>();
  let sequence = 0;
  $effect(() => {
    const draft = { subject: $form.subject, body: $form.body, templateId };
    const ticket = ++sequence;
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

<div class="flex flex-col gap-5">
  <form method="POST" use:enhance novalidate aria-label={template ? 'Edit email template' : 'New email template'}>
    <Panel title="Wording">
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
          <Field.Label for="email-template-body">Message</Field.Label>
          <!-- Twelve rows, not sized to its text: a content-sized box reflowed as the focus left it, and moved the preview
               below out from under a click. -->
          <Textarea
            id="email-template-body"
            name="body"
            rows={12}
            class="field-sizing-fixed resize-y"
            maxlength={10000}
            required
            bind:ref={bodyInput}
            bind:value={$form.body}
            onfocus={() => (lastField = 'body')}
            aria-invalid={$errors.body ? 'true' : undefined}
            aria-describedby={describedBy('email-template-body-error', $errors.body, 'email-template-body-hint')}
          />
          <Field.Description id="email-template-body-hint">Plain text. A blank line starts a new paragraph.</Field.Description>
          <Field.Error id="email-template-body-error" errors={$errors.body?.map((message) => ({ message }))} />
        </Field.Field>

        <div role="group" aria-labelledby="email-template-variables" class="flex flex-col gap-2">
          <span id="email-template-variables" class="text-body font-medium">Insert a variable</span>
          <div class="flex flex-wrap gap-2">
            {#each usable as variable (variable.name)}
              <Button type="button" variant="outline" onclick={() => insert(variable.name)}>{variable.label}</Button>
            {/each}
          </div>
        </div>
      </div>
      {#snippet footer()}
        <div class="flex flex-wrap items-center justify-end gap-2">
          <FailureText message={$errors._errors?.[0]} class="mr-auto" />
          <Button href={cancelHref} variant="outline" size="lg">Cancel</Button>
          <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />{submitLabel}</Button>
        </div>
      {/snippet}
    </Panel>
  </form>

  <Panel title="Preview">
    {#snippet actions()}
      {#if sendingEnabled}
        <Button variant="outline" onclick={sendTest} disabled={test.isPending}><SendIcon aria-hidden="true" />Send test</Button>
      {/if}
    {/snippet}
    <div class="flex flex-col gap-3">
      <EmailPreview {preview} failure={previewFailure} />
      <p role="status" class="text-muted-foreground empty:hidden">{tested}</p>
      <FailureText message={testFailure} />
    </div>
  </Panel>
</div>
