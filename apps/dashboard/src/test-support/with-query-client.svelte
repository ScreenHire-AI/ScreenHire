<script lang="ts">
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import { untrack, type Component } from 'svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';

  // Renders a component inside its own query cache and a tooltip provider, as the signed-in shell and root layout do, for component tests. No retries, so
  // a failed request shows its error state at once.
  // `any`: the harness takes any component; each test passes the props that component needs.
  let { component: Inner, props = {}, client }: { component: Component<any>; props?: Record<string, unknown>; client?: QueryClient } =
    $props();

  setQueryClientContext(untrack(() => client) ?? new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } }));
</script>

<Tooltip.Provider>
  <Inner {...props} />
</Tooltip.Provider>
