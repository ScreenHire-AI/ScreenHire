<script lang="ts">
  import EyeIcon from '~icons/lucide/eye';
  import ListIcon from '~icons/lucide/list';
  import PencilIcon from '~icons/lucide/pencil';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { cn } from '#lib/utils.js';

  // The Edit / Preview switch (design system A8), at the top of an editor whose result someone else sees: a pencil and
  // an eye, in a labelled group where the pressed one is the view on screen. Editor and preview never share the screen, and
  // the group holds exactly these two choices: the preview's own window opens from its panel (`PreviewPanel`). A view that
  // nobody can edit (a published version, a built-in) names its first choice for what it shows, Questions with a list icon,
  // rather than Edit with a pencil (E3).
  let {
    mode = $bindable('edit'),
    readOnly = false,
    readLabel = 'Questions',
    class: className,
  }: {
    mode?: 'edit' | 'preview';
    /** The content reads only: the first choice says what it shows instead of offering an edit. */
    readOnly?: boolean;
    /** What the read-only content is, as the first choice names it: the questions of a form, the wording of an email. */
    readLabel?: string;
    class?: string;
  } = $props();
</script>

<div role="group" aria-label="View" class={cn('flex gap-1', className)}>
  <Toggle bind:pressed={() => mode === 'edit', () => (mode = 'edit')}>
    {#if readOnly}<ListIcon aria-hidden="true" />{readLabel}{:else}<PencilIcon aria-hidden="true" />Edit{/if}
  </Toggle>
  <Toggle bind:pressed={() => mode === 'preview', () => (mode = 'preview')}><EyeIcon aria-hidden="true" />Preview</Toggle>
</div>
