<script lang="ts">
  import ExternalLinkIcon from '@lucide/svelte/icons/external-link';
  import SaveIcon from '@lucide/svelte/icons/save';
  import type { HttpValidationProblemDetails, PublicBranding } from '@screenhire/api-client';
  import {
    ApiError,
    createGetOrganization,
    createGetOrganizationBranding,
    getGetOrganizationBrandingHistoryQueryOptions,
    createUpdateOrganizationBranding,
    getGetOrganizationBrandingQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistoryFact from '#lib/components/history-fact.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { brandColorPattern, normalizeBrandColor } from '#lib/branding.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';

  // The careers pages' brand colour (apply-flow.md section 5, design system section 18). The server derives the palette
  // and darkens a colour that would make text unreadable; this page shows what the careers pages will use.
  const queryClient = useQueryClient();
  const branding = createGetOrganizationBranding();
  const organization = createGetOrganization();
  const detail = $derived(branding.data?.data);
  const update = createUpdateOrganizationBranding(() => ({
    mutation: { onSuccess: () => queryClient.invalidateQueries({ queryKey: getGetOrganizationBrandingQueryKey(), exact: true }) },
  }));

  const schema = z.object({
    brandColor: z.string().trim().regex(brandColorPattern, 'Enter a hex colour of six digits, such as 1f4e5a.'),
  });

  let saved = $state('');

  async function save(brandColor: string | null) {
    const response = await update.mutateAsync({ data: { brandColor } });
    saved = response.data.brandAdjusted ? 'Saved. Darkened so text on it stays readable.' : 'Saved.';
    return response.data;
  }

  const { form, errors, enhance, submitting, reset } = superForm(defaults({ brandColor: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      saved = '';
      if (!submitted.valid) return;
      try {
        await save(normalizeBrandColor(submitted.data.brandColor));
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the colour')];
        }
      }
    },
  });

  let clearFailure = $state('');
  let clearing = $state(false);
  async function useDefault() {
    saved = '';
    clearFailure = '';
    clearing = true;
    try {
      await save(null);
      reset({ data: { brandColor: '' } });
      saved = "Saved. Careers pages use ScreenHire's colours.";
    } catch (error) {
      clearFailure = describeError(error, 'Could not clear the colour');
    } finally {
      clearing = false;
    }
  }

  // The field starts from the saved colour once it loads; a refetch after that keeps what was typed.
  let started = false;
  $effect(() => {
    if (detail && !started) {
      started = true;
      reset({ data: { brandColor: detail.brandColor ?? '' } });
    }
  });

  // The picker shows what the text field holds while it is a whole colour, and otherwise the careers pages' own default
  // (the `--candidate-cta` token), which they use until a colour is saved.
  const fallback = getComputedStyle(document.documentElement).getPropertyValue('--candidate-cta').trim();
  const picked = $derived(brandColorPattern.test($form.brandColor.trim()) ? normalizeBrandColor($form.brandColor) : fallback);

  const colours = (palette: PublicBranding) => [
    { label: 'Buttons', value: palette.brand },
    { label: 'Text on buttons', value: palette.onBrand },
    { label: 'Links and headings', value: palette.brandText },
    { label: 'Borders and focus', value: palette.brandUi },
  ];
</script>

<svelte:head>
  <title>Branding · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Branding' }]} />

<main class="flex flex-col">
  <HeaderBand title="Branding">
    {#snippet facts()}
      {#if detail}<HistoryFact value={detail.lastUpdated} name="Brand colour" page={(before) => getGetOrganizationBrandingHistoryQueryOptions({ before })} />{/if}
    {/snippet}
  </HeaderBand>

  <SettingsMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
    {#if branding.isError && !detail}
      <Panel title="Brand colour">
        <QueryError message={describeError(branding.error, 'Could not load the branding')} onretry={() => branding.refetch()} inset />
      </Panel>
    {:else if !detail}
      <Panel title="Brand colour">
        <div aria-hidden="true"><Skeleton class="h-14 w-full" /></div>
        <p class="sr-only" role="status">Loading the branding…</p>
      </Panel>
    {:else}
      <form method="POST" use:enhance novalidate aria-label="Brand colour">
        <Panel title="Brand colour">
          <Field.Field data-invalid={$errors.brandColor ? true : undefined}>
            <Field.Label for="brand-color">Hex colour</Field.Label>
            <div class="flex min-w-0 gap-2">
              <input
                type="color"
                aria-label="Pick a colour"
                value={picked}
                oninput={(event) => ($form.brandColor = event.currentTarget.value)}
                class="h-9 w-12 shrink-0 cursor-pointer rounded-lg border border-input bg-card p-1 pointer-coarse:h-11 pointer-coarse:w-14"
              />
              <Input
                id="brand-color"
                name="brandColor"
                maxlength={7}
                autocomplete="off"
                spellcheck={false}
                class="min-w-0 flex-1 font-mono"
                bind:value={$form.brandColor}
                aria-invalid={$errors.brandColor ? 'true' : undefined}
                aria-describedby={['brand-color-hint', $errors.brandColor && 'brand-color-error'].filter(Boolean).join(' ')}
              />
            </div>
            <Field.Description id="brand-color-hint">Buttons and links on your careers pages.</Field.Description>
            <Field.Error id="brand-color-error" errors={$errors.brandColor?.map((message) => ({ message }))} />
          </Field.Field>
          {#snippet footer()}
            <div class="flex flex-wrap items-center justify-end gap-2">
              <FailureText message={$errors._errors?.[0] ?? clearFailure} class="mr-auto" />
              <p role="status" class="mr-auto text-muted-foreground empty:hidden">{$errors._errors || clearFailure ? '' : saved}</p>
              {#if detail.brandColor}
                <Button variant="outline" size="lg" disabled={clearing || $submitting} onclick={useDefault}>Use the default</Button>
              {/if}
              <Button type="submit" size="lg" disabled={$submitting || clearing}><SaveIcon aria-hidden="true" />Save colour</Button>
            </div>
          {/snippet}
        </Panel>
      </form>

      <Panel title="On your careers pages">
        {#snippet actions()}
          {#if organization.data}
            <Button href={organization.data.data.careersUrl} target="_blank" rel="noopener" variant="outline">
              <ExternalLinkIcon aria-hidden="true" />View<span class="sr-only"> careers page (opens in a new tab)</span>
            </Button>
          {/if}
        {/snippet}
        {#if detail.palette}
          <SummaryList>
            {#each colours(detail.palette) as colour (colour.label)}
              <SummaryItem label={colour.label}>
                <span class="flex items-center gap-2">
                  <span aria-hidden="true" class="size-5 shrink-0 rounded-md border border-border" style:background-color={colour.value}></span>
                  <span class="font-mono text-code">{colour.value}</span>
                </span>
              </SummaryItem>
            {/each}
          </SummaryList>
        {:else}
          <p class="text-muted-foreground">ScreenHire's colours, until you save one.</p>
        {/if}
      </Panel>
    {/if}
  </div>
  </SettingsMenu>
</main>
