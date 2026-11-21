<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import SaveIcon from '~icons/lucide/save';
  import type { HttpValidationProblemDetails, WebhookEndpointEntry } from '@screenhire/api-client';
  import { ApiError, createListWebhookEventTypes } from '@screenhire/api-client/svelte-query';
  import { untrack } from 'svelte';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import Notice from '#lib/components/notice.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';
  import { cn } from '#lib/utils.js';
  import { eventLabel } from '#lib/webhooks.js';

  // A webhook endpoint's address, description and events, in a sheet (design system A16): Add endpoint over the Webhooks
  // list, Edit over the endpoint's page. The API checks the address again (public https only) and says why it refused
  // one. The sheet closes once `onsave` resolves; the page decides what follows (the signing secret, for a new one).
  let {
    open = $bindable(false),
    endpoint,
    onsave,
  }: {
    open?: boolean;
    /** The endpoint to edit; none for a new one. */
    endpoint?: WebhookEndpointEntry;
    onsave: (value: { url: string; description: string | null; events: string[] }) => Promise<unknown>;
  } = $props();

  const editing = $derived(endpoint !== undefined);
  // The events load when the sheet first opens, not with the page behind it.
  const eventTypes = createListWebhookEventTypes(() => ({ query: { enabled: open } }));
  const types = $derived(eventTypes.data?.data ?? []);

  const schema = z.object({
    url: z.string().trim().min(1, 'Enter the address to send events to.').max(2048, 'Keep the address to 2,048 characters or fewer.'),
    description: z.string().max(200, 'Keep the description to 200 characters or fewer.'),
    events: z.array(z.string()).min(1, 'Choose at least one event.'),
  });

  const valuesOf = (current: WebhookEndpointEntry | undefined) => ({
    url: current?.url ?? '',
    description: current?.description ?? '',
    events: [...(current?.events ?? [])],
  });
  const { form, errors, enhance, submitting, reset } = superForm(defaults(untrack(() => valuesOf(endpoint)), zod4(schema)), {
    SPA: true,
    dataType: 'json',
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      try {
        await onsave({
          url: submitted.data.url.trim(),
          description: submitted.data.description.trim() || null,
          events: submitted.data.events,
        });
        open = false;
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, editing ? 'Could not save the endpoint' : 'Could not add the endpoint')];
        }
      }
    },
  });

  // Each opening starts from the endpoint as it is now (its page reads it every few seconds), or empty for a new one.
  $effect(() => {
    if (open) untrack(() => reset({ data: valuesOf(endpoint) }));
  });

  const describedBy = (id: string, error: unknown, hint?: string) => [hint, error ? id : undefined].filter(Boolean).join(' ') || undefined;
  const eventErrors = $derived(($errors.events as { _errors?: string[] } | undefined)?._errors ?? []);
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>{editing ? 'Edit endpoint' : 'Add endpoint'}</Sheet.Title>
      <Sheet.Description>Where ScreenHire sends events, and which ones.</Sheet.Description>
    </Sheet.Header>
    <form method="POST" use:enhance novalidate aria-label={editing ? 'Edit webhook endpoint' : 'New webhook endpoint'} class="flex flex-col gap-5 px-4 pb-4">
      {#if endpoint && !endpoint.enabled}
        <!-- Saving leaves it off, and the sheet says so where someone is looking at the form (E3); Turn on is on the page behind. -->
        <Notice>This endpoint is off. Saving does not turn it on.</Notice>
      {/if}
      <Field.Field data-invalid={$errors.url ? true : undefined}>
        <Field.Label for="webhook-url">URL</Field.Label>
        <Input
          id="webhook-url"
          name="url"
          type="url"
          inputmode="url"
          autocomplete="off"
          spellcheck={false}
          maxlength={2048}
          required
          bind:value={$form.url}
          aria-invalid={$errors.url ? 'true' : undefined}
          aria-describedby={describedBy('webhook-url-error', $errors.url, 'webhook-url-hint')}
        />
        <Field.Description id="webhook-url-hint">A public https address.</Field.Description>
        <Field.Error id="webhook-url-error" errors={$errors.url?.map((message) => ({ message }))} />
      </Field.Field>

      <Field.Field data-invalid={$errors.description ? true : undefined}>
        <Field.Label for="webhook-description">Description <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
        <Input
          id="webhook-description"
          name="description"
          maxlength={200}
          autocomplete="off"
          bind:value={$form.description}
          aria-invalid={$errors.description ? 'true' : undefined}
          aria-describedby={describedBy('webhook-description-error', $errors.description)}
        />
        <Field.Error id="webhook-description-error" errors={$errors.description?.map((message) => ({ message }))} />
      </Field.Field>

      <fieldset class="flex flex-col gap-2" aria-describedby={eventErrors.length > 0 ? 'webhook-events-error' : undefined}>
        <legend class="mb-1 text-body font-medium">Events</legend>
        {#if eventTypes.isPending}
          <div aria-hidden="true" class="flex flex-col gap-2">{#each [0, 1, 2] as row (row)}<Skeleton class="h-6 w-56" />{/each}</div>
        {:else if eventTypes.isError}
          <QueryError message={describeError(eventTypes.error, 'Could not load the events')} onretry={() => eventTypes.refetch()} inset />
        {:else}
          {#each types as type (type)}
            <label class="flex min-w-0 items-center gap-2 pointer-coarse:min-h-11">
              <input type="checkbox" name="events" value={type} bind:group={$form.events} class="size-4 shrink-0 accent-primary" />
              {eventLabel(type)}
            </label>
          {/each}
        {/if}
        <Field.Error id="webhook-events-error" errors={eventErrors.map((message) => ({ message }))} />
      </fieldset>

      <FailureText message={$errors._errors?.[0]} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={$submitting}>
          {#if editing}<SaveIcon aria-hidden="true" />Save{:else}<PlusIcon aria-hidden="true" />Add endpoint{/if}
        </Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
