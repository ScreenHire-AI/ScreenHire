<script lang="ts" module>
  /** As the API allows it (JobApplication.MaxTagLength). */
  const maxTagLength = 50;
</script>

<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import XIcon from '~icons/lucide/x';
  import FailureText from '#lib/components/failure-text.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';

  // A record's tags (design system section 11): each a fact pill, with its own Remove button beside it, never inside it
  // (A5), and a field to add one. Removing is not destructive: adding the tag again undoes it (section 10). Without
  // `onadd` and `onremove` (a Viewer, A3) it shows the pills only. The same list holds the sites that may embed the
  // careers page (Settings, Organization), with its own words and field.
  let {
    tags,
    onadd,
    onremove,
    busy = false,
    failure = '',
    noun = 'tag',
    label = 'Tags',
    placeholder = `New ${noun}`,
    maxlength = maxTagLength,
    inputmode,
    missing: missingMessage = 'Name the tag first.',
    empty = 'No tags yet.',
    compact = false,
  }: {
    tags: readonly string[];
    /** Adds a tag; true when it worked, which clears the field. */
    onadd?: (tag: string) => Promise<boolean>;
    onremove?: (tag: string) => void;
    /** A change is on its way. */
    busy?: boolean;
    /** Why the last change failed, shown under the field. */
    failure?: string;
    /** What one item is, in the buttons' words: "Add tag", "Remove tag Urgent". */
    noun?: string;
    /** The list's name for screen readers. */
    label?: string;
    placeholder?: string;
    maxlength?: number;
    inputmode?: 'text' | 'url';
    /** What the field says when Add is pressed with nothing in it. */
    missing?: string;
    /** What the list says with nothing in it. */
    empty?: string;
    /**
     * A candidate's header: the pills in a wrapping row, each Remove button shown on hover, focus and tap only (a touch
     * screen has no hover), and Add tag a text button that opens its field in place. Nothing says "No tags yet".
     */
    compact?: boolean;
  } = $props();

  const inputId = $props.id();
  let draft = $state('');
  let missing = $state(false);
  const message = $derived(missing ? missingMessage : failure);
  let adding = $state(false);
  const focusOnMount = (node: HTMLElement) => node.focus();

  async function onsubmit(event: SubmitEvent) {
    event.preventDefault();
    const tag = draft.trim();
    missing = !tag;
    if (!tag || !onadd) return;
    if (await onadd(tag)) {
      draft = '';
      adding = false;
    }
  }
</script>

{#if compact}
  <div class="flex min-w-0 flex-wrap items-center gap-x-1 gap-y-1.5">
    {#if tags.length > 0}
      <ul aria-label={label} class="flex min-w-0 flex-wrap items-center gap-x-1 gap-y-1.5">
        {#each tags as tag (tag)}
          <li class="group/tag flex max-w-full min-w-0 items-center">
            <Badge variant="muted" class="min-w-0"><TruncatedText text={tag} /></Badge>
            {#if onremove}
              <!-- Shown on hover, focus and tap only: the pills stay quiet until someone reaches for one (F4). -->
              <IconButton
                label={`Remove ${noun} ${tag}`}
                size="icon-xs"
                disabled={busy}
                class="opacity-0 group-focus-within/tag:opacity-100 group-hover/tag:opacity-100 focus-visible:opacity-100 pointer-coarse:opacity-100"
                onclick={() => onremove(tag)}><XIcon aria-hidden="true" /></IconButton
              >
            {/if}
          </li>
        {/each}
      </ul>
    {/if}
    {#if onadd}
      {#if adding}
        <form class="flex min-w-0 items-center gap-2" novalidate {onsubmit}>
          <label for={inputId} class="sr-only">New {noun}</label>
          <Input
            id={inputId}
            bind:value={draft}
            {maxlength}
            {placeholder}
            autocomplete="off"
            class="h-7 w-40 min-w-0"
            aria-invalid={message ? 'true' : undefined}
            oninput={() => (missing = false)}
            onkeydown={(event) => {
              if (event.key === 'Escape') ((adding = false), (draft = ''), (missing = false));
            }}
            {@attach focusOnMount}
          />
          <Button type="submit" variant="outline" size="sm" class="shrink-0" disabled={busy}>Add {noun}</Button>
        </form>
      {:else}
        <Button variant="outline" size="xs" disabled={busy} onclick={() => (adding = true)}><PlusIcon aria-hidden="true" />Add {noun}</Button>
      {/if}
    {/if}
    <FailureText {message} />
  </div>
{:else}
<div class="flex flex-col gap-3">
  {#if tags.length === 0}
    <p class="text-muted-foreground">{empty}</p>
  {:else}
    <ul aria-label={label} class="flex flex-wrap gap-x-1 gap-y-2">
      {#each tags as tag (tag)}
        <li class="flex max-w-full min-w-0 items-center">
          <Badge variant="muted" class="min-w-0"><TruncatedText text={tag} /></Badge>
          {#if onremove}
            <IconButton label={`Remove ${noun} ${tag}`} disabled={busy} onclick={() => onremove(tag)}><XIcon aria-hidden="true" /></IconButton>
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
  {#if onadd}
    <form class="flex min-w-0 gap-2" novalidate {onsubmit}>
      <label for={inputId} class="sr-only">New {noun}</label>
      <Input
        id={inputId}
        bind:value={draft}
        {maxlength}
        {placeholder}
        {inputmode}
        spellcheck={inputmode === 'url' ? false : undefined}
        autocomplete="off"
        class="min-w-0 flex-1"
        aria-invalid={message ? 'true' : undefined}
        oninput={() => (missing = false)}
      />
      <Button type="submit" variant="outline" class="shrink-0" disabled={busy}><PlusIcon aria-hidden="true" />Add {noun}</Button>
    </form>
  {/if}
  <FailureText {message} />
</div>
{/if}
