<script lang="ts">
  import SlidersIcon from '~icons/lucide/sliders-horizontal';
  import type { PostingCustomization } from '@screenhire/api-client';
  import { getGetPostingCustomizationQueryKey } from '@screenhire/api-client/svelte-query';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import CustomizePostingSheet from '#lib/components/customize-posting-sheet.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // The Customize sheet for a board whose job changed since: the style guide has no API, so this example brings its own
  // query client, and Preview and Save and post show their failure lines. The sheet drops its query each time it closes,
  // so the sample goes back in each time it opens.
  const client = new QueryClient({ defaultOptions: { queries: { staleTime: Infinity, retry: false }, mutations: { retry: false } } });
  setQueryClientContext(client);

  const jobId = 'demo-job';
  const boardId = 'adzuna';
  const customization: PostingCustomization = {
    boardId,
    boardName: 'Adzuna',
    jobVersion: 2,
    title: { override: 'Product designer (remote)', job: 'Senior product designer' },
    description: { override: null, job: '<p>Join Brightwater Studio as our senior product designer.</p>' },
    changes: [{ field: 'Title', before: 'Product designer', after: 'Senior product designer' }],
  };

  let open = $state(false);
  function show() {
    client.setQueryData(getGetPostingCustomizationQueryKey(jobId, boardId), { status: 200, headers: new Headers(), data: customization });
    open = true;
  }
</script>

<Button variant="outline" aria-haspopup="dialog" onclick={show}><SlidersIcon aria-hidden="true" />Customize</Button>
<CustomizePostingSheet bind:open {jobId} {boardId} boardName="Adzuna" />
