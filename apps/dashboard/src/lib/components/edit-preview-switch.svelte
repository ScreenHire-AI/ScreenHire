<script lang="ts">
  import AppWindowIcon from '~icons/lucide/app-window';
  import EyeIcon from '~icons/lucide/eye';
  import ListIcon from '~icons/lucide/list';
  import PencilIcon from '~icons/lucide/pencil';
  import IconButton from '#lib/components/icon-button.svelte';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { cn } from '#lib/utils.js';

  // The Edit / Preview switch (design system A8), at the top of an editor whose result someone else sees: a pencil and
  // an eye, in a labelled group where the pressed one is the view on screen. Editor and preview never share the screen.
  // With `onpopout`, a window icon beside it opens the preview in a second window that follows the edits
  // (#lib/preview-window.svelte.ts). A view that nobody can edit (a published version, a built-in) names its first choice
  // for what it shows, Questions with a list icon, rather than Edit with a pencil (E3).
  let {
    mode = $bindable('edit'),
    onpopout,
    readOnly = false,
    readLabel = 'Questions',
    class: className,
  }: {
    mode?: 'edit' | 'preview';
    /** Opens the preview window, usually `source.open(url)`. */
    onpopout?: () => void;
    /** The content reads only: the first choice says what it shows instead of offering an edit. */
    readOnly?: boolean;
    /** What the read-only content is, as the first choice names it: the questions of a form, the wording of an email. */
    readLabel?: string;
    class?: string;
  } = $props();
</script>

<div class={cn('flex items-center gap-1', className)}>
  <div role="group" aria-label="View" class="flex gap-1">
    <Toggle bind:pressed={() => mode === 'edit', () => (mode = 'edit')}>
      {#if readOnly}<ListIcon aria-hidden="true" />{readLabel}{:else}<PencilIcon aria-hidden="true" />Edit{/if}
    </Toggle>
    <Toggle bind:pressed={() => mode === 'preview', () => (mode = 'preview')}><EyeIcon aria-hidden="true" />Preview</Toggle>
  </div>
  {#if onpopout}
    <!-- A second window has no room on a phone, where the switch already shows one thing at a time (A8). -->
    <IconButton label="Open in a new window" onclick={onpopout} class="max-md:hidden"><AppWindowIcon aria-hidden="true" /></IconButton>
  {/if}
</div>
