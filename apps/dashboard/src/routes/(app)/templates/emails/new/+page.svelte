<script lang="ts">
  import SaveIcon from '~icons/lucide/save';
  import type { EmailPreview } from '@screenhire/api-client';
  import { createCreateEmailTemplate, createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import EmailTemplateForm from '#lib/components/email-template-form.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import LeaveDialog from '#lib/components/leave-dialog.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { emailTemplatesPath } from '#lib/email.js';
  import { can } from '#lib/permissions.js';
  import { createPreviewSource } from '#lib/preview-window.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { templateHref, templatesHref, templateUsagePath } from '#lib/templates.js';

  // A new email template of the organization's own, in the email template editor (design system section 7, builder):
  // the Edit / Preview switch over its name and wording (A8). Saving creates it and opens its editor.
  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const templates = createListEmailTemplates();
  const create = createCreateEmailTemplate(() => ({
    mutation: { onSettled: () => Promise.all([invalidatePath(queryClient, emailTemplatesPath), invalidatePath(queryClient, templateUsagePath)]) },
  }));
  const list = $derived(templates.data?.data);
  const listHref = templatesHref('emails');
  const mayEdit = $derived(can(page.data.user, 'EditEmailTemplates'));
  const formId = 'email-template-form';

  let mode = $state<'edit' | 'preview'>('edit');
  let rendered = $state<{ preview: EmailPreview | undefined; failure: string | undefined }>({ preview: undefined, failure: undefined });
  let dirty = $state(false);
  let leaving = $state(false);
  const source = createPreviewSource(() => rendered);

  async function save(value: { name: string | null; subject: string; body: string }) {
    const response = await create.mutateAsync({ data: { name: value.name ?? '', subject: value.subject, body: value.body } });
    leaving = true;
    await goto(templateHref('emails', response.data.id), { replaceState: true });
  }
</script>

<svelte:head>
  <title>New email template · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Templates', href: listHref }, { label: 'New email template' }]} back />

{#snippet readOnlyReason()}
  <!-- Read only says why and what to do (E3), in one line under the title. -->
  <p>Ask an owner or admin to make email templates.</p>
{/snippet}

<main class={['flex flex-col', sidebar.isMobile && mayEdit && 'pb-28']}>
  <HeaderBand title="New email template" details={mayEdit ? undefined : readOnlyReason} actions={sidebar.isMobile || !mayEdit ? undefined : saveButton} />
  <div class="flex w-full flex-col gap-4 px-4 py-6 md:px-6">
    {#if !mayEdit}
      <!-- The header band says why there is nothing to fill in (E3). -->
    {:else if templates.isError}
      <Panel title="Wording" class="max-w-form">
        <QueryError message={describeError(templates.error, 'Could not load the variables')} onretry={() => templates.refetch()} inset />
      </Panel>
    {:else if !list}
      <Panel title="Wording" class="max-w-form">
        <div aria-hidden="true" class="flex flex-col gap-4">{#each [0, 1, 2] as field (field)}<Skeleton class="h-14 w-full" />{/each}</div>
        <p class="sr-only" role="status">Loading…</p>
      </Panel>
    {:else}
      <EmailTemplateForm
        variables={list.variables}
        sendingEnabled={list.sendingEnabled}
        bind:mode
        onpopout={() => source.open('/preview/email')}
        bind:rendered
        bind:dirty
        {formId}
        failure="Could not create the template"
        onsave={save}
      />
    {/if}
  </div>
  {#if sidebar.isMobile && mayEdit}
    <ActionBar>{@render saveButton()}</ActionBar>
  {/if}
</main>

{#snippet saveButton()}
  <Button type="submit" form={formId} size="lg" disabled={create.isPending}><SaveIcon aria-hidden="true" />Save</Button>
{/snippet}

<!-- Unsaved wording lives only on this page: ask before leaving it. -->
<LeaveDialog unsaved={dirty && !leaving} title="Leave without saving this template?" />
