<script lang="ts">
  import { createGetOrganizationVerificationSettings, getGetOrganizationVerificationSettingsHistoryQueryOptions } from '@screenhire/api-client/svelte-query';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistoryFact from '#lib/components/history-fact.svelte';
  import NotificationDefaultsForm from '#lib/components/notification-defaults-form.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';
  import { settingsCrumbs } from '#lib/settings.js';

  // Settings > Organization > Notification defaults (docs/architecture/notifications.md): the organization's starting choices for
  // the alerts that reach people by email and Slack, in one place rather than inside the feature each is about. For Edit settings
  // (the settings layout refuses anyone else, as the API does). A person's own choices are Your account > Notifications.
  const settings = createGetOrganizationVerificationSettings();
  const detail = $derived(settings.data?.data);
</script>

<svelte:head>
  <title>Notification defaults · ScreenHire</title>
</svelte:head>

<TopBar crumbs={settingsCrumbs('/settings/notification-defaults')} />

<main class="flex flex-col">
  <HeaderBand title="Notification defaults" oneRow>
    {#snippet actions()}
      {#if detail}
        <HistoryFact value={detail.lastUpdated} name="Verification settings" page={(before) => getGetOrganizationVerificationSettingsHistoryQueryOptions({ before })} />
      {/if}
    {/snippet}
  </HeaderBand>

  <SettingsMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
      {#if settings.isError && !detail}
        <Panel title="Alerts">
          <QueryError message={describeError(settings.error, 'Could not load the notification defaults')} onretry={() => settings.refetch()} inset />
        </Panel>
      {:else if !detail}
        <Panel title="Alerts">
          <div aria-hidden="true" class="flex flex-col gap-3"><Skeleton class="h-10 w-full" /><Skeleton class="h-10 w-full" /></div>
          <p class="sr-only" role="status">Loading the notification defaults…</p>
        </Panel>
      {:else}
        <NotificationDefaultsForm {detail} />
      {/if}
    </div>
  </SettingsMenu>
</main>
