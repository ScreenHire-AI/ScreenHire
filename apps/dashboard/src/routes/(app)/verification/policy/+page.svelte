<script lang="ts">
  import PolicyForm from '#lib/components/verification-area/policy-form.svelte';
  import SettingsTab from '#lib/components/verification-area/settings-tab.svelte';
  import { canEditVerification } from '#lib/verification-area.js';
  import type { PageProps } from './$types';

  // The Policy tab: the organization's AI-use policy, the device check and the trusted sources. Everyone who sees
  // verification reads it; Edit settings changes it.
  let { data }: PageProps = $props();
</script>

<svelte:head>
  <title>Policy · Verification · ScreenHire</title>
</svelte:head>

<SettingsTab title="Policy">
  {#snippet children(detail)}
    <PolicyForm {detail} canEdit={canEditVerification(data.user)} />
  {/snippet}
</SettingsTab>
