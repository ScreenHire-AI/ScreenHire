<script lang="ts">
  import { goto } from '$app/navigation';
  import { visibleSettingsSections } from '#lib/settings.js';
  import { getShell } from '#lib/shell.js';
  import type { PageProps } from './$types';

  // Settings has no page of its own (design system section 11): its address, the sidebar's Settings item, opens the first
  // settings page the person's role may use (General for an Owner), whose menu lists every other one. Someone whose role
  // may use none is answered by the layout.
  let { data }: PageProps = $props();

  const shell = getShell();
  $effect(() => {
    const first = visibleSettingsSections(data.user, shell?.features())[0];
    if (first) void goto(first.href, { replaceState: true });
  });
</script>

<svelte:head>
  <title>Settings · ScreenHire</title>
</svelte:head>
