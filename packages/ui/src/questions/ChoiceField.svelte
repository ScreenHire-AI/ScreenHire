<script lang="ts">
  // Radios or checkboxes in a fieldset: yes/no, single choice and multiple choice questions. `selected` binds in the
  // dashboard preview; on the careers page it is the server's re-render of what the candidate chose.
  import { describedBy, fieldId } from './model';

  interface Props {
    name: string;
    legend: string;
    options: { value: string; label: string }[];
    selected?: string[];
    multiple?: boolean;
    required?: boolean;
    hint?: string | null;
    error?: string | null;
  }

  let { name, legend, options, selected = $bindable([]), multiple = false, required = false, hint = null, error = null }: Props = $props();

  const id = $derived(fieldId(name));

  function toggle(value: string, checked: boolean) {
    selected = multiple ? (checked ? [...selected, value] : selected.filter((item) => item !== value)) : checked ? [value] : [];
  }
</script>

<!-- tabindex -1: the error summary's link focuses the group. -->
<fieldset {id} tabindex="-1" aria-describedby={describedBy(id, hint, error)} class="flex flex-col gap-2">
  <legend class="mb-1.5 text-sm font-bold">
    {legend}
    {#if !required}<span class="font-medium text-candidate-muted-foreground"> (optional)</span>{/if}
  </legend>
  {#if hint}<p id={`${id}-hint`} class="-mt-1 text-candidate-hint text-candidate-muted-foreground">{hint}</p>{/if}
  {#if error}<p id={`${id}-error`} class="text-sm font-semibold text-destructive">{error}</p>{/if}
  {#each options as option (option.value)}
    <label class="choice">
      <input
        type={multiple ? 'checkbox' : 'radio'}
        {name}
        value={option.value}
        checked={selected.includes(option.value)}
        aria-invalid={error ? 'true' : undefined}
        onchange={(event) => toggle(option.value, event.currentTarget.checked)}
      />
      <span>{option.label}</span>
    </label>
  {/each}
</fieldset>
