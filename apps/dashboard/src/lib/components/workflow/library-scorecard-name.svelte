<script lang="ts">
  import { createListScorecardTemplates } from '@screenhire/api-client/svelte-query';

  // The name of a library scorecard a stage points at, read from the library.
  let { id }: { id: string | null | undefined } = $props();

  const library = createListScorecardTemplates();
  const name = $derived(library.data?.data.find((scorecard) => scorecard.id === id)?.name ?? 'a scorecard from the library');
</script>

{name}
