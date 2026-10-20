<script lang="ts">
  import type { HttpValidationProblemDetails } from '@screenhire/api-client';
  import { ApiError, createCreateHiringTemplate, createListHiringTemplates } from '@screenhire/api-client/svelte-query';
  import { CreateHiringTemplateBody } from '@screenhire/api-client/zod';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { goto } from '$app/navigation';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { applyValidationProblem } from '#lib/forms.js';
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
        <p role="alert" class="text-sm text-destructive">{$errors._errors[0]}</p>
      {/if}
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline' }))}>Cancel</Dialog.Close>
        <Button type="submit" disabled={$submitting}>Create template</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
