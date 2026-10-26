<script lang="ts">
  import type { Component } from 'svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';

  // Renders a component under a tooltip provider, as the root layout does, for component tests.
  // `any`: the harness takes any component; each test passes the props that component needs.
  let { component: Inner, props = {} }: { component: Component<any>; props?: Record<string, unknown> } = $props();
</script>

<Tooltip.Provider>
  <Inner {...props} />
</Tooltip.Provider>
