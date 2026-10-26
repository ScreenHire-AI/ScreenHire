<script lang="ts">
  import ExternalLinkIcon from '@lucide/svelte/icons/external-link';
  import SaveIcon from '@lucide/svelte/icons/save';
  import type { HttpValidationProblemDetails, OrganizationProfile } from '@screenhire/api-client';
  import {
    ApiError,
    createGetOrganization,
    createUpdateOrganization,
    getGetOrganizationHistoryQueryOptions,
    getGetOrganizationQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { UpdateOrganizationBody } from '@screenhire/api-client/zod';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { invalidateAll } from '$app/navigation';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistoryFact from '#lib/components/history-fact.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';

  // The organization's name, which owners and admins can change, and what they cannot: the careers address (its slug
  // is what boards and candidates link to) and where its data lives. The form template (section 7).
  const queryClient = useQueryClient();
  const organization = createGetOrganization();
  const detail = $derived(organization.data?.data);
  const update = createUpdateOrganization(() => ({
    mutation: { onSuccess: () => queryClient.invalidateQueries({ queryKey: getGetOrganizationQueryKey(), exact: true }) },
  }));

  const schema = UpdateOrganizationBody.extend({
    name: z.string().trim().min(1, "Enter your organization's name.").max(200, 'Keep the name to 200 characters or fewer.'),
  });

  let saved = $state('');
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      saved = '';
      if (!submitted.valid) return;
      try {
        await update.mutateAsync({ data: { name: submitted.data.name.trim() } });
        // The sidebar's workspace name comes from the session the root layout loads.
        await invalidateAll();
        saved = 'Saved.';
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the name')];
        }
      }
    },
  });

  // The form starts from the organization once it loads; a refetch after that keeps what was typed.
  let started = false;
  $effect(() => {
    if (detail && !started) {
      started = true;
      reset({ data: { name: detail.name } });
    }
  });

  const region = (profile: OrganizationProfile) => profile.homeRegion.toUpperCase();
</script>

<svelte:head>
  <title>Organization · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Organization' }]} />

<main class="flex flex-col">
  <HeaderBand title="Organization">
    {#snippet facts()}
      {#if detail}<HistoryFact value={detail.lastUpdated} name={detail.name} page={(before) => getGetOrganizationHistoryQueryOptions({ before })} />{/if}
    {/snippet}
  </HeaderBand>

  <SettingsMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
    {#if organization.isError && !detail}
      <Panel title="Details">
        <QueryError message={describeError(organization.error, 'Could not load the organization')} onretry={() => organization.refetch()} inset />
      </Panel>
    {:else if !detail}
      <Panel title="Details">
        <div aria-hidden="true" class="flex flex-col gap-4"><Skeleton class="h-14 w-full" /></div>
        <p class="sr-only" role="status">Loading the organization…</p>
      </Panel>
      <Panel title="Careers page">
        <div aria-hidden="true" class="flex flex-col gap-3">
          {#each [0, 1, 2] as row (row)}<Skeleton class="h-5 w-full" />{/each}
        </div>
      </Panel>
    {:else}
      <form method="POST" use:enhance novalidate aria-label="Organization details">
        <Panel title="Details">
          <Field.Field data-invalid={$errors.name ? true : undefined}>
            <Field.Label for="organization-name">Name</Field.Label>
            <Input
              id="organization-name"
              name="name"
              maxlength={200}
              required
              autocomplete="organization"
              bind:value={$form.name}
              aria-invalid={$errors.name ? 'true' : undefined}
              aria-describedby={$errors.name ? 'organization-name-error' : undefined}
            />
            <Field.Error id="organization-name-error" errors={$errors.name?.map((message) => ({ message }))} />
          </Field.Field>
          {#snippet footer()}
            <div class="flex flex-wrap items-center justify-end gap-2">
              <FailureText message={$errors._errors?.[0]} class="mr-auto" />
              <p role="status" class="mr-auto text-muted-foreground empty:hidden">{$errors._errors ? '' : saved}</p>
              <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save</Button>
            </div>
          {/snippet}
        </Panel>
      </form>

      <Panel title="Careers page">
        <SummaryList>
          <SummaryItem label="Address">
            <span class="flex min-w-0"><TruncatedText text={detail.careersUrl} href={detail.careersUrl} icon={ExternalLinkIcon} underlined class="text-primary" /></span>
          </SummaryItem>
          <SummaryItem label="Data region">{region(detail)}</SummaryItem>
          <SummaryItem label="Created"><RelativeTime at={detail.createdAt} /></SummaryItem>
        </SummaryList>
      </Panel>
    {/if}
  </div>
  </SettingsMenu>
</main>
