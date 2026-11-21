<script lang="ts">
  import { untrack } from 'svelte';
  import SaveIcon from '~icons/lucide/save';
  import type { OrganizationVerificationSettings } from '@screenhire/api-client';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import AlertGrid, { type AlertRow } from '#lib/components/alert-grid.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { describeError } from '#lib/query.js';
  import { createVerificationSettingsSave } from '#lib/verification-settings-save.js';

  // The AI usage alert on the AI use tab (docs/architecture/ai-usage.md, "Budget, alerts and the cap"): the bell always gets
  // it; email and Slack add a message. It saves only these two choices, laying the Verification suggested alert over as saved.
  let { detail }: { detail: OrganizationVerificationSettings } = $props();

  const row: AlertRow<'budgetEmail' | 'budgetSlack'> = {
    label: 'AI usage',
    about:
      'Tells people who can see AI usage when the monthly budget passes 50%, 80% and 100%, when the cap is reached, and after an unusually busy day.',
    inApp: 'The bell tells everyone who can see AI usage.',
    email: 'budgetEmail',
    slack: 'budgetSlack',
  };

  const schema = z.object({ alerts: z.object({ budgetEmail: z.boolean(), budgetSlack: z.boolean() }) });
  const save = createVerificationSettingsSave();
  let saved = $state('');
  const { form, errors, enhance, submitting } = superForm(
    defaults(untrack(() => ({ alerts: { budgetEmail: detail.alerts.budgetEmail ?? false, budgetSlack: detail.alerts.budgetSlack ?? false } })), zod4(schema)),
    {
      SPA: true,
      dataType: 'json',
      validationMethod: 'submit-only',
      validators: zod4(schema),
      resetForm: false,
      applyAction: false,
      invalidateAll: false,
      async onUpdate({ form: submitted }) {
        saved = '';
        if (!submitted.valid) return;
        try {
          await save(detail, {
            alerts: {
              suggestedEmail: detail.alerts.suggestedEmail ?? false,
              suggestedSlack: detail.alerts.suggestedSlack ?? false,
              budgetEmail: submitted.data.alerts.budgetEmail,
              budgetSlack: submitted.data.alerts.budgetSlack,
            },
          });
          saved = 'Saved.';
        } catch (error) {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the alert')];
        }
      },
    },
  );
</script>

<!-- With no email sender and no Slack, the bell is the only way and there is nothing to save, so there is no Save either. -->
{#snippet saveRow()}
  <div class="flex flex-wrap items-center justify-end gap-2">
    <FailureText message={$errors._errors?.[0]} class="mr-auto" />
    <p role="status" class="mr-auto text-muted-foreground empty:hidden">{$errors._errors ? '' : saved}</p>
    <Button type="submit" variant="outline" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save alert</Button>
  </div>
{/snippet}

<form method="POST" use:enhance novalidate aria-label="AI usage alert">
  <Panel title="Notifications" flush footer={detail.emailAvailable || detail.slackConnected ? saveRow : undefined}>
    <AlertGrid rows={[row]} bind:values={$form.alerts} emailAvailable={detail.emailAvailable} slackConnected={detail.slackConnected} label="AI usage alert" />
  </Panel>
</form>
