<script lang="ts">
  import CountryPicker from '#lib/components/country-picker.svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';

  // The picker in a form with its label, as the job forms use it, for component tests. The value shows in a span,
  // and a submit counts, so a test can tell that Enter in the field picks rather than submits.
  let { value = $bindable([]) }: { value?: string[] } = $props();
  let submits = $state(0);
</script>

<Tooltip.Provider>
  <form
    aria-label="Where"
    onsubmit={(event) => {
      event.preventDefault();
      submits += 1;
    }}
  >
    <label for="where-countries">Countries candidates can work from</label>
    <CountryPicker id="where-countries" bind:value />
  </form>
  <span data-testid="value">{value.join(',')}</span>
  <span data-testid="submits">{submits}</span>
</Tooltip.Provider>
