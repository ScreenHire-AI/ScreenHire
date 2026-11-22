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
    /** Its first choice takes the focus when the page loads: the first question of a careers page. Never in the preview. */
    autofocus?: boolean;
  }

  let {
    name,
    legend,
    options,
    selected = $bindable([]),
    multiple = false,
    required = false,
    hint = null,
    error = null,
    autofocus = false,
  }: Props = $props();

  const id = $derived(fieldId(name));

  function toggle(value: string, checked: boolean) {
    selected = multiple ? (checked ? [...selected, value] : selected.filter((item) => item !== value)) : checked ? [value] : [];
  }
</script>

<!-- tabindex -1: the error summary's link focuses the group. -->
<fieldset {id} tabindex="-1" aria-describedby={describedBy(id, hint, error)} class="flex flex-col gap-2">
  <legend class="mb-2 text-candidate-body font-semibold">
    {legend}
    {#if !required}<span class="font-medium text-candidate-muted-foreground"> (optional)</span>{/if}
  </legend>
  {#if hint}<p id={`${id}-hint`} class="-mt-1 text-candidate-hint text-candidate-muted-foreground">{hint}</p>{/if}
  {#if error}<p id={`${id}-error`} class="text-candidate-body font-semibold text-destructive">{error}</p>{/if}
  {#each options as option, index (option.value)}
    <label class="choice">
      <!-- svelte-ignore a11y_autofocus -- only the careers page sets it, for the first question of a page the candidate just moved to (apply-flow.md §7); the preview never does -->
      <input
        autofocus={autofocus && index === 0}
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
