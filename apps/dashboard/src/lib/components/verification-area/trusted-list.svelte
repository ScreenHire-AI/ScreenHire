<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import XIcon from '~icons/lucide/x';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { maxTrustedEntries } from '#lib/verification-settings.js';

  // One of the organization's trusted lists on the Checks tab (verification-engine.md 10.11): the email domains Duplicates
  // trusts and the networks Network trusts. An entry is checked as it is typed, the way the server checks it again on save,
  // and the server's own refusal shows in the same place.
  let {
    id,
    label,
    hint,
    placeholder,
    noun,
    entries = $bindable(),
    problem,
    normalize,
    serverError = $bindable(''),
    numeric = false,
  }: {
    id: string;
    label: string;
    hint: string;
    placeholder: string;
    /** What one entry is, plural: "domains". */
    noun: string;
    entries: string[];
    problem: (value: string) => string | null;
    normalize: (value: string) => string;
    serverError?: string;
    /** Addresses line up in tabular figures. */
    numeric?: boolean;
  } = $props();

  let input = $state('');
  let error = $state('');
  const shown = $derived(error || serverError);

  function add() {
    serverError = '';
    const wrong = problem(input);
    if (wrong) {
      error = wrong;
      return;
    }
    const entry = normalize(input);
    if (entries.length >= maxTrustedEntries && !entries.includes(entry)) {
      error = `At most ${maxTrustedEntries} ${noun}.`;
      return;
    }
    error = '';
    if (!entries.includes(entry)) entries = [...entries, entry];
    input = '';
  }

  function onkeydown(event: KeyboardEvent) {
    if (event.key === 'Enter') {
      event.preventDefault();
      add();
    }
  }
</script>

<Field.Field data-invalid={shown ? true : undefined}>
  <Field.Label for={id}>{label}</Field.Label>
  <Field.Description id={`${id}-hint`}>{hint}</Field.Description>
  {#if entries.length > 0}
    <ul class="flex flex-col gap-1" aria-label={label}>
      {#each entries as entry (entry)}
        <li class="flex items-center justify-between gap-2">
          <span class={['min-w-0 break-all', numeric && 'tabular-nums']}>{entry}</span>
          <Tooltip.Root>
            <Tooltip.Trigger>
              {#snippet child({ props })}
                <Button {...props} type="button" variant="ghost" size="icon" aria-label={`Remove ${entry}`} onclick={() => (entries = entries.filter((other) => other !== entry))}>
                  <XIcon aria-hidden="true" />
                </Button>
              {/snippet}
            </Tooltip.Trigger>
            <Tooltip.Content>Remove {entry}</Tooltip.Content>
          </Tooltip.Root>
        </li>
      {/each}
    </ul>
    <p class="text-muted-foreground tabular-nums">{entries.length} of {maxTrustedEntries} {noun}</p>
  {/if}
  <div class="flex flex-wrap items-start gap-2">
    <div class="min-w-48 flex-1">
      <Input
        {id}
        autocomplete="off"
        spellcheck="false"
        class={numeric ? 'tabular-nums' : undefined}
        {placeholder}
        bind:value={input}
        {onkeydown}
        aria-invalid={shown ? 'true' : undefined}
        aria-describedby={shown ? `${id}-hint ${id}-error` : `${id}-hint`}
      />
    </div>
    <Button type="button" variant="outline" size="lg" onclick={add}><PlusIcon aria-hidden="true" />Add {noun.replace(/s$/, '')}</Button>
  </div>
  <Field.Error id={`${id}-error`} errors={shown ? [{ message: shown }] : undefined} />
</Field.Field>
