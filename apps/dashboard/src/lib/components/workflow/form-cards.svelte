<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import Panel from '#lib/components/panel.svelte';
  import { questionCount, type TimelineForm } from '#lib/forms-timeline.js';
  import { cn } from '#lib/utils.js';

  // A hiring process's forms (design system section 11, Form cards): one card per form in the order the candidate meets
  // them, each its name, when it is sent and how many questions it asks, then "New form". A card is a choice among peers,
  // so it is a pressed button (A6); the strip scrolls sideways when the forms outgrow the width, and never wraps (S9, F5).
  let {
    forms,
    selected = $bindable(),
    onadd,
  }: {
    forms: readonly TimelineForm[];
    /** `apply`, or the id of the form on show. */
    selected: string;
    /** Opens the choice of a library form; left out where the workflow only reads. */
    onadd?: () => void;
  } = $props();

  const cardClass =
    'flex min-w-48 shrink-0 flex-col items-start gap-0.5 rounded-lg border px-3 py-2 text-left pointer-coarse:min-h-11 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring';
</script>

<Panel title="Forms">
  <!-- Cards keep their width and the row scrolls, so a long name never wraps a card shorter than its neighbours. -->
  <div role="group" aria-label="Forms" class="flex min-w-0 items-stretch gap-2 overflow-x-auto pb-1 [scrollbar-width:thin]">
    {#each forms as form (form.id)}
      <button
        type="button"
        data-form={form.id}
        aria-pressed={selected === form.id}
        onclick={() => (selected = form.id)}
        class={cn(cardClass, selected === form.id ? 'border-primary bg-accent text-accent-foreground' : 'border-border bg-card hover:bg-muted')}
      >
        <span class="font-semibold">{form.name}</span>
        <span class={cn('text-caption', selected === form.id ? 'text-accent-foreground' : 'text-muted-foreground')}>{form.when}</span>
        <span class={cn('text-caption tabular-nums', selected === form.id ? 'text-accent-foreground' : 'text-muted-foreground')}>
          {questionCount(form.questions)}{form.equality ? ', counted only' : ''}
        </span>
      </button>
    {/each}
    {#if onadd}
      <button type="button" onclick={onadd} class={cn(cardClass, 'justify-center border-dashed border-border bg-card text-primary hover:bg-muted')}>
        <span class="flex items-center gap-1.5 font-semibold"><PlusIcon aria-hidden="true" />New form</span>
      </button>
    {/if}
  </div>
</Panel>
