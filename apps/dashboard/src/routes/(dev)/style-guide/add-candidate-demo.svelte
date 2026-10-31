<script lang="ts">
  import UserPlusIcon from '@lucide/svelte/icons/user-plus';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import AddCandidateSheet from '#lib/components/add-candidate-sheet.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // The Add candidate sheet as a job page opens it, with its job preset: the style guide has no API, so this example
  // brings its own query client, and Add candidate shows its failure line.
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  setQueryClientContext(client);

  let open = $state(false);
</script>

<Button variant="outline" aria-haspopup="dialog" onclick={() => (open = true)}><UserPlusIcon aria-hidden="true" />Add candidate</Button>
<AddCandidateSheet bind:open job={{ id: 'demo-job', title: 'Product designer' }} />
