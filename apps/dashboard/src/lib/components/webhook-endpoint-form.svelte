<script lang="ts">
  import SaveIcon from '@lucide/svelte/icons/save';
  import type { HttpValidationProblemDetails, WebhookEndpointEntry } from '@screenhire/api-client';
  import { ApiError, createListWebhookEventTypes } from '@screenhire/api-client/svelte-query';
  import { untrack } from 'svelte';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';
  import { eventLabel } from '#lib/webhooks.js';

  // A webhook endpoint's address, description and events, on the form template: the new page and the edit page. The
  // API checks the address again (public https only) and says why it refused one.
  let {
    endpoint,
    submitLabel,
    cancelHref,
    failure,
    onsave,
  }: {
    /** The endpoint to edit; none for a new one. */
    endpoint?: WebhookEndpointEntry;
    submitLabel: string;
    cancelHref: string;
    /** What failed, for `describeError`: "Could not add the endpoint". */
    failure: string;
    onsave: (value: { url: string; description: string | null; events: string[] }) => Promise<unknown>;
  } = $props();

  const eventTypes = createListWebhookEventTypes();
  const types = $derived(eventTypes.data?.data ?? []);

  const schema = z.object({
    url: z.string().trim().min(1, 'Enter the address to send events to.').max(2048, 'Keep the address to 2,048 characters or fewer.'),
    description: z.string().max(200, 'Keep the description to 200 characters or fewer.'),
    events: z.array(z.string()).min(1, 'Choose at least one event.'),
  });

  const initial = untrack(() => ({ url: endpoint?.url ?? '', description: endpoint?.description ?? '', events: [...(endpoint?.events ?? [])] }));
  const { form, errors, enhance, submitting } = superForm(defaults(initial, zod4(schema)), {
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
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, failure)];
        }
      }
    },
  });

  const describedBy = (id: string, error: unknown, hint?: string) => [hint, error ? id : undefined].filter(Boolean).join(' ') || undefined;
  const eventErrors = $derived(($errors.events as { _errors?: string[] } | undefined)?._errors ?? []);
</script>

<form method="POST" use:enhance novalidate aria-label={endpoint ? 'Edit webhook endpoint' : 'New webhook endpoint'}>
  <Panel title="Endpoint">
    <div class="flex flex-col gap-4">
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
    </div>
    {#snippet footer()}
      <div class="flex flex-wrap items-center justify-end gap-2">
        <FailureText message={$errors._errors?.[0]} class="mr-auto" />
        <Button href={cancelHref} variant="outline" size="lg">Cancel</Button>
        <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />{submitLabel}</Button>
      </div>
    {/snippet}
  </Panel>
</form>
