<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import CopyIcon from '~icons/lucide/copy';
  import type { HttpValidationProblemDetails, ManualPostingStatus } from '@screenhire/api-client';
  import { ApiError, createCreateManualPosting, createUpdateManualPosting } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { untrack } from 'svelte';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { CopyFeedback } from '#lib/copy-feedback.svelte.js';
  import { commonBoards, isUsableKey, localToday, sourceKeyFor, trackedUrl } from '#lib/manual-postings.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';

  // Where the job was posted by hand (Post it yourself, design system section 11): the board's name, picked from common
  // boards or typed, its tracked apply link to paste on the board, and the listing's address and dates. A new posting's
  // link follows the name as it is typed and is sent with the save, so the link the person copied is the one that counts;
  // once saved, the link never changes, and editing the name renames the row only. The listing's address is a link for the
  // person: nothing fetches it.
  let {
    jobId,
    jobUrl,
    boardIds,
    posting,
    onsaved,
    oncancel,
  }: {
    jobId: string;
    /** The job's careers page, which every tracked link extends. */
    jobUrl: string;
    /** The boards ScreenHire posts to itself, whose ids a link cannot take. */
    boardIds: readonly string[];
    /** The posting being edited; a new one when left out. */
    posting?: ManualPostingStatus;
    onsaved?: () => void;
    oncancel?: () => void;
  } = $props();

  const queryClient = useQueryClient();
  const settle = { onSettled: () => invalidatePath(queryClient, jobsPath) };
  const create = createCreateManualPosting(() => ({ mutation: settle }));
  const update = createUpdateManualPosting(() => ({ mutation: settle }));

  const isUrl = (value: string) => {
    if (value === '') return true;
    try {
      const url = new URL(value);
      // As the server's IsListingUrl: a web address with a host and no user name or password in it.
      return (url.protocol === 'https:' || url.protocol === 'http:') && url.hostname !== '' && url.username === '' && url.password === '';
    } catch {
      return false;
    }
  };
  const schema = z
    .object({
      boardName: z
        .string()
        .trim()
        .min(1, 'Name the board you posted on.')
        .max(80, 'Keep the board name to 80 characters or fewer.')
        .refine((name) => sourceKeyFor(name) !== '', 'Use at least one letter or digit in the board name.'),
      listingUrl: z.string().trim().max(2048, 'That address is too long.').refine(isUrl, "Give the listing's full address, starting with https://."),
      postedOn: z.string().min(1, 'Say when you posted it.'),
      endsOn: z.string(),
    })
    .refine((value) => value.endsOn === '' || value.endsOn >= value.postedOn, { message: 'The end date cannot be before the day it was posted.', path: ['endsOn'] });

  const start = untrack(() => ({
    boardName: posting?.boardName ?? '',
    listingUrl: posting?.listingUrl ?? '',
    postedOn: posting?.postedOn ?? localToday(),
    endsOn: posting?.endsOn ?? '',
  }));

  const { form, errors, enhance, submitting } = superForm(defaults(start, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      const { boardName, listingUrl, postedOn, endsOn } = submitted.data;
      try {
        if (posting) {
          await update.mutateAsync({ jobId, postingId: posting.id, data: { boardName, listingUrl: listingUrl || null, postedOn, endsOn: endsOn || null } });
        } else {
          await create.mutateAsync({ jobId, data: { boardName, sourceKey: sourceKeyFor(boardName), listingUrl: listingUrl || null, postedOn, endsOn: endsOn || null } });
        }
        onsaved?.();
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the posting')];
        }
      }
    },
  });

  // The link to paste on the board: a saved posting's own, or the one the name being typed makes.
  const key = $derived(posting?.sourceKey ?? sourceKeyFor($form.boardName));
  const usable = $derived(posting !== undefined || isUsableKey(key, boardIds));
  const link = $derived(posting?.trackedUrl ?? (usable ? trackedUrl(jobUrl, key) : ''));

  const feedback = new CopyFeedback();

  const describedBy = (field: string, error: unknown, hint = false) =>
    [hint ? `manual-${field}-hint` : '', error ? `manual-${field}-error` : ''].filter(Boolean).join(' ') || undefined;
</script>

<form method="POST" use:enhance novalidate aria-label={posting ? `Edit the posting on ${posting.boardName}` : 'Save the posting'} class="flex min-w-0 flex-col gap-4" data-manual-posting-form>
  <Field.Field data-invalid={$errors.boardName ? true : undefined}>
    <Field.Label for="manual-board">Board</Field.Label>
    <Input
      id="manual-board"
      name="boardName"
      list="manual-board-options"
      maxlength={80}
      autocomplete="off"
      placeholder="LinkedIn, or type the board's name"
      bind:value={$form.boardName}
      aria-invalid={$errors.boardName ? 'true' : undefined}
      aria-describedby={describedBy('board', $errors.boardName)}
    />
    <datalist id="manual-board-options">
      {#each commonBoards as board (board)}<option value={board}></option>{/each}
    </datalist>
    <Field.Error id="manual-board-error" errors={$errors.boardName?.map((message) => ({ message }))} />
  </Field.Field>

  <!-- The apply link for the board: applications through it show the board in Source and in Reports. -->
  <Field.Field>
    <Field.Label for="manual-link">Apply link for this board</Field.Label>
    <!-- Wraps: on a phone Copy link goes under the link rather than leaving the link a few characters wide (F2). -->
    <div class="flex min-w-0 flex-wrap gap-2">
      <Input id="manual-link" readonly value={link} placeholder="Name the board first" class="min-w-40 flex-1 font-mono text-code" aria-describedby="manual-link-hint" data-tracked-link />
      <Button type="button" variant="outline" size="lg" disabled={!link} onclick={() => feedback.copyText('link', link)} class="shrink-0">
        {#if feedback.copied('link')}<CheckIcon aria-hidden="true" />{:else}<CopyIcon aria-hidden="true" />{/if}{feedback.said('link', 'Copy link')}
      </Button>
    </div>
    <Field.Description id="manual-link-hint">
      {#if !usable && key !== ''}
        ScreenHire posts to this board itself. Name the board another way.
      {:else}
        Applications through it show {$form.boardName.trim() || 'the board'} as their source once you save.
      {/if}
    </Field.Description>
  </Field.Field>

  <Field.Field data-invalid={$errors.listingUrl ? true : undefined}>
    <Field.Label for="manual-listing">Listing address <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
    <Input
      id="manual-listing"
      name="listingUrl"
      type="url"
      inputmode="url"
      autocomplete="off"
      placeholder="https://"
      bind:value={$form.listingUrl}
      aria-invalid={$errors.listingUrl ? 'true' : undefined}
      aria-describedby={describedBy('listing', $errors.listingUrl, true)}
    />
    <Field.Description id="manual-listing-hint">The board's name in the list links here.</Field.Description>
    <Field.Error id="manual-listing-error" errors={$errors.listingUrl?.map((message) => ({ message }))} />
  </Field.Field>

  <div class="grid gap-4 sm:grid-cols-2">
    <Field.Field data-invalid={$errors.postedOn ? true : undefined}>
      <Field.Label for="manual-posted">Date posted</Field.Label>
      <Input
        id="manual-posted"
        name="postedOn"
        type="date"
        bind:value={$form.postedOn}
        aria-invalid={$errors.postedOn ? 'true' : undefined}
        aria-describedby={describedBy('posted', $errors.postedOn)}
      />
      <Field.Error id="manual-posted-error" errors={$errors.postedOn?.map((message) => ({ message }))} />
    </Field.Field>
    <Field.Field data-invalid={$errors.endsOn ? true : undefined}>
      <Field.Label for="manual-ends">End date <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
      <Input
        id="manual-ends"
        name="endsOn"
        type="date"
        bind:value={$form.endsOn}
        aria-invalid={$errors.endsOn ? 'true' : undefined}
        aria-describedby={describedBy('ends', $errors.endsOn)}
      />
      <Field.Error id="manual-ends-error" errors={$errors.endsOn?.map((message) => ({ message }))} />
    </Field.Field>
  </div>

  <div class="flex flex-wrap items-center justify-end gap-2 border-t border-border-subtle pt-4">
    <div class="mr-auto flex min-w-0 flex-col gap-1">
      {#each $errors._errors ?? [] as message (message)}<FailureText {message} />{/each}
    </div>
    {#if oncancel}<Button type="button" variant="outline" size="lg" onclick={oncancel}>Cancel</Button>{/if}
    <Button type="submit" size="lg" disabled={$submitting}>{posting ? 'Save changes' : 'Save posting'}</Button>
  </div>
</form>
