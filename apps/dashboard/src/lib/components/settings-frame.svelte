<script lang="ts">
  import type { Snippet } from 'svelte';

  // The settings frame (design system section 7, Settings): the menu and one column, centred in the content area so a
  // wide window's spare width splits evenly on both sides. Every page with the Settings or account menu has the same
  // frame, whatever it holds (a form, a list, Billing's columns), so moving between them never moves the menu (S8). The
  // header band's `frame` lines its title up with the menu on the same box, `max-w-settings-frame`: 74rem of content plus
  // the page's 24px gutters (below `md` the window is narrower than the frame anyway). From `lg` the menu is a 200px
  // column; below it the menu is a row above the column and the frame fills the window. The column is a container
  // (`@container/column`), so what sits in it responds to the column's width. Panels fill the column, and a form's fields
  // keep the 688px measure inside them (S6), so no width pools beside a short form.
  let {
    menu,
    children,
  }: {
    /** The Settings or account menu. */
    menu?: Snippet;
    children: Snippet;
  } = $props();
</script>

<div data-settings-frame class="mx-auto flex w-full max-w-settings-frame flex-col gap-5 px-4 py-6 md:px-6 lg:grid lg:grid-cols-[12.5rem_minmax(0,1fr)] lg:items-start lg:gap-6">
  {@render menu?.()}
  <div class={['@container/column flex min-w-0 flex-col gap-5 **:data-[slot=field]:max-w-form **:data-[slot=field-set]:max-w-form', !menu && 'lg:col-span-2']}>{@render children()}</div>
</div>
