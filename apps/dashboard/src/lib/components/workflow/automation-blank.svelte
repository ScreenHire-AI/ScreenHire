<script lang="ts">
  import type { Automation } from '@screenhire/api-client';
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import PlusIcon from '~icons/lucide/plus';
  import * as Popover from '#lib/components/ui/popover/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import type { Blank } from '#lib/automation-sentence.js';
  import { cn } from '#lib/utils.js';
  import ChoiceList from './choice-list.svelte';
  import { addChipClass } from './controls.js';

  // One blank of an automation's sentence (design system section 11, Automation sentence): a bordered chip with a chevron
  // that opens a short list of plain choices (`ChoiceList`). A choice closes the list and fills the blank; a multi blank
  // ticks values and stays open; a text blank is a field that fills as it is typed. The chip's name is "Name: what it says",
  // so what is seen is in what is heard (WCAG 2.5.3). It marks a problem with `data-invalid`, because a button takes no
  // `aria-invalid`, and a text blank with nothing in it with `data-empty`, so whoever sends the focus here can open it.
  let {
    blank,
    invalid = false,
    describedby,
    open = $bindable(false),
    onapply,
    onclose,
  }: {
    blank: Blank;
    invalid?: boolean;
    describedby?: string;
    open?: boolean;
    /** The automation with this blank's choice made. */
    onapply: (next: Automation) => void;
    /** The list closed, with the blank as it was left. */
    onclose?: (blank: Blank) => void;
  } = $props();

  const uid = $props.id();
  const shownText = $derived(blank.text || blank.placeholder || '');
  const chip = $derived(
    blank.adds
      ? cn(addChipClass, invalid && 'border-status-verify-mark')
      : cn(
          'inline-flex max-w-full min-w-0 items-center gap-1 rounded-lg border bg-card px-2 py-0.5 align-middle text-left leading-6 font-semibold text-foreground shadow-surface',
          'min-h-8 pointer-coarse:min-h-11 hover:bg-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
          invalid ? 'border-status-verify-mark' : 'border-input',
          !blank.text && blank.kind === 'text' && 'text-muted-foreground',
        ),
  );

  function choose(id: string) {
    if (blank.kind === 'choice') {
      open = false;
      onapply(blank.apply(id));
    } else if (blank.kind === 'multi') {
      const next = blank.selected.includes(id) ? blank.selected.filter((value) => value !== id) : [...blank.selected, id];
      // The last tick stays: a condition with no value is not one.
      if (next.length > 0) onapply(blank.apply(blank.choices.map((choice) => choice.id).filter((value) => next.includes(value))));
    }
  }

  const selected = $derived(blank.kind === 'multi' ? blank.selected : blank.kind === 'choice' && blank.selected !== null ? [blank.selected] : []);
</script>

<Popover.Root
  bind:open
  onOpenChange={(next) => {
    if (!next) onclose?.(blank);
  }}
>
  <Popover.Trigger
    class={chip}
    aria-label={blank.adds ? `${blank.name}: ${blank.text}` : `${blank.name}: ${shownText}`}
    aria-describedby={describedby}
    data-invalid={invalid || undefined}
    data-focus={blank.focusPath}
    data-blank={blank.id}
    data-adds={blank.adds || undefined}
    data-empty={(blank.kind === 'text' && blank.text.trim() === '') || undefined}
  >
    {#if blank.adds}<PlusIcon aria-hidden="true" class="size-3.5 shrink-0" />{/if}<span class="min-w-0 break-words">{shownText}</span>{#if !blank.adds}<ChevronDownIcon aria-hidden="true" class={cn('size-3.5 shrink-0 text-muted-foreground transition-transform motion-reduce:transition-none', open && 'rotate-180')} />{/if}
  </Popover.Trigger>
  <Popover.Content
    align="start"
    collisionPadding={16}
    class="w-72 max-w-[calc(100vw-2rem)] gap-1 p-1"
    onOpenAutoFocus={(event) => {
      // A text blank starts in its field, the one thing it asks for, and a list in the field that takes its keys.
      event.preventDefault();
      document.getElementById(`${uid}-input`)?.focus();
    }}
  >
    {#if blank.kind === 'text'}
      <div class="flex flex-col gap-1.5 p-1.5">
        <label class="flex flex-col gap-1 text-body font-semibold text-secondary-foreground" for={`${uid}-input`}>
          {blank.name}
        </label>
        <Input
          id={`${uid}-input`}
          data-blank-input
          type={blank.numeric ? 'number' : 'text'}
          value={blank.value}
          maxlength={blank.maxLength}
          placeholder={blank.placeholder}
          oninput={(event) => onapply(blank.apply(event.currentTarget.value))}
          onkeydown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              open = false;
            }
          }}
        />
        {#if blank.hint}<p class="text-caption text-muted-foreground">{blank.hint}</p>{/if}
        <div class="flex justify-end"><Button variant="outline" size="sm" onclick={() => (open = false)}>Done</Button></div>
      </div>
      {#if blank.note}<p class="px-2 py-1.5 text-caption text-muted-foreground">{blank.note}</p>{/if}
    {:else}
      <ChoiceList id={`${uid}-input`} name={blank.name} choices={blank.choices} {selected} note={blank.note} onchoose={choose} />
    {/if}
  </Popover.Content>
</Popover.Root>
