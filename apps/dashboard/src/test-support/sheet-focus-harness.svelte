<script lang="ts">
  import * as Sheet from '#lib/components/ui/sheet/index.js';

  // An open sheet with something to read and, when `field` is set, a field to fill: what the sheet focuses first.
  let { field = false, open = $bindable(true) }: { field?: boolean; open?: boolean } = $props();
</script>

<Sheet.Root bind:open>
  <Sheet.Content>
    <Sheet.Header>
      <Sheet.Title>Screening scorecard</Sheet.Title>
      <Sheet.Description>Jordan Ellis, Recruiter.</Sheet.Description>
    </Sheet.Header>
    {#if field}<label>Note <input name="note" /></label>{/if}
    <p>Early in their career, with a shipped web shop to show.</p>
  </Sheet.Content>
</Sheet.Root>
