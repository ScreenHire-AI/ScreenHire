<script lang="ts">
  import type { OrganizationVerificationSettings } from '@screenhire/api-client';
  import { createGetOrganizationVerificationSettings } from '@screenhire/api-client/svelte-query';
  import type { Snippet } from 'svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';

  // The 688px column of a settings tab (design system section 7, Form) around the organization's saved verification
  // settings: a skeleton while they load, an error with Retry, then `children` with the settings. The layout reads the same
  // query for the Last updated fact, so this adds no request.
  let { title, children }: { title: string; children: Snippet<[OrganizationVerificationSettings]> } = $props();

  const settings = createGetOrganizationVerificationSettings();
  const detail = $derived(settings.data?.data);
</script>

<div class="flex w-full max-w-form flex-col gap-5">
  {#if settings.isError && !detail}
    <Panel {title}>
      <QueryError message={describeError(settings.error, 'Could not load the verification settings')} onretry={() => settings.refetch()} inset />
    </Panel>
  {:else if !detail}
    <Panel {title}>
      <div aria-hidden="true" class="flex flex-col gap-4"><Skeleton class="h-10 w-full" /><Skeleton class="h-14 w-full" /></div>
      <p class="sr-only" role="status">Loading the verification settings…</p>
    </Panel>
  {:else}
    {@render children(detail)}
  {/if}
</div>
