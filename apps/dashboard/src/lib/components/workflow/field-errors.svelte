<script lang="ts">
  // The validator's messages for one part of the workflow, under the control they are about. `id` is for the control's
  // aria-describedby.
  let { id, messages }: { id: string; messages: readonly string[] } = $props();
</script>

{#if messages.length > 0}
  <ul {id} class="flex flex-col gap-0.5 text-body font-semibold text-destructive">
    {#each messages as message (message)}<li>{message}</li>{/each}
  </ul>
{/if}
