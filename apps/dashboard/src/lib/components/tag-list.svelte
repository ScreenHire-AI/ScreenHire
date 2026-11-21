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
  import { suggestTags, type KnownTag } from '#lib/tags.js';

  // A record's tags (design system section 11): each a fact pill, with its own Remove button beside it, never inside it
  // (A5), and a field to add one. Removing is not destructive: adding the tag again undoes it (section 10). Without
  // `onadd` and `onremove` (a Viewer, A3) it shows the pills only. The same list holds the sites that may embed the
  // careers page (Settings, Careers site, Careers page), with its own words and field. With `suggestions` (the
  // organization's tags) the field is a combobox: it lists the tags that match, most used first, with Create for a new
  // name last; the arrow keys move, Enter picks, Esc closes the list only.
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
    suggestions,
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
    /** The organization's tags (`GET /v1/tags`) to suggest while typing; without it the field is a plain text field. */
    suggestions?: readonly KnownTag[];
  } = $props();

  const inputId = $props.id();
  let draft = $state('');
  let missing = $state(false);
  const message = $derived(missing ? missingMessage : failure);
  let adding = $state(false);
  const focusOnMount = (node: HTMLElement) => node.focus();

  // The suggestions: tags to pick, then Create for a new name. Open while the field has focus and there is something to offer.
  const listId = `${inputId}-suggestions`;
  let listing = $state(false);
  let active = $state(0);
  const choices = $derived(suggestions ? suggestTags(suggestions, draft, tags) : { matches: [], create: null });
  const options = $derived([
    ...choices.matches.map((name) => ({ name, create: false })),
    ...(choices.create ? [{ name: choices.create, create: true }] : []),
  ]);
  const open = $derived(suggestions !== undefined && listing && options.length > 0);
  const optionId = (index: number) => `${listId}-${index}`;

  async function add(text: string) {
    const tag = text.trim();
    missing = !tag;
    if (!tag || !onadd) return;
    if (await onadd(tag)) {
      draft = '';
      adding = false;
      active = 0;
    }
  }

  async function onsubmit(event: SubmitEvent) {
    event.preventDefault();
    await add(open ? options[active]!.name : draft);
  }

  function onfieldkeydown(event: KeyboardEvent) {
    if (open && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
      event.preventDefault();
      active = (active + (event.key === 'ArrowDown' ? 1 : options.length - 1)) % options.length;
      return;
    }
    if (event.key === 'Escape' && open) {
      // Closes the list only: the field, and a compact header's open field, stay.
      event.preventDefault();
      event.stopPropagation();
      listing = false;
      return;
    }
    if (event.key === 'Escape' && compact) ((adding = false), (draft = ''), (missing = false));
  }

  function oninput() {
    missing = false;
    listing = true;
    active = 0;
  }

  // The field's combobox attributes, only when it suggests (an input may take the combobox role).
  const combobox = $derived(
    suggestions
      ? {
          role: 'combobox',
          'aria-autocomplete': 'list' as const,
          'aria-expanded': open,
          'aria-controls': open ? listId : undefined,
          'aria-activedescendant': open ? optionId(active) : undefined,
        }
      : {},
  );
</script>

{#snippet suggestionList()}
  {#if open}
    <ul id={listId} role="listbox" aria-label="Tags" class="absolute top-full left-0 z-50 mt-1 flex w-full max-w-xs min-w-48 flex-col rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-overlay">
      {#each options as option, index (option.create ? `create:${option.name}` : option.name)}
        <!-- Picked on mousedown, before the field loses focus; the keyboard picks with Enter in the field. -->
        <li
          id={optionId(index)}
          role="option"
          aria-selected={index === active}
          class="flex min-h-9 min-w-0 items-center gap-2 rounded-md px-2.5 py-1 aria-selected:bg-accent pointer-coarse:min-h-11"
          onmousedown={(event) => {
            event.preventDefault();
            void add(option.name);
          }}
        >
          {#if option.create}
            <PlusIcon aria-hidden="true" class="size-4 shrink-0 text-muted-foreground" />
            <span class="min-w-0 truncate">Create "{option.name}"</span>
          {:else}
            <span class="min-w-0 truncate">{option.name}</span>
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
{/snippet}

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
        <form class="relative flex min-w-0 items-center gap-2" novalidate {onsubmit}>
          <label for={inputId} class="sr-only">New {noun}</label>
          <Input
            id={inputId}
            bind:value={draft}
            {maxlength}
            {placeholder}
            autocomplete="off"
            class="h-7 w-40 min-w-0"
            aria-invalid={message ? 'true' : undefined}
            {...combobox}
            {oninput}
            onkeydown={onfieldkeydown}
            onfocus={() => (listing = true)}
            onblur={() => (listing = false)}
            {@attach focusOnMount}
          />
          <Button type="submit" variant="outline" size="sm" class="shrink-0" disabled={busy}>Add {noun}</Button>
          {@render suggestionList()}
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
    <form class="relative flex min-w-0 gap-2" novalidate {onsubmit}>
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
        {...combobox}
        {oninput}
        onkeydown={onfieldkeydown}
        onfocus={() => (listing = true)}
        onblur={() => (listing = false)}
      />
      <Button type="submit" variant="outline" class="shrink-0" disabled={busy}><PlusIcon aria-hidden="true" />Add {noun}</Button>
      {@render suggestionList()}
    </form>
  {/if}
  <FailureText {message} />
</div>
{/if}
