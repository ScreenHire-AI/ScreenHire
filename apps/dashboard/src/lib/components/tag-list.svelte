<script lang="ts">
  import PlusIcon from '@lucide/svelte/icons/plus';
  import XIcon from '@lucide/svelte/icons/x';
  import FailureText from '#lib/components/failure-text.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';

  // A record's tags (design system section 11): each a fact pill, with its own Remove button beside it, never inside it
  // (A5), and a field to add one. Removing is not destructive: adding the tag again undoes it (section 10). Without
  // `onadd` and `onremove` (a Viewer, A3) it shows the pills only.
  let {
    tags,
    onadd,
    onremove,
    busy = false,
    failure = '',
  }: {
    tags: readonly string[];
    /** Adds a tag; true when it worked, which clears the field. */
    onadd?: (tag: string) => Promise<boolean>;
    onremove?: (tag: string) => void;
    /** A change is on its way. */
    busy?: boolean;
    /** Why the last change failed, shown under the field. */
    failure?: string;
  } = $props();

  /** As the API allows it (JobApplication.MaxTagLength). */
  const maxTagLength = 50;
  const inputId = $props.id();
  let draft = $state('');
  let missing = $state(false);
  const message = $derived(missing ? 'Name the tag first.' : failure);

  async function onsubmit(event: SubmitEvent) {
    event.preventDefault();
    const tag = draft.trim();
    missing = !tag;
    if (!tag || !onadd) return;
    if (await onadd(tag)) draft = '';
  }
</script>

<div class="flex flex-col gap-3">
  {#if tags.length === 0}
    <p class="text-muted-foreground">No tags yet.</p>
  {:else}
    <ul aria-label="Tags" class="flex flex-wrap gap-x-1 gap-y-2">
      {#each tags as tag (tag)}
        <li class="flex max-w-full min-w-0 items-center">
          <Badge variant="muted" class="min-w-0"><TruncatedText text={tag} /></Badge>
          {#if onremove}
            <IconButton label={`Remove tag ${tag}`} disabled={busy} onclick={() => onremove(tag)}><XIcon aria-hidden="true" /></IconButton>
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
  {#if onadd}
    <form class="flex min-w-0 gap-2" novalidate {onsubmit}>
      <label for={inputId} class="sr-only">New tag</label>
      <Input
        id={inputId}
        bind:value={draft}
        maxlength={maxTagLength}
        autocomplete="off"
        class="min-w-0 flex-1"
        aria-invalid={message ? 'true' : undefined}
        oninput={() => (missing = false)}
      />
      <Button type="submit" variant="outline" class="shrink-0" disabled={busy}><PlusIcon aria-hidden="true" />Add tag</Button>
    </form>
  {/if}
  <FailureText {message} />
</div>
