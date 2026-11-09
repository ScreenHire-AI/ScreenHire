<script lang="ts">
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { can, seesAllJobs } from '#lib/permissions.js';
  import { featureWaits, settingsCrumbs, settingsSectionOf, visibleSettingsSections } from '#lib/settings.js';
  import { getShell } from '#lib/shell.js';
  import type { LayoutProps } from './$types';

  // Each settings page needs the permission its changes need (the API's RequirePermission, ADR-0014). The sidebar offers
  // only the pages the person's role may use; this answers anyone who opens another by its address, instead of a page of
  // refused requests.
  let { data, children }: LayoutProps = $props();

  const shell = getShell();
  const section = $derived(settingsSectionOf(page.url.pathname));
  // The Settings home has no permission of its own: it is open to anyone who may use one of the settings it lists.
  const features = $derived(shell?.features() ?? {});
  const roleAllows = $derived(section ? can(data.user, section.permission) : visibleSettingsSections(data.user, features).length > 0);
  // API keys and webhooks reach every job, so a person on Only jobs they're on cannot use them whatever their role says.
  const jobsAllow = $derived(!section?.allJobs || seesAllJobs(data.user));
  const permitted = $derived(roleAllows && jobsAllow);
  // Slack's page exists only where a Slack app is set up (A3): an address to it elsewhere goes to the first page the person
  // may use, once the shell has heard that Slack is off. While that loads nothing is drawn or decided for someone who may
  // use the page; anyone else already has their answer, and without an organization the shell never asks.
  const hidden = $derived(Boolean(section?.feature) && features[section!.feature!] === false);
  const waiting = $derived(permitted && Boolean(section?.feature) && featureWaits(features, section!.feature!));
  // Someone whose only setting is Slack has no home until the shell hears that Slack is on: wait, never refuse.
  const homeWaits = $derived(!section && !roleAllows && featureWaits(features, 'slack') && visibleSettingsSections(data.user, { ...features, slack: true }).length > 0);
  const pending = $derived(hidden || waiting || homeWaits);
  const allowed = $derived(permitted && !pending);
  // A refused page is titled with the setting it was reached from.
  const title = $derived(section?.label ?? 'Settings');
  $effect(() => {
    if (hidden) void goto(visibleSettingsSections(data.user, features)[0]?.href ?? '/', { replaceState: true });
  });
</script>

<svelte:head>
  {#if !allowed && !pending}<title>{title} · ScreenHire</title>{/if}
</svelte:head>

{#if allowed}
  {@render children()}
{:else if !pending}
  <TopBar crumbs={settingsCrumbs(page.url.pathname)} />

  <main class="flex flex-col">
    <HeaderBand form {title} />
    <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
      <Panel title={roleAllows ? 'Needs all jobs' : 'Not in your role'}>
        <Empty.Root>
          <Empty.Header>
            <Empty.Description>
              {#if roleAllows}
                {section?.label} reach every job, and you see only the jobs you're on. Ask an owner or admin of your organization.
              {:else if section?.refusal}
                {section.refusal}
              {:else}
                Your role cannot change {section ? section.label.toLowerCase() : 'settings'}. Ask an owner or admin of your organization.
              {/if}
            </Empty.Description>
          </Empty.Header>
          <Empty.Content><Button href="/" variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to jobs</Button></Empty.Content>
        </Empty.Root>
      </Panel>
    </div>
  </main>
{/if}
