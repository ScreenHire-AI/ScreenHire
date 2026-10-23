<script lang="ts">
  import { createCreateEmailTemplate, createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import EmailTemplateForm from '#lib/components/email-template-form.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { emailTemplatesPath } from '#lib/email.js';
  import { describeError, invalidatePath } from '#lib/query.js';

  // A new template of the organization's own, on the form template. Saving opens the list, where it now appears.
  const queryClient = useQueryClient();
  const templates = createListEmailTemplates();
  const create = createCreateEmailTemplate(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, emailTemplatesPath) } }));
  const list = $derived(templates.data?.data);

  async function save(value: { name: string | null; subject: string; body: string }) {
    await create.mutateAsync({ data: { name: value.name ?? '', subject: value.subject, body: value.body } });
    await goto('/settings/email-templates');
  }
</script>

<svelte:head>
  <title>Add template · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Email templates', href: '/settings/email-templates' }, { label: 'Add template' }]} back />

<main class="flex flex-col">
  <HeaderBand form title="Add template" />
  <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
    {#if templates.isError && !list}
      <Panel title="Wording">
        <QueryError message={describeError(templates.error, 'Could not load the variables')} onretry={() => templates.refetch()} inset />
      </Panel>
    {:else if !list}
      <Panel title="Wording">
        <div aria-hidden="true" class="flex flex-col gap-4">{#each [0, 1, 2] as field (field)}<Skeleton class="h-14 w-full" />{/each}</div>
        <p class="sr-only" role="status">Loading…</p>
      </Panel>
    {:else}
      <EmailTemplateForm
        variables={list.variables}
        sendingEnabled={list.sendingEnabled}
        submitLabel="Add template"
        cancelHref="/settings/email-templates"
        failure="Could not add the template"
        onsave={save}
      />
    {/if}
  </div>
</main>
