<script lang="ts">
  import { dev } from '$app/env';

  // The style guide is a development tool, and a production build answers 404 here (`+page.ts`). Its catalogue imports
  // every component, about 1 MB of JavaScript, so only development loads it: the production build leaves the import out,
  // and its 404 page does not wait for the whole catalogue to download and parse first.
  const guide = dev ? import('./style-guide.svelte') : undefined;
</script>

{#if guide}
  {#await guide then module}<module.default />{/await}
{/if}
