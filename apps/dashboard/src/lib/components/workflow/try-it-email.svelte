<script lang="ts">
  import type { EmailPreview as Preview, EmailTemplateEntry } from '@screenhire/api-client';
  import { createPreviewEmailTemplate } from '@screenhire/api-client/svelte-query';
  import EmailPreview from '#lib/components/email-preview.svelte';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { untrack } from 'svelte';
  import { describeError } from '#lib/query.js';

  // One email in Try it, drawn as an inbox lists it (design system section 11, Try it): the organization's logo and name,
  // the subject with the sample candidate's name filled in, and the start of the text, muted. The row is a button that
  // opens the email in full in a sheet, rendered by the API from the template's wording as it stands (`EmailPreview`).
  // Nothing is sent.
  let {
    template,
    organizationName,
    logoUrl,
  }: {
    /** The template as the organization words it; undefined while the list loads or when it no longer exists. */
    template: EmailTemplateEntry | undefined;
    organizationName: string;
    logoUrl?: string | null;
  } = $props();

  const previewer = createPreviewEmailTemplate();
  let preview = $state<Preview>();
  let failure = $state('');
  let open = $state(false);

  // The preview follows the template's wording only. The mutation's own state is read untracked: tracking it would run
  // the effect again each time the request settles, and send it again.
  $effect(() => {
    // Another template starts from nothing, so a row never shows the email it held before.
    preview = undefined;
    failure = '';
    if (!template) return;
    const { subject, body, id } = template;
    let current = true;
    untrack(() =>
      previewer
        .mutateAsync({ data: { subject, body, templateId: id } })
        .then((response) => current && (preview = response.data))
        .catch((error) => current && (failure = describeError(error, 'Could not show this email'))),
    );
    return () => (current = false);
  });

  // The first words of the text part, as an inbox shows under the subject. The text part starts with the greeting.
  const snippet = $derived(
    preview?.text
      .split('\n')
      .map((line) => line.trim())
      .find((line) => line.length > 0) ?? '',
  );
  const initial = $derived(organizationName.trim().charAt(0).toUpperCase() || '?');
</script>

<button
  type="button"
  data-try-it-email={template?.id}
  aria-haspopup="dialog"
  onclick={() => (open = true)}
  class="flex w-full min-w-0 items-center gap-3 rounded-lg border border-border bg-card p-3 text-left hover:bg-secondary focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
>
  {#if logoUrl}
    <img src={logoUrl} alt="" class="size-9 shrink-0 rounded-lg border border-border-subtle object-contain" />
  {:else}
    <span aria-hidden="true" class="grid size-9 shrink-0 place-items-center rounded-lg bg-secondary font-semibold">{initial}</span>
  {/if}
  <span class="flex min-w-0 flex-1 flex-col">
    <span class="truncate font-semibold">{organizationName}</span>
    {#if preview}
      <span data-try-it-subject class="truncate">{preview.subject}</span>
      <span class="truncate text-muted-foreground">{snippet}</span>
    {:else if failure}
      <span class="truncate text-muted-foreground">{template?.name ?? 'An email'}</span>
    {:else}
      <Skeleton class="mt-1 h-4 w-2/3" />
      <span class="sr-only">{template?.name ?? 'An email'}</span>
    {/if}
  </span>
</button>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-2xl">
    <Sheet.Header>
      <Sheet.Title>{template?.name ?? 'Email'}</Sheet.Title>
      <Sheet.Description>What the candidate gets. Nothing is sent.</Sheet.Description>
    </Sheet.Header>
    {#if open}
      <div class="px-4 pb-4"><EmailPreview {preview} {failure} /></div>
    {/if}
  </Sheet.Content>
</Sheet.Root>
