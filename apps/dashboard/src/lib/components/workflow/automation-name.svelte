<script lang="ts">
  import type { Automation, DefinitionError } from '@screenhire/api-client';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { errorsAt } from '#lib/workflow.js';
  import FieldErrors from './field-errors.svelte';
  import { labelClass } from './controls.js';

  // An automation's Name and On, above its Sentence and Rules tabs, so both tabs edit the same two controls and switching
  // tabs never moves them. The Name field carries the automation's path in `data-focus`, so opening an automation from the
  // map or a list row starts there.
  let { automation = $bindable(), index, errors }: { automation: Automation; index: number; errors: readonly DefinitionError[] } = $props();

  const uid = $props.id();
  const path = $derived(`automations[${index}]`);
  const nameErrors = $derived(errorsAt(errors, `${path}.name`));
</script>

<div class="flex flex-col gap-1">
  <div class="grid items-end gap-2.5 @md:grid-cols-[1fr_auto]">
    <label class={labelClass}>
      Name
      <Input
        bind:value={automation.name}
        maxlength={120}
        data-focus={path}
        aria-invalid={nameErrors.length > 0 || undefined}
        aria-describedby={nameErrors.length > 0 ? `${uid}-name-errors` : undefined}
      />
    </label>
    <label class="flex h-9 items-center gap-2 text-body font-semibold pointer-coarse:h-11">
      <Switch checked={automation.enabled !== false} onCheckedChange={(on) => (automation.enabled = on)} />
      On
    </label>
  </div>
  <FieldErrors id={`${uid}-name-errors`} messages={nameErrors} />
</div>
