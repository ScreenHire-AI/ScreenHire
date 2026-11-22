<script lang="ts">
  import { createGetOrganizationVerificationSettings, getGetOrganizationVerificationSettingsHistoryQueryOptions } from '@screenhire/api-client/svelte-query';
  import AiModelForm from '#lib/components/ai-model-form.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistoryFact from '#lib/components/history-fact.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';
  import { settingsCrumbs } from '#lib/settings.js';

  // Settings > Integrations > AI model (verification-engine.md 10.11): whose key runs the AI check, ScreenHire's included model or
  // the organization's own provider, key and model. An integration like Slack or a calendar, for Edit settings (the settings
  // layout refuses anyone else, as the API does). What the checks cost on either key is Billing's AI use section; whether the
  // check runs at all is "Compare each application's sources" on Verification's Checks tab. The key is part of the verification
  // settings, so Last updated and History are theirs.
  const settings = createGetOrganizationVerificationSettings();
  const detail = $derived(settings.data?.data);
</script>

<svelte:head>
  <title>AI model · ScreenHire</title>
</svelte:head>

<TopBar crumbs={settingsCrumbs('/settings/ai-model')} />

<main class="flex flex-col">
  <HeaderBand title="AI model" oneRow>
    {#snippet actions()}
      {#if detail}
        <HistoryFact value={detail.lastUpdated} name="Verification settings" page={(before) => getGetOrganizationVerificationSettingsHistoryQueryOptions({ before })} />
      {/if}
    {/snippet}
  </HeaderBand>

  <SettingsMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
      {#if settings.isError && !detail}
        <Panel title="Whose key runs the AI check">
          <QueryError message={describeError(settings.error, 'Could not load the AI model')} onretry={() => settings.refetch()} inset />
        </Panel>
      {:else if !detail}
        <Panel title="Whose key runs the AI check">
          <div aria-hidden="true" class="flex flex-col gap-4"><Skeleton class="h-14 w-full" /><Skeleton class="h-10 w-full" /></div>
          <p class="sr-only" role="status">Loading the AI model…</p>
        </Panel>
      {:else}
        <AiModelForm {detail} />
      {/if}
    </div>
  </SettingsMenu>
</main>
