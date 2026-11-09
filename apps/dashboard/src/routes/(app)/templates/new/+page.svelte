<script lang="ts">
  import { createCreateHiringTemplate, createListHiringTemplates } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StartingPointCard from '#lib/components/starting-point-card.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { can } from '#lib/permissions.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { templatesPath } from '#lib/realtime.js';
  import { startingPoints, type StartingPoint } from '#lib/starting-points.js';
  import { templatesHref, templateUsagePath } from '#lib/templates.js';

  // A new hiring template starts from a choice (design system section 11, Start from): a starting point ScreenHire ships,
  // or nothing. Each card is its name, one example line and its stages as plain text. "Use this starting point" is the one
  // filled button; "Start from nothing" creates the basic pipeline. The template's page opens as a draft, to change and
  // publish.
  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const templates = createListHiringTemplates();
  const create = createCreateHiringTemplate(() => ({
    mutation: { onSettled: () => Promise.all([invalidatePath(queryClient, templatesPath), invalidatePath(queryClient, templateUsagePath)]) },
  }));
  const mayEdit = $derived(can(page.data.user, 'EditHiringTemplates'));
  const listHref = templatesHref('hiring');

  const byId = $derived(new Map((templates.data?.data ?? []).map((template) => [template.id, template])));
  let chosen = $state<StartingPoint>(startingPoints[0]!);
  let name = $state('');
  let named = $state(false);
  let failure = $state('');
  const chosenName = $derived(byId.get(chosen.templateId)?.name ?? '');

  // The name follows the chosen card until the person types their own.
  $effect(() => {
    if (!named) name = chosenName;
  });

  async function start(point: StartingPoint | null) {
    failure = '';
    // The name field follows the chosen card until the person types, so starting from nothing uses only a typed name.
    const typed = named ? name.trim() : '';
    const title = point ? name.trim() || chosenName : typed || 'New hiring template';
    try {
      const response = await create.mutateAsync({ data: { name: title, copyFromVersionId: point?.versionId ?? null } });
      await goto(`/templates/${response.data.id}`);
    } catch (error) {
      failure = describeError(error, 'Could not create the template');
    }
  }
</script>

<svelte:head>
  <title>New hiring template · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Templates', href: listHref }, { label: 'New hiring template' }]} back />

{#snippet actions()}
  <Button variant="outline" size="lg" href={listHref}>Cancel</Button>
  {@render useIt()}
{/snippet}

{#snippet useIt()}
  <Button size="lg" disabled={create.isPending || !chosenName} onclick={() => start(chosen)}>Use this starting point</Button>
{/snippet}

<main class={['flex flex-col', sidebar.isMobile && mayEdit && 'pb-28']}>
  <HeaderBand title="New hiring template" actions={sidebar.isMobile || !mayEdit ? undefined : actions} />
  <div class="flex w-full flex-col gap-5 px-4 py-6 md:px-6">
    {#if !mayEdit}
      <p class="text-muted-foreground">Ask an owner or admin to make hiring templates.</p>
    {:else}
      <Panel label="Name" class="max-w-form">
        <label class="flex flex-col gap-1 text-body font-semibold text-secondary-foreground">
          Template name
          <Input
            bind:value={name}
            maxlength={120}
            oninput={() => (named = true)}
            onkeydown={(event) => event.key === 'Enter' && (event.preventDefault(), start(chosen))}
          />
        </label>
      </Panel>
      <Panel title="Start from">
        {#snippet actions()}
          <Button variant="outline" disabled={create.isPending} onclick={() => start(null)}>Start from nothing</Button>
        {/snippet}
        <fieldset class="m-0 border-0 p-0">
          <legend class="sr-only">Starting point</legend>
          <div class="grid grid-cols-[repeat(auto-fit,minmax(16rem,1fr))] gap-4">
            {#each startingPoints as point (point.templateId)}
              <StartingPointCard template={byId.get(point.templateId)} chosen={chosen.templateId === point.templateId} onchoose={() => (chosen = point)} />
            {/each}
          </div>
        </fieldset>
        <FailureText message={failure} class="mt-3" />
      </Panel>
    {/if}
  </div>
  {#if sidebar.isMobile && mayEdit}
    <ActionBar>{@render useIt()}</ActionBar>
  {/if}
</main>
