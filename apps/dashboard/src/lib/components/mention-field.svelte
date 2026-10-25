<script lang="ts">
  import type { TeamMember } from '@screenhire/api-client';
  import { tick } from 'svelte';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { initials } from '#lib/format.js';
  import { insertMention, matchMembers, mentionQuery, type PickedMention } from '#lib/mentions.js';
  import { cn } from '#lib/utils.js';

  // The note field with mentions (design system section 11, Mention): @ opens the organization's members, narrowing as
  // you type. The arrow keys move, Enter or Tab picks, Esc closes the list (and only the list). The field shows names
  // ("@Robin Hale") and keeps who was picked in `picked`, so `toStored` can save each mention as the member's id. A
  // textarea cannot take the combobox role, so the field points at the list with aria-controls and
  // aria-activedescendant, which a textbox allows. Ctrl or Cmd Enter saves; a form shows an @ button below md that calls `startMention`.
  let {
    value = $bindable(''),
    picked = $bindable([]),
    members,
    currentUserId,
    id,
    rows = 2,
    maxlength,
    placeholder,
    invalid = false,
    describedby,
    onsave,
    oncancel,
    ref = $bindable(null),
    class: className,
  }: {
    value?: string;
    picked?: PickedMention[];
    /** The organization's members; the signed-in person is left out of the list. */
    members: readonly TeamMember[];
    currentUserId: string | undefined;
    id: string;
    rows?: number;
    maxlength?: number;
    placeholder?: string;
    invalid?: boolean;
    describedby?: string;
    /** Ctrl or Cmd Enter. */
    onsave?: () => void;
    /** Esc while no list is open. */
    oncancel?: () => void;
    ref?: HTMLTextAreaElement | null;
    class?: string;
  } = $props();

  const listId = $derived(`${id}-teammates`);
  let query = $state<{ start: number; query: string } | null>(null);
  let active = $state(0);

  const others = $derived(members.filter((member) => member.userId !== currentUserId));
  const options = $derived(query ? matchMembers(others, query.query) : []);
  const open = $derived(query !== null && options.length > 0);
  const activeId = $derived(open && options[active] ? `${listId}-${options[active].userId}` : undefined);

  function update() {
    const previous = query?.query;
    query = ref ? mentionQuery(value, ref.selectionStart) : null;
    if (query?.query !== previous) active = 0;
  }

  async function pick(member: TeamMember) {
    if (!query || !ref) return;
    const inserted = insertMention(value, query.start, ref.selectionStart, member.displayName);
    value = inserted.text;
    if (!picked.some((mention) => mention.userId === member.userId)) picked = [...picked, { userId: member.userId, name: member.displayName }];
    query = null;
    await tick();
    ref.focus();
    ref.setSelectionRange(inserted.caret, inserted.caret);
  }

  function onkeydown(event: KeyboardEvent) {
    if (open) {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        active = (active + (event.key === 'ArrowDown' ? 1 : options.length - 1)) % options.length;
        return;
      }
      if ((event.key === 'Enter' && !event.ctrlKey && !event.metaKey) || event.key === 'Tab') {
        event.preventDefault();
        void pick(options[active]!);
        return;
      }
    }
    if (event.key === 'Escape' && query !== null) {
      // Closes the list only: a surrounding dialog or editor stays open.
      event.preventDefault();
      event.stopPropagation();
      query = null;
      return;
    }
    if (event.key === 'Escape' && oncancel) {
      event.preventDefault();
      event.stopPropagation();
      oncancel();
      return;
    }
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey) && onsave) {
      event.preventDefault();
      onsave();
    }
  }

  /** The @ button on a phone (the form's own): an @ at the caret, after a space when it would join a word, and the list opens. */
  export async function startMention() {
    if (!ref) return;
    const caret = ref.selectionStart;
    const before = value.slice(0, caret);
    const at = before.length > 0 && !/\s$/.test(before) ? ' @' : '@';
    value = before + at + value.slice(caret);
    await tick();
    ref.focus();
    ref.setSelectionRange(caret + at.length, caret + at.length);
    update();
  }
</script>

<div class={cn('relative flex min-w-0 flex-col gap-2', className)}>
  <Textarea
    bind:ref
    bind:value
    {id}
    {rows}
    {maxlength}
    {placeholder}
    aria-invalid={invalid ? 'true' : undefined}
    aria-describedby={describedby}
    aria-autocomplete="list"
    aria-controls={open ? listId : undefined}
    aria-activedescendant={activeId}
    oninput={update}
    onclick={update}
    onkeyup={(event) => (event.key === 'ArrowLeft' || event.key === 'ArrowRight' || event.key === 'Home' || event.key === 'End') && update()}
    onblur={() => (query = null)}
    {onkeydown}
  />
  {#if open}
    <ul id={listId} role="listbox" aria-label="Teammates" class="absolute top-full left-0 z-50 mt-1 flex w-full max-w-xs flex-col rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-overlay">
      {#each options as member, index (member.userId)}
        <!-- Picked on mousedown, before the field loses focus; the keyboard picks with Enter or Tab in the field. -->
        <li
          id={`${listId}-${member.userId}`}
          role="option"
          aria-selected={index === active}
          class="flex min-h-10 cursor-pointer items-center gap-2.5 rounded-md px-2.5 py-1 aria-selected:bg-accent pointer-coarse:min-h-11"
          onmousedown={(event) => {
            event.preventDefault();
            void pick(member);
          }}
        >
          <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-caption font-semibold">{initials(member.displayName)}</span>
          <span class="flex min-w-0 flex-col">
            <span class="truncate font-semibold">{member.displayName}</span>
            <span class="text-caption text-muted-foreground">{member.role}</span>
          </span>
        </li>
      {/each}
    </ul>
  {/if}
  <p role="status" class="sr-only">{query === null ? '' : options.length === 0 ? 'No teammates match' : `${options.length} ${options.length === 1 ? 'teammate' : 'teammates'}`}</p>
</div>
