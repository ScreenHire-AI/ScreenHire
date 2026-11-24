<script lang="ts">
  import ClockIcon from '~icons/lucide/clock';
  import FileTextIcon from '~icons/lucide/file-text';
  import HandIcon from '~icons/lucide/hand';
  import InboxIcon from '~icons/lucide/inbox';
  import LogInIcon from '~icons/lucide/log-in';
  import PlusIcon from '~icons/lucide/plus';
  import ReplaceIcon from '~icons/lucide/replace';
  import Trash2Icon from '~icons/lucide/trash-2';
  import ZapIcon from '~icons/lucide/zap';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { APPLY_FORM_ID, questionCount, type TimelineForm } from '#lib/forms-timeline.js';
  import { cn } from '#lib/utils.js';

  // A hiring process's forms (design system section 11, Form cards): one card per form in the order the candidate meets
  // them, then "New form". A card is two rows read at a glance: a form icon, the name and its question count as a count
  // badge; then when it is sent, as a chip with its own icon (When they apply, Entering Screening, 3 days after Offer, By
  // hand only). A card is a choice among peers, so it is a pressed button (A6); the chosen one takes the accent fill and
  // its full primary border (S11). The badge and the chip are words inside the button, never controls of their own. A
  // form's menu, Replace with another form and Remove form, is the button's sibling in the card's corner, never inside it,
  // and the application form has none. The strip scrolls sideways when the forms outgrow the width, and never wraps (S9, F5).
  let {
    forms,
    selected = $bindable(),
    onadd,
    onreplace,
    onremove,
  }: {
    forms: readonly TimelineForm[];
    /** `apply`, or the id of the form on show. */
    selected: string;
    /** Opens the choice of a library form; left out where the workflow only reads. */
    onadd?: () => void;
    /** Opens the choice of the library form to put in this one's place; left out where the workflow only reads. */
    onreplace?: (id: string) => void;
    /** Removes a form; left out where the workflow only reads. */
    onremove?: (id: string) => void;
  } = $props();

  // What a menu item opens, or takes away, waits until the menu has closed: a dialog opened from the item itself loses
  // focus to the closing menu, and a card that goes takes its menu with it.
  let chosen: (() => void) | undefined;
  function onMenuClosed() {
    chosen?.();
    chosen = undefined;
  }

  const momentIcons: Record<TimelineForm['moment'], typeof ClockIcon> = { apply: InboxIcon, stage: LogInIcon, later: ClockIcon, hand: HandIcon, other: ZapIcon };

  const cardClass =
    'flex max-w-64 min-w-52 shrink-0 flex-col items-start gap-2 rounded-lg border px-3 py-2.5 text-left transition-colors duration-150 ease-in-out motion-reduce:transition-none pointer-coarse:min-h-11 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring';
</script>

<Panel title="Forms">
  <!-- Cards keep their width and the row scrolls, so a long name never wraps a card shorter than its neighbours. -->
  <div role="group" aria-label="Forms" class="flex min-w-0 items-stretch gap-2 overflow-x-auto p-1 pb-2 [scrollbar-width:thin]">
    {#each forms as form (form.id)}
      {@const on = selected === form.id}
      {@const Moment = momentIcons[form.moment]}
      {@const menu = form.id !== APPLY_FORM_ID && Boolean(onreplace || onremove)}
      <div class="relative flex shrink-0" data-form-card={form.id}>
        <button
          type="button"
          data-form={form.id}
          aria-pressed={on}
          onclick={() => (selected = form.id)}
          class={cn(cardClass, on ? 'border-primary bg-accent text-accent-foreground' : 'border-border bg-card hover:bg-muted')}
        >
          <span class={cn('flex w-full min-w-0 items-start gap-2', menu && 'pr-7 pointer-coarse:pr-10')}>
            <FileTextIcon aria-hidden="true" class={cn('mt-0.5 shrink-0', on ? 'text-primary' : 'text-muted-foreground')} />
            <span class="min-w-0 flex-1 font-semibold break-words">{form.name}</span>
            <Badge variant="count" class={cn('shrink-0', on && 'bg-card')} data-form-count>
              <span aria-hidden="true">{form.questions}</span><span class="sr-only">{questionCount(form.questions)}</span>
            </Badge>
          </span>
          <span class="flex flex-wrap items-center gap-1.5">
            <span
              data-form-when={form.moment}
              class={cn('inline-flex h-6 items-center gap-1 rounded-md border px-2 text-caption [&_svg]:size-3.5', on ? 'border-primary/30 bg-card text-foreground' : 'border-input bg-secondary text-muted-foreground')}
            >
              <Moment aria-hidden="true" />{form.whenShort}
            </span>
            {#if form.equality}<span class={cn('text-caption', on ? 'text-accent-foreground' : 'text-muted-foreground')}>Counted only</span>{/if}
          </span>
        </button>
        {#if menu}
          <MoreMenu label={`Actions for ${form.name}`} size="icon-sm" quiet onclosed={onMenuClosed} class="absolute top-1.5 right-1.5 pointer-coarse:top-0 pointer-coarse:right-0">
            {#if onreplace}
              <DropdownMenu.Item onSelect={() => (chosen = () => onreplace(form.id))}><ReplaceIcon aria-hidden="true" />Replace with another form</DropdownMenu.Item>
            {/if}
            {#if onremove}
              <DropdownMenu.Item onSelect={() => (chosen = () => onremove(form.id))}><Trash2Icon aria-hidden="true" />Remove form</DropdownMenu.Item>
            {/if}
          </MoreMenu>
        {/if}
      </div>
    {/each}
    {#if onadd}
      <button type="button" onclick={onadd} class={cn(cardClass, 'items-center justify-center border-dashed border-input bg-card text-primary hover:bg-muted')}>
        <span class="flex items-center gap-1.5 font-semibold"><PlusIcon aria-hidden="true" />New form</span>
      </button>
    {/if}
  </div>
</Panel>
