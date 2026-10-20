<script lang="ts">
  import { createCreateHiringTemplate, createListHiringTemplates } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { labelClass, selectClass } from '#lib/components/workflow/controls.js';
  import { formatShortDate } from '#lib/format.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { templatesPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';

  // Hiring templates (workflow-engine.md §1): the system templates ScreenHire ships, read-only, and the organization's
  // own. A new template starts blank or as a copy of a published version, to customise.
  const queryClient = useQueryClient();
  const templates = createListHiringTemplates();
  const create = createCreateHiringTemplate(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, templatesPath) } }));

  const list = $derived(templates.data?.data ?? []);
  const copyable = $derived(list.filter((template) => template.latestPublishedVersionId));

  let creating = $state(false);
  let name = $state('');
  let copyFrom = $state('');
  let failure = $state('');

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    failure = '';
    if (!name.trim()) {
      failure = 'Give the template a name.';
      return;
    }
    try {
      const response = await create.mutateAsync({ data: { name: name.trim(), copyFromVersionId: copyFrom || null } });
      creating = false;
      await goto(`/templates/${response.data.id}`);
    } catch (error) {
      failure = describeError(error, 'Could not create the template');
    }
  }
</script>

<svelte:head>
  <title>Templates · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Templates' }]} />

<main class="flex flex-col gap-3.5 px-5 py-4.5">
  <div class="flex flex-wrap items-end justify-between gap-3">
    <div class="flex flex-col gap-1.5">
      <h1 class="font-display text-[30px] leading-tight font-bold tracking-[-0.02em]">Templates</h1>
      <p class="max-w-2xl text-muted-foreground">
        A template is a reusable hiring workflow: stages, screening questions and automations. Jobs copy a published
        version and can be tailored on their own.
      </p>
    </div>
    <Button onclick={() => (creating = true)}>New template</Button>
  </div>

  {#if templates.isError}
    <QueryError message={describeError(templates.error, 'Could not load templates')} onretry={() => templates.refetch()} />
  {:else}
    <section aria-label="Templates" class="overflow-hidden rounded-xl border border-border bg-card">
      <Table.Root>
        <Table.Header>
          <Table.Row>
            <Table.Head class="font-mono text-[10.5px] tracking-[0.06em] uppercase">Name</Table.Head>
            <Table.Head class="font-mono text-[10.5px] tracking-[0.06em] uppercase">Published</Table.Head>
            <Table.Head class="font-mono text-[10.5px] tracking-[0.06em] uppercase">Draft</Table.Head>
            <Table.Head class="text-right font-mono text-[10.5px] tracking-[0.06em] uppercase">Updated</Table.Head>
          </Table.Row>
        </Table.Header>
        <Table.Body>
          {#if templates.isPending}
            {#each [0, 1, 2] as row (row)}
              <Table.Row aria-hidden="true">
                <Table.Cell colspan={4}><Skeleton class="h-5 w-full" /></Table.Cell>
              </Table.Row>
            {/each}
          {:else}
            {#each list as template (template.id)}
              <Table.Row>
                <Table.Cell>
                  <div class="flex flex-wrap items-center gap-2">
                    <a href={`/templates/${template.id}`} class="font-semibold text-foreground no-underline hover:underline">{template.name}</a>
                    {#if template.isSystem}<Badge variant="outline">ScreenHire</Badge>{/if}
                  </div>
                  {#if template.description}<p class="text-[12.5px] text-muted-foreground">{template.description}</p>{/if}
                </Table.Cell>
                <Table.Cell class="font-mono text-xs">{template.latestPublishedVersion ? `v${template.latestPublishedVersion}` : 'Not yet'}</Table.Cell>
                <Table.Cell class="font-mono text-xs text-muted-foreground">{template.draftVersion ? `v${template.draftVersion}` : '-'}</Table.Cell>
                <Table.Cell class="text-right font-mono text-xs text-muted-foreground">{formatShortDate(template.updatedAt ?? template.createdAt)}</Table.Cell>
              </Table.Row>
            {:else}
              <Table.Row>
                <Table.Cell colspan={4} class="text-muted-foreground">No templates yet.</Table.Cell>
              </Table.Row>
            {/each}
          {/if}
        </Table.Body>
      </Table.Root>
    </section>
  {/if}
</main>

<Dialog.Root bind:open={creating}>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>New template</Dialog.Title>
      <Dialog.Description>Version 1 starts as a draft. Publish it when it is ready for jobs to use.</Dialog.Description>
    </Dialog.Header>
    <form class="flex flex-col gap-3" onsubmit={submit}>
      <label class={labelClass}>
        Name
        <Input bind:value={name} maxlength={120} required />
      </label>
      <label class={labelClass}>
        Start from
        <select class={selectClass} bind:value={copyFrom}>
          <option value="">A basic pipeline</option>
          {#each copyable as template (template.id)}
            <option value={template.latestPublishedVersionId}>{template.name} (v{template.latestPublishedVersion})</option>
          {/each}
        </select>
      </label>
      <p role="status" class="text-[13px] font-semibold text-destructive empty:hidden">{failure}</p>
      <Dialog.Footer>
        <Dialog.Close class={cn(buttonVariants({ variant: 'outline' }))}>Cancel</Dialog.Close>
        <Button type="submit" disabled={create.isPending}>Create template</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
