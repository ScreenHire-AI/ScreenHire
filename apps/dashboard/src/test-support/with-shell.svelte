<script lang="ts">
  import type { Component } from 'svelte';
  import type { SettingsFeatures } from '#lib/settings.js';
  import { setShell } from '#lib/shell.js';

  // Renders a component inside a shell that reports the given optional features, as the (app) layout does, for tests of
  // what depends on them (the Settings menu and layout). `any`: the harness takes any component.
  let {
    component: Inner,
    props = {},
    features = {},
    openCalendar = () => {},
  }: { component: Component<any>; props?: Record<string, unknown>; features?: SettingsFeatures; openCalendar?: () => void } = $props();

  setShell({
    openPalette: () => {},
    openShortcuts: () => {},
    openNotifications: () => {},
    openCalendar: () => openCalendar(),
    live: () => 'connected',
    features: () => features,
  });
</script>

<Inner {...props} />
