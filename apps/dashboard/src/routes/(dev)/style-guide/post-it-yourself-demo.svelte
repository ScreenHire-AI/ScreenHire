<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import PostItYourselfSheet from '#lib/components/post-it-yourself-sheet.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // The Post it yourself sheet for a new posting: the job's text to copy, then where it was posted. The style guide has no
  // API, so this example brings its own query client, and Save posting shows its failure line.
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  setQueryClientContext(client);

  const job = {
    id: 'demo-job',
    title: 'Senior product designer',
    location: 'Remote (United Kingdom)',
    employmentType: 'FullTime',
    salary: null,
    description: '<h2>The role</h2><ul><li>Own the hiring dashboard</li><li>Work with two engineers</li></ul>',
  } as const;

  let open = $state(false);
</script>

<Button variant="outline" aria-haspopup="dialog" onclick={() => (open = true)}><PlusIcon aria-hidden="true" />Post it yourself</Button>
<PostItYourselfSheet bind:open mode={{ kind: 'new' }} {job} jobUrl="https://careers.example.com/brightwater/jobs/senior-product-designer" boardIds={['careers', 'adzuna']} />
