<script lang="ts">
  import SendIcon from '@lucide/svelte/icons/send';
  import { createListEmailTemplates, createPreviewApplicationEmail, createSendEmails } from '@screenhire/api-client/svelte-query';
  import EmailPreview from '#lib/components/email-preview.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { describeQueued, emailCount, sendableTemplates } from '#lib/email.js';
  import { describeError } from '#lib/query.js';

  // Email one candidate or a selection (design system section 11, Email sheet): a template, the email as the first
  // candidate receives it, and Send. Each candidate gets their own email; each one appears in its application's activity
  // once the worker has sent it. The rejection email is never offered: only a Reject action sends it.
  let {
    open = $bindable(false),
    recipients,
    onsent,
  }: {
    open?: boolean;
    /** The applications to email, in the order the page shows them. */
    recipients: readonly { id: string; candidateName: string }[];
    /** After the API queued the emails, with a line saying how many. */
    onsent?: (message: string) => void;
  } = $props();

  const templates = createListEmailTemplates(() => ({ query: { enabled: open } }));
  const choices = $derived(sendableTemplates(templates.data?.data.templates ?? []));
  let templateId = $state('');
  // The first template until the person picks one, and again if theirs goes away.
  const chosen = $derived(choices.some((template) => template.id === templateId) ? templateId : (choices[0]?.id ?? ''));
  const first = $derived(recipients[0]);
  const preview = createPreviewApplicationEmail(
    () => ({ applicationId: first?.id ?? '', templateId: chosen }),
    () => ({ query: { enabled: open && Boolean(first && chosen) } }),
  );
  const send = createSendEmails();
  let failure = $state('');

  const title = $derived(recipients.length === 1 ? `Email ${recipients[0]!.candidateName}` : `Email ${recipients.length} candidates`);

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    failure = '';
    try {
      const response = await send.mutateAsync({ data: { applicationIds: recipients.map((recipient) => recipient.id), templateId: chosen } });
      open = false;
      onsent?.(describeQueued(response.data.queued, response.data.skipped));
    } catch (error) {
      failure = describeError(error, 'Could not send the email');
    }
  }
</script>

<Sheet.Root bind:open onOpenChange={(next) => next || (failure = '')}>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-2xl">
    <Sheet.Header>
      <Sheet.Title>{title}</Sheet.Title>
    </Sheet.Header>
    <form id="email-sheet-form" class="@container flex flex-col gap-5 px-4" onsubmit={submit} aria-label={title}>
      <Field.Field>
        <Field.Label for="email-sheet-template">Template</Field.Label>
        {#if templates.isError}
          <FailureText message={describeError(templates.error, 'Could not load the templates')} />
        {:else}
          <select id="email-sheet-template" class={selectClass} bind:value={() => chosen, (value) => (templateId = value)} disabled={!templates.data}>
            {#each choices as template (template.id)}<option value={template.id}>{template.name}</option>{/each}
          </select>
        {/if}
      </Field.Field>
      <section aria-labelledby="email-sheet-preview" class="flex min-w-0 flex-col gap-2">
        <h3 id="email-sheet-preview" class="text-heading">
          {recipients.length === 1 ? 'Preview' : `Preview for ${first?.candidateName ?? ''}`}
        </h3>
        <EmailPreview
          preview={preview.data?.data}
          failure={preview.isError ? describeError(preview.error, 'Could not show the preview') : undefined}
        />
      </section>
    </form>
    <Sheet.Footer class="flex-row flex-wrap items-center justify-end">
      <FailureText message={failure} class="mr-auto" />
      <Sheet.Close>
        {#snippet child({ props })}<Button {...props} variant="outline" size="lg">Cancel</Button>{/snippet}
      </Sheet.Close>
      <Button type="submit" form="email-sheet-form" size="lg" disabled={!chosen || send.isPending}>
        <SendIcon aria-hidden="true" />{recipients.length === 1 ? 'Send' : `Send ${emailCount(recipients.length)}`}
      </Button>
    </Sheet.Footer>
  </Sheet.Content>
</Sheet.Root>
