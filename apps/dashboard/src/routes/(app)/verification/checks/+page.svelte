<script lang="ts">
  import type { CheckGroup } from '@screenhire/api-client';
  import { createGetVerificationOverview } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
  import ChecksSettings from '#lib/components/verification-area/checks-settings.svelte';
  import SettingsTab from '#lib/components/verification-area/settings-tab.svelte';
  import { canEditVerification } from '#lib/verification-area.js';
  import type { PageProps } from './$types';

  // The Checks tab: every switch that decides what runs, and the settings that tune each group inside it. Everyone who sees
  // verification reads it; Edit settings changes it. Each group's count is the Overview's, so the two never disagree. A link
  // to one group's settings names it in the hash (`#answers`), and that row starts open.
  let { data }: PageProps = $props();

  const overview = createGetVerificationOverview();
  const found = $derived.by(() => {
    const result = overview.data?.data;
    if (!result) return null;
    const applications: Partial<Record<CheckGroup, number>> = Object.fromEntries(result.groups.map((group) => [group.group, group.applications]));
    return { applications, checked: result.checked };
  });
</script>

<svelte:head>
  <title>Checks · Verification · ScreenHire</title>
</svelte:head>

<SettingsTab title="Checks" wide>
  {#snippet children(detail)}
    <ChecksSettings {detail} canEdit={canEditVerification(data.user)} {found} opened={page.url.hash.slice(1)} />
  {/snippet}
</SettingsTab>
