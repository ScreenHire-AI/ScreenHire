<script lang="ts">
  import { untrack } from 'svelte';
  import SaveIcon from '~icons/lucide/save';
  import type { CheckGroup, OrganizationVerificationSettings } from '@screenhire/api-client';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { describeError } from '#lib/query.js';
  import { checkGroupHints, checkGroupLabels } from '#lib/verification-settings.js';
  import { createVerificationSettingsSave, savedMessage } from '#lib/verification-settings-save.js';

  // The Checks tab (verification-engine.md 10.11): the six groups of checks, each with one line on what it looks at. Whoever
  // can see verification reads which are on; whoever can edit settings also gets the switches and Save (A3: a role that cannot
  // change a group is not shown a disabled switch). A group that is off records that it did not run, so nobody reads a
  // missing check as clean.
  let { detail, canEdit }: { detail: OrganizationVerificationSettings; canEdit: boolean } = $props();

  const groups = Object.keys(checkGroupLabels) as CheckGroup[];
  const keys: Record<CheckGroup, 'network' | 'contact' | 'documents' | 'answers' | 'duplicates' | 'formUse'> = {
    Network: 'network',
    Contact: 'contact',
    Documents: 'documents',
    Answers: 'answers',
    Duplicates: 'duplicates',
    FormUse: 'formUse',
  };

  const schema = z.object({
    checks: z.object({ network: z.boolean(), contact: z.boolean(), documents: z.boolean(), answers: z.boolean(), duplicates: z.boolean(), formUse: z.boolean() }),
  });
  const valueOf = (group: CheckGroup) => detail.checks[keys[group]] ?? true;

  const save = createVerificationSettingsSave();
  let saved = $state('');
  const { form, errors, enhance, submitting } = superForm(
    defaults(
      untrack(() => ({
        checks: {
          network: valueOf('Network'),
          contact: valueOf('Contact'),
          documents: valueOf('Documents'),
          answers: valueOf('Answers'),
          duplicates: valueOf('Duplicates'),
          formUse: valueOf('FormUse'),
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
          await save(detail, { checks: submitted.data.checks });
          saved = savedMessage;
        } catch (error) {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the settings')];
        }
      },
    },
  );
</script>

{#if canEdit}
  <form method="POST" use:enhance novalidate aria-label="Checks settings" class="flex flex-col gap-5">
    <Panel title="Checks">
      <div class="flex flex-col gap-5">
        <p class="text-muted-foreground">A group that is off records that it did not run, so nobody reads a missing check as clean.</p>
        {#each groups as group (group)}
          {@const key = keys[group]}
          <div class="flex items-start gap-4">
            <div class="flex min-w-0 flex-1 flex-col gap-1">
              <Field.Label for={`check-${key}`}>{checkGroupLabels[group]}</Field.Label>
              <Field.Description id={`check-${key}-hint`}>{checkGroupHints[group]}</Field.Description>
            </div>
            <Switch id={`check-${key}`} bind:checked={$form.checks[key]} aria-describedby={`check-${key}-hint`} class="mt-1" />
          </div>
        {/each}
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
{:else}
  <Panel title="Checks">
    <div class="flex flex-col gap-5">
      <p class="text-muted-foreground">A group that is off records that it did not run, so nobody reads a missing check as clean.</p>
      <ul class="flex flex-col gap-5" aria-label="Check groups">
        {#each groups as group (group)}
          <li class="flex items-start gap-4">
            <div class="flex min-w-0 flex-1 flex-col gap-1">
              <span class="text-body font-medium">{checkGroupLabels[group]}</span>
              <span class="text-muted-foreground">{checkGroupHints[group]}</span>
            </div>
            {#if valueOf(group)}
              <StatusBadge status="done" label="On" quiet />
            {:else}
              <StatusBadge status="notInEffect" label="Off" />
            {/if}
          </li>
        {/each}
      </ul>
    </div>
  </Panel>
{/if}
