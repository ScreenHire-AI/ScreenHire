<script lang="ts">
  import ChecksForm from '#lib/components/verification-area/checks-form.svelte';
  import SettingsTab from '#lib/components/verification-area/settings-tab.svelte';
  import { canEditVerification } from '#lib/verification-area.js';
  import type { PageProps } from './$types';

  // The Checks tab: which groups of checks run. Everyone who sees verification reads it; Edit settings changes it.
  let { data }: PageProps = $props();
</script>

<svelte:head>
  <title>Checks · Verification · ScreenHire</title>
</svelte:head>

<SettingsTab title="Checks">
  {#snippet children(detail)}
    <ChecksForm {detail} canEdit={canEditVerification(data.user)} />
  {/snippet}
</SettingsTab>
