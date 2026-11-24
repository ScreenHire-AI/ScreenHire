<script lang="ts">
  import type { Automation, DefinitionError } from '@screenhire/api-client';
  import PencilIcon from '~icons/lucide/pencil';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { maxAutomationName } from '#lib/automation-sentence.js';
  import { errorsAt } from '#lib/workflow.js';
  import FieldErrors from './field-errors.svelte';

  // An automation's name as the sheet's title, edited in place, with its On switch beside it: one of each above the Sentence
  // and Rules tabs. The pencil and the edge say it can be edited before it is hovered (A13). A name follows the trigger and
  // the steps until a person types one (`suggestedName`, kept by the editor), and one left empty goes back to following them.
  let {
    automation = $bindable(),
    index,
    errors,
    suggested,
  }: {
    automation: Automation;
    index: number;
    errors: readonly DefinitionError[];
    /** The name the automation has until a person types one. */
    suggested: string;
  } = $props();

  const uid = $props.id();
  const path = $derived(`automations[${index}]`);
  const nameErrors = $derived(errorsAt(errors, `${path}.name`));
</script>

<div class="flex flex-col gap-1">
  <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
    <div class="relative min-w-0 flex-[1_1_16rem]">
      <Input
        bind:value={automation.name}
        aria-label="Name"
        maxlength={maxAutomationName}
        data-focus={`${path}.name`}
        class="truncate border-border-subtle pr-8 text-heading md:text-heading"
        aria-invalid={nameErrors.length > 0 || undefined}
        aria-describedby={nameErrors.length > 0 ? `${uid}-name-errors` : undefined}
        onblur={() => {
          if (automation && !automation.name.trim()) automation.name = suggested;
        }}
        onkeydown={(event) => {
          if (event.key === 'Enter') event.currentTarget.blur();
        }}
      />
      <PencilIcon aria-hidden="true" class="pointer-events-none absolute top-1/2 right-2.5 size-4 -translate-y-1/2 text-muted-foreground" />
    </div>
    <label class="flex h-9 shrink-0 items-center gap-2 text-body font-semibold pointer-coarse:h-11">
      <Switch checked={automation.enabled !== false} onCheckedChange={(on) => (automation.enabled = on)} />
      On
    </label>
  </div>
  <FieldErrors id={`${uid}-name-errors`} messages={nameErrors} />
</div>
