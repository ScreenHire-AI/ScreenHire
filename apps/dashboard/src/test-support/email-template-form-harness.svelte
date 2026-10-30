<script lang="ts">
  import type { EmailTemplateEntry, EmailTemplateVariableEntry } from '@screenhire/api-client';
  import EmailTemplateForm from '#lib/components/email-template-form.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // The email template form as its editor page mounts it, with Save outside it, submitting it through the `form`
  // attribute.
  let {
    template,
    variables,
    sendingEnabled,
    editable = true,
    onsave,
  }: {
    template?: EmailTemplateEntry;
    variables: readonly EmailTemplateVariableEntry[];
    sendingEnabled: boolean;
    editable?: boolean;
    onsave: (value: { name: string | null; subject: string; body: string }) => Promise<unknown>;
  } = $props();

  let mode = $state<'edit' | 'preview'>('edit');
</script>

<Button type="submit" form="email-template-form">Save</Button>
<EmailTemplateForm {template} {variables} {sendingEnabled} {editable} bind:mode formId="email-template-form" failure="Could not save" {onsave} />
