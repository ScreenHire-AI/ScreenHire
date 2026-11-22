<script lang="ts">
  import { untrack } from 'svelte';
  import SaveIcon from '~icons/lucide/save';
  import type { OrganizationVerificationSettings } from '@screenhire/api-client';
  import { getGetAccountNotificationsQueryKey } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import AlertGrid, { type AlertRow } from '#lib/components/alert-grid.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { describeError } from '#lib/query.js';
  import { createVerificationSettingsSave } from '#lib/verification-settings-save.js';

  // The organization's notification defaults (docs/architecture/notifications.md, "Each person's choices"): where everyone starts
  // for the two alerts that reach people by email and Slack as well as the bell, until they choose for themselves in Your
  // account > Notifications. The bell always gets both. They are part of the verification settings, so the save lays these four
  // choices over the rest as saved, and the people's own grids, which start from them, are read again.
  let { detail }: { detail: OrganizationVerificationSettings } = $props();

  type Key = 'suggestedEmail' | 'suggestedSlack' | 'budgetEmail' | 'budgetSlack';
  const rows: AlertRow<Key>[] = [
    {
      label: 'Verification suggested',
      about: 'Tells the reviewers who see the job when an application reaches Verify. It names the job, never the candidate or why.',
      inApp: 'The candidate joins To verify, and the sidebar counts them.',
      email: 'suggestedEmail',
      slack: 'suggestedSlack',
    },
    {
      label: 'AI usage',
      about:
        'Tells people who can see AI usage when the monthly budget passes 50%, 80% and 100%, when the cap is reached, and after an unusually busy day.',
      inApp: 'The bell tells everyone who can see AI usage.',
      email: 'budgetEmail',
      slack: 'budgetSlack',
    },
  ];

  const schema = z.object({
    alerts: z.object({ suggestedEmail: z.boolean(), suggestedSlack: z.boolean(), budgetEmail: z.boolean(), budgetSlack: z.boolean() }),
  });
  const queryClient = useQueryClient();
  const save = createVerificationSettingsSave();
  let saved = $state('');
  const { form, errors, enhance, submitting } = superForm(
    defaults(
      untrack(() => ({
        alerts: {
          suggestedEmail: detail.alerts.suggestedEmail ?? false,
          suggestedSlack: detail.alerts.suggestedSlack ?? false,
          budgetEmail: detail.alerts.budgetEmail ?? false,
          budgetSlack: detail.alerts.budgetSlack ?? false,
        },
      })),
      zod4(schema),
    ),
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
          await save(detail, { alerts: submitted.data.alerts });
          await queryClient.invalidateQueries({ queryKey: getGetAccountNotificationsQueryKey(), exact: true });
          saved = 'Saved. People who have not chosen for themselves start from these.';
        } catch (error) {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the notification defaults')];
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
    <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save</Button>
  </div>
{/snippet}

<form method="POST" use:enhance novalidate aria-label="Notification defaults" class="flex flex-col gap-5">
  <Panel title="Alerts" flush footer={detail.emailAvailable || detail.slackConnected ? saveRow : undefined}>
    <p class="border-b border-border-subtle px-4.5 py-3 text-muted-foreground max-md:px-4">
      Where everyone starts. Each person can change their own in <a href="/account/notifications" class="text-foreground underline underline-offset-2">Your account</a>.
    </p>
    <AlertGrid {rows} bind:values={$form.alerts} emailAvailable={detail.emailAvailable} slackConnected={detail.slackConnected} label="Notification defaults" />
  </Panel>
</form>
