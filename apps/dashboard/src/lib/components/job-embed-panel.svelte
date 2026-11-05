<script lang="ts">
  import { createGetOrganization } from '@screenhire/api-client/svelte-query';
  import CodeBlock from '#lib/components/code-block.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { embedSnippet } from '#lib/embed-snippet.js';
  import { describeError } from '#lib/query.js';

  // The snippet that shows one job in a frame on the company's own site (careers-widget.md): the organization snippet
  // plus the job's address. Like that snippet it appears once at least one site may embed the careers page, because the
  // browser refuses the frame everywhere else; until then the panel says where to add the site.
  let { slug }: { slug: string } = $props();

  const organization = createGetOrganization();
  const detail = $derived(organization.data?.data);
</script>

{#if organization.isPending}
  <!-- The panel's shape while the organization loads, so the page does not shift when it arrives (E1). -->
  <Panel title="Embed on your website">
    <div aria-hidden="true" class="flex flex-col gap-2">
      <Skeleton class="h-16 w-full" />
      <Skeleton class="h-8 w-20" />
    </div>
  </Panel>
{:else if detail}
  <Panel title="Embed on your website">
    <div class="flex flex-col gap-2">
      {#if detail.embedOrigins.length > 0}
        <CodeBlock code={embedSnippet(detail.careersUrl, slug)} label="Embed code for this job" />
        <p class="text-muted-foreground">
          Paste it where this job should show. The code for every job is on
          <a href="/settings/careers-site" class="font-semibold text-primary underline underline-offset-2">Careers site settings</a>.
        </p>
      {:else}
        <p class="text-muted-foreground">
          Add your website on
          <a href="/settings/careers-site" class="font-semibold text-primary underline underline-offset-2">Careers site settings</a>
          to get this job's embed code.
        </p>
      {/if}
    </div>
  </Panel>
{:else if organization.isError}
  <Panel title="Embed on your website">
    <QueryError message={describeError(organization.error, 'Could not load the embed code')} onretry={() => organization.refetch()} inset />
  </Panel>
{/if}
