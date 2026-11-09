<script lang="ts">
  import { untrack } from 'svelte';
  import SaveIcon from '~icons/lucide/save';
  import type { OrganizationVerificationSettings } from '@screenhire/api-client';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { checkboxClass } from '#lib/components/workflow/controls.js';
  import { describeError } from '#lib/query.js';
  import { createVerificationSettingsSave, savedMessage } from '#lib/verification-settings-save.js';

  // The two alerts of the Notifications tab (verification-engine.md 10.11), each by email, by Slack or both. People always get
  // the bell; these add a message. Neither carries a level, a reason or a candidate's name.
  let { detail }: { detail: OrganizationVerificationSettings } = $props();

  const schema = z.object({
    alerts: z.object({ suggestedEmail: z.boolean(), suggestedSlack: z.boolean(), budgetEmail: z.boolean(), budgetSlack: z.boolean() }),
  });

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
          saved = savedMessage;
        } catch (error) {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the settings')];
        }
      },
    },
  );
</script>

<form method="POST" use:enhance novalidate aria-label="Alert settings" class="flex flex-col gap-5">
  <Panel title="Alerts">
    <div class="flex flex-col gap-5">
      <fieldset class="flex flex-col gap-2">
        <legend class="mb-1 text-body font-medium">Verification suggested</legend>
        <p class="text-muted-foreground" id="alert-suggested-hint">Tells reviewers who see the job. It names the job, never the candidate or why.</p>
        {#if detail.emailAvailable}
          <label class="flex min-w-0 items-center gap-2 pointer-coarse:min-h-11">
            <input type="checkbox" name="suggestedEmail" bind:checked={$form.alerts.suggestedEmail} aria-describedby="alert-suggested-hint" class={checkboxClass} />
            By email
          </label>
        {/if}
        {#if detail.slackConnected}
          <label class="flex min-w-0 items-center gap-2 pointer-coarse:min-h-11">
            <input type="checkbox" name="suggestedSlack" bind:checked={$form.alerts.suggestedSlack} aria-describedby="alert-suggested-hint" class={checkboxClass} />
            On Slack, as a direct message
          </label>
        {/if}
      </fieldset>

      <fieldset class="flex flex-col gap-2">
        <legend class="mb-1 text-body font-medium">AI usage</legend>
        <p class="text-muted-foreground" id="alert-budget-hint">
          Tells people who can see AI usage when the monthly budget passes 50%, 80% and 100%, when the cap is reached, and after an unusually busy day.
        </p>
        {#if detail.emailAvailable}
          <label class="flex min-w-0 items-center gap-2 pointer-coarse:min-h-11">
            <input type="checkbox" name="budgetEmail" bind:checked={$form.alerts.budgetEmail} aria-describedby="alert-budget-hint" class={checkboxClass} />
            By email
          </label>
        {/if}
        {#if detail.slackConnected}
          <label class="flex min-w-0 items-center gap-2 pointer-coarse:min-h-11">
            <input type="checkbox" name="budgetSlack" bind:checked={$form.alerts.budgetSlack} aria-describedby="alert-budget-hint" class={checkboxClass} />
            On Slack, as a direct message
          </label>
        {/if}
      </fieldset>

      {#if !detail.slackConnected}
        <p class="text-muted-foreground">
          Slack is not connected. <a href="/settings/slack" class="inline-flex min-h-6 items-center underline underline-offset-2 pointer-coarse:min-h-11">Connect it in Settings</a> to send direct messages.
        </p>
      {/if}
    </div>
    {#snippet footer()}
      <div class="flex flex-wrap items-center justify-end gap-2">
        <FailureText message={$errors._errors?.[0]} class="mr-auto" />
        <p role="status" class="mr-auto text-muted-foreground empty:hidden">{$errors._errors ? '' : saved}</p>
        <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save</Button>
      </div>
    {/snippet}
  </Panel>
</form>
