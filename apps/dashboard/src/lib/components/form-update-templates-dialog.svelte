<script lang="ts">
  import RefreshCwIcon from '~icons/lucide/refresh-cw';
  import type { FormTemplateUpdate, UsageItem } from '@screenhire/api-client';
  import { createApplyFormToTemplates } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import { Checkbox } from '#lib/components/ui/checkbox/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { templatesPath } from '#lib/realtime.js';
  import { formTemplatesPath, templateUsagePath } from '#lib/templates.js';
  import { cn } from '#lib/utils.js';

  // After a library form is published (workflow-engine.md section 7, Forms): the hiring templates on an older version, all
  // ticked, and what updating does before it happens (A11). A template with no draft gets a new published version with the
  // form's new questions, which new jobs get; one with a draft has the draft changed. Jobs keep theirs until someone
  // updates them. The result says what happened to each.
  let {
    open = $bindable(false),
    formId,
    version,
    formName,
    templates,
    ondone,
  }: {
    open?: boolean;
    formId: string;
    version: number;
    formName: string;
    /** The hiring templates whose current versions hold an older version of the form. */
    templates: readonly UsageItem[];
    ondone?: (results: readonly FormTemplateUpdate[]) => void;
  } = $props();

  const queryClient = useQueryClient();
  const apply = createApplyFormToTemplates(() => ({
    mutation: {
      onSettled: () =>
        Promise.all([invalidatePath(queryClient, templatesPath), invalidatePath(queryClient, templateUsagePath), invalidatePath(queryClient, formTemplatesPath)]),
    },
  }));

  let chosen = $state<string[]>([]);
  let failure = $state('');
  $effect(() => {
    if (open) {
      chosen = templates.map((template) => template.id);
      failure = '';
    }
  });

  const count = $derived(chosen.length);

  async function update() {
    failure = '';
    try {
      const response = await apply.mutateAsync({ formId, version, data: { templateIds: chosen } });
      open = false;
      ondone?.(response.data.templates);
    } catch (error) {
      failure = describeError(error, 'Could not update the templates');
    }
  }
</script>

<Dialog.Root bind:open>
  <Dialog.Content class="sm:max-w-lg">
    <Dialog.Header>
      <Dialog.Title>Update the templates that use {formName}?</Dialog.Title>
      <Dialog.Description>
        Each one takes version {version}. A template without a draft is published again with the new questions, so new jobs
        get them; a template with a draft has its draft changed. Jobs stay on their version until you update them.
      </Dialog.Description>
    </Dialog.Header>
    <fieldset class="flex flex-col gap-2">
      <legend class="sr-only">Hiring templates to update</legend>
      {#each templates as template (template.id)}
        <label class="flex items-center gap-2 pointer-coarse:min-h-11">
          <Checkbox
            checked={chosen.includes(template.id)}
            onCheckedChange={(on) => (chosen = on ? [...chosen, template.id] : chosen.filter((id) => id !== template.id))}
          />
          <span>{template.name}</span>
          <span class="text-muted-foreground">on version {template.templateVersion}</span>
        </label>
      {/each}
    </fieldset>
    <FailureText message={failure} />
    <Dialog.Footer>
      <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Not now</Dialog.Close>
      <Button size="lg" disabled={count === 0 || apply.isPending} onclick={update}>
        <RefreshCwIcon aria-hidden="true" />Update {count === 1 ? '1 template' : `${count} templates`}
      </Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
