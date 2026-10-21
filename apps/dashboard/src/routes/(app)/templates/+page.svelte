<script lang="ts">
  import type { HttpValidationProblemDetails } from '@screenhire/api-client';
  import { ApiError, createCreateHiringTemplate, createListHiringTemplates } from '@screenhire/api-client/svelte-query';
  import { CreateHiringTemplateBody } from '@screenhire/api-client/zod';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FileStackIcon from '@lucide/svelte/icons/file-stack';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import { goto } from '$app/navigation';
  import ActionBar from '#lib/components/action-bar.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { formatDate } from '#lib/dates.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { templatesPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';

  // Hiring templates (workflow-engine.md §1): the system templates ScreenHire ships, read-only, and the organization's
  // own, on the list template. A new template starts blank or as a copy of a published version, to customise.
  const queryClient = useQueryClient();
  const templates = createListHiringTemplates();
  // The sidebar's own phone test (under md), where the primary action moves to the action bar.
  const sidebar = Sidebar.useSidebar();
  const create = createCreateHiringTemplate(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, templatesPath) } }));

  const list = $derived(templates.data?.data ?? []);
  const copyable = $derived(list.filter((template) => template.latestPublishedVersionId));

  let search = $state('');
  const shown = $derived.by(() => {
    const words = search.trim().toLowerCase();
    return words ? list.filter((template) => `${template.name} ${template.description ?? ''}`.toLowerCase().includes(words)) : list;
  });

  let creating = $state(false);

  // The select's "a basic pipeline" is an empty string, which the body's uuid would refuse; it becomes null on the way out.
  const schema = CreateHiringTemplateBody.pick({ name: true }).extend({
    name: z.string().trim().min(1, 'Give the template a name.'),
    copyFromVersionId: z.string(),
  });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '', copyFromVersionId: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid) return;
      try {
        const response = await create.mutateAsync({
          data: { name: form.data.name.trim(), copyFromVersionId: form.data.copyFromVersionId || null },
        });
        creating = false;
        await goto(`/templates/${response.data.id}`);
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(form, error.body as HttpValidationProblemDetails);
        } else {
          form.valid = false;
          form.errors._errors = [describeError(error, 'Could not create the template')];
        }
      }
    },
  });

  $effect(() => {
    if (creating) reset({ data: { name: '', copyFromVersionId: '' } });
  });
</script>

<svelte:head>
  <title>Templates · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Templates' }]} />

<main class={[sidebar.isMobile && 'pb-28']}>
  <!-- The page's primary action; a phone has it in the action bar (section 8). -->
  <HeaderBand
    title="Templates"
    count={templates.isSuccess ? list.length : undefined}
    countLabel={list.length === 1 ? 'template' : 'templates'}
    actions={sidebar.isMobile ? undefined : newTemplate}
    wide
  />

  <div class="mx-auto flex w-full max-w-page flex-col gap-5 px-4 py-6 md:px-6 2xl:max-w-page-wide">
    {#if templates.isError}
      <QueryError message={describeError(templates.error, 'Could not load templates')} onretry={() => templates.refetch()} />
    {:else}
      <Panel title="All templates" flush>
        {#snippet tools()}
          <SearchField bind:value={search} label="Search templates" class="ml-auto flex-[0_1_15rem]" />
        {/snippet}
        {#if templates.isPending}
          <div aria-hidden="true" class="flex flex-col gap-3 px-4.5 py-4">
            {#each [0, 1, 2] as row (row)}<Skeleton class="h-6 w-full" />{/each}
          </div>
          <p class="sr-only" role="status">Loading templates…</p>
        {:else if shown.length === 0}
          <Empty.Root>
            <Empty.Header>
              <Empty.Media variant="icon"><FileStackIcon /></Empty.Media>
              <Empty.Title>{list.length === 0 ? 'No templates yet' : 'No templates match'}</Empty.Title>
            </Empty.Header>
            <Empty.Content>
              {#if list.length === 0}
                <Button variant="outline" onclick={() => (creating = true)}>New template</Button>
              {:else}
                <Button variant="outline" onclick={() => (search = '')}>Clear search</Button>
              {/if}
            </Empty.Content>
          </Empty.Root>
        {:else}
          <!-- Phones get two-line list items (design system section 8); wider screens the table. -->
          <ul class="md:hidden">
            {#each shown as template (template.id)}
              <ListItem href={`/templates/${template.id}`} title={template.name}>
                {#snippet meta()}{formatDate(template.updatedAt ?? template.createdAt)}{/snippet}
                {#snippet facts()}
                  {#if template.isSystem}<Badge variant="muted">ScreenHire</Badge>{/if}
                  <Badge variant="muted" class="tabular-nums">{template.latestPublishedVersion ? `Version ${template.latestPublishedVersion}` : 'Not published'}</Badge>
                  {#if template.draftVersion}<Badge variant="muted" class="tabular-nums">Draft {template.draftVersion}</Badge>{/if}
                {/snippet}
              </ListItem>
            {/each}
          </ul>
          <div class="overflow-x-auto max-md:hidden">
            <Table.Root>
              <Table.Header>
                <Table.Row class="bg-muted hover:bg-muted">
                  <Table.Head class="px-4.5">Name</Table.Head>
                  <Table.Head>Description</Table.Head>
                  <Table.Head>Published</Table.Head>
                  <Table.Head>Draft</Table.Head>
                  <Table.Head class="pr-4.5 text-right">Updated</Table.Head>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {#each shown as template (template.id)}
                  <!-- The whole row is the link (design system section 9): the name's link covers it. -->
                  <Table.Row class="relative h-12">
                    <Table.Cell class="px-4.5">
                      <span class="flex items-center gap-2">
                        <a href={`/templates/${template.id}`} class="font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline"
                          >{template.name}</a
                        >
                        {#if template.isSystem}<Badge variant="muted">ScreenHire</Badge>{/if}
                      </span>
                    </Table.Cell>
                    <!-- The description takes the room the other columns leave and truncates, so the table fits from md
                         up rather than scrolling sideways (F1). -->
                    <Table.Cell class="w-full max-w-0 text-muted-foreground">
                      {#if template.description}<span class="relative z-10 block max-w-md"><TruncatedText text={template.description} /></span>{/if}
                    </Table.Cell>
                    <Table.Cell class="tabular-nums">
                      {#if template.latestPublishedVersion}Version {template.latestPublishedVersion}{:else}<span class="text-muted-foreground">Not yet</span>{/if}
                    </Table.Cell>
                    <Table.Cell class="text-muted-foreground tabular-nums">{template.draftVersion ? `Version ${template.draftVersion}` : ''}</Table.Cell>
                    <Table.Cell class="pr-4.5 text-right text-muted-foreground tabular-nums">{formatDate(template.updatedAt ?? template.createdAt)}</Table.Cell>
                  </Table.Row>
                {/each}
              </Table.Body>
            </Table.Root>
          </div>
        {/if}
      </Panel>
    {/if}
  </div>

  {#if sidebar.isMobile}
    <ActionBar>{@render newTemplate()}</ActionBar>
  {/if}
</main>

{#snippet newTemplate()}
  <Button size="lg" onclick={() => (creating = true)}><PlusIcon aria-hidden="true" />New template</Button>
{/snippet}

<Dialog.Root bind:open={creating}>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>New template</Dialog.Title>
      <Dialog.Description>Version 1 starts as a draft.</Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
      <Field.Field data-invalid={$errors.name ? true : undefined}>
        <Field.Label for="new-template-name">Name</Field.Label>
        <Input
          id="new-template-name"
          name="name"
          maxlength={120}
          required
          bind:value={$form.name}
          aria-invalid={$errors.name ? 'true' : undefined}
          aria-describedby={$errors.name ? 'new-template-name-error' : undefined}
        />
        <Field.Error id="new-template-name-error" errors={$errors.name?.map((message) => ({ message }))} />
      </Field.Field>
      <Field.Field data-invalid={$errors.copyFromVersionId ? true : undefined}>
        <Field.Label for="new-template-source">Start from</Field.Label>
        <select
          id="new-template-source"
          name="copyFromVersionId"
          class={selectClass}
          bind:value={$form.copyFromVersionId}
          aria-invalid={$errors.copyFromVersionId ? 'true' : undefined}
          aria-describedby={$errors.copyFromVersionId ? 'new-template-source-error' : undefined}
        >
          <option value="">A basic pipeline</option>
          {#each copyable as template (template.id)}
            <option value={template.latestPublishedVersionId}>{template.name} (v{template.latestPublishedVersion})</option>
          {/each}
        </select>
        <Field.Error id="new-template-source-error" errors={$errors.copyFromVersionId?.map((message) => ({ message }))} />
      </Field.Field>
      {#if $errors._errors}
        <p role="alert" class="text-body text-destructive">{$errors._errors[0]}</p>
      {/if}
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline' }))}>Cancel</Dialog.Close>
        <Button type="submit" disabled={$submitting}>Create template</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
