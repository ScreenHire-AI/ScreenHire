<script lang="ts">
  import SendIcon from '~icons/lucide/send';
  import type { ApplicationFollowUp, FollowUpQuestionnaire } from '@screenhire/api-client';
  import { sendApplicationForm } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { sendableForms } from '#lib/application-forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { cn } from '#lib/utils.js';

  // "Send a form" (workflow-engine.md section 8): the job's forms, each with Send, or Send again while it waits for the
  // candidate, and none once it is answered. It emails the candidate the form's link in the sender's name, whatever the
  // form's automations do; a second send of the same form is the same form with a fresh link.
  let {
    open = $bindable(false),
    applicationId,
    forms,
    sent,
    onsent,
  }: {
    open?: boolean;
    applicationId: string;
    /** The job's forms. */
    forms: readonly FollowUpQuestionnaire[];
    /** The forms sent to this candidate so far. */
    sent: readonly ApplicationFollowUp[];
    onsent?: (name: string) => void;
  } = $props();

  const queryClient = useQueryClient();
  const rows = $derived(sendableForms(forms, sent));
  let sending = $state<string | null>(null);
  let failure = $state('');

  // Each press is one send; its key lets the API drop a repeat of the same request rather than send twice.
  async function send(id: string, name: string) {
    failure = '';
    sending = id;
    try {
      await sendApplicationForm(applicationId, id, { headers: { 'Idempotency-Key': crypto.randomUUID() } });
      await invalidatePath(queryClient, `/v1/applications/${applicationId}`);
      open = false;
      onsent?.(name);
    } catch (error) {
      failure = describeError(error, `Could not send ${name}`);
    } finally {
      sending = null;
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Send a form</Sheet.Title>
      <Sheet.Description>The candidate gets an email with a link to answer it, in your name.</Sheet.Description>
    </Sheet.Header>
    <div class="flex flex-col gap-4 px-4 pb-4">
      {#if rows.length === 0}
        <p>This job has no forms to send yet. Add one on its pipeline's Forms tab.</p>
      {:else}
        <ul class="flex flex-col divide-y divide-border rounded-lg border border-border">
          {#each rows as row (row.id)}
            <li class="flex items-center justify-between gap-3 px-3 py-2">
              <div class="flex min-w-0 flex-col">
                <span class="font-semibold break-words">{row.name}</span>
                <span class="text-caption text-muted-foreground">
                  {row.answered ? 'Answered' : row.sent ? 'Sent, waiting' : 'Not sent yet'}{row.equality ? ', counted only' : ''}
                </span>
              </div>
              {#if !row.answered}
                <Button variant="outline" disabled={sending !== null} onclick={() => send(row.id, row.name)} aria-label={`${row.sent ? 'Send again' : 'Send'} ${row.name}`}>
                  <SendIcon aria-hidden="true" />{row.sent ? 'Send again' : 'Send'}
                </Button>
              {/if}
            </li>
          {/each}
        </ul>
      {/if}
      <FailureText message={failure} />
      <div class="flex justify-end">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Close</Sheet.Close>
      </div>
    </div>
  </Sheet.Content>
</Sheet.Root>
