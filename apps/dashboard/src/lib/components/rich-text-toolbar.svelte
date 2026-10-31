<script lang="ts" module>
  /** Which of the toolbar's formats the selection has, for each toggle's pressed state. */
  export interface RichTextActive {
    bold: boolean;
    italic: boolean;
    heading: boolean;
    subheading: boolean;
    bulletList: boolean;
    orderedList: boolean;
    link: boolean;
  }
</script>

<script lang="ts">
  import BoldIcon from '@lucide/svelte/icons/bold';
  import Heading2Icon from '@lucide/svelte/icons/heading-2';
  import Heading3Icon from '@lucide/svelte/icons/heading-3';
  import ItalicIcon from '@lucide/svelte/icons/italic';
  import LinkIcon from '@lucide/svelte/icons/link';
  import ListIcon from '@lucide/svelte/icons/list';
  import ListOrderedIcon from '@lucide/svelte/icons/list-ordered';
  import type { Editor } from '@tiptap/core';
  import { mergeProps } from 'bits-ui';
  import type { Component } from 'svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Kbd, KbdGroup } from '#lib/components/ui/kbd/index.js';
  import * as Popover from '#lib/components/ui/popover/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { normalizeLinkUrl } from '#lib/rich-text.js';
  import { isApple } from '#lib/shortcuts.js';

  // The formatting toolbar (design system section 11, Rich text editor): one tab stop, the arrow keys move between the
  // toggles (the WAI-ARIA toolbar pattern), each pressed toggle takes `accent` (A6) and its tooltip names it with its
  // key caps (A4, K1). Every shortcut has a modifier, so none is a single-key shortcut (WCAG 2.1.4). Links have none:
  // Ctrl K is the command palette everywhere.
  let { editor, active, controls }: { editor: Editor; active: RichTextActive; controls: string } = $props();

  const mod = isApple() ? '⌘' : 'Ctrl';
  const alt = isApple() ? '⌥' : 'Alt';

  type Format = Exclude<keyof RichTextActive, 'link'>;
  const formats: { format: Format; label: string; keys: string[]; icon: Component; run: () => void }[] = [
    { format: 'bold', label: 'Bold', keys: [mod, 'B'], icon: BoldIcon, run: () => editor.chain().focus().toggleBold().run() },
    { format: 'italic', label: 'Italic', keys: [mod, 'I'], icon: ItalicIcon, run: () => editor.chain().focus().toggleItalic().run() },
    {
      format: 'heading',
      label: 'Heading',
      keys: [mod, alt, '2'],
      icon: Heading2Icon,
      run: () => editor.chain().focus().toggleHeading({ level: 2 }).run(),
    },
    {
      format: 'subheading',
      label: 'Subheading',
      keys: [mod, alt, '3'],
      icon: Heading3Icon,
      run: () => editor.chain().focus().toggleHeading({ level: 3 }).run(),
    },
    {
      format: 'bulletList',
      label: 'Bulleted list',
      keys: [mod, 'Shift', '8'],
      icon: ListIcon,
      run: () => editor.chain().focus().toggleBulletList().run(),
    },
    {
      format: 'orderedList',
      label: 'Numbered list',
      keys: [mod, 'Shift', '7'],
      icon: ListOrderedIcon,
      run: () => editor.chain().focus().toggleOrderedList().run(),
    },
  ];

  // A pressed toggle keeps the focus and the selection in the text, so typing goes on where it was. Keyboard users still
  // reach the toolbar with Tab.
  const keepEditorFocus = (event: MouseEvent) => event.preventDefault();

  // Roving focus: the toolbar is one tab stop, the last toggle used.
  let current = $state(0);
  let toolbar = $state<HTMLElement | null>(null);
  const count = formats.length + 1;

  function onkeydown(event: KeyboardEvent) {
    const next =
      event.key === 'ArrowRight' ? (current + 1) % count
      : event.key === 'ArrowLeft' ? (current - 1 + count) % count
      : event.key === 'Home' ? 0
      : event.key === 'End' ? count - 1
      : undefined;
    if (next === undefined) return;
    event.preventDefault();
    current = next;
    toolbar?.querySelectorAll<HTMLElement>('[data-toolbar-item]')[next]?.focus();
  }

  // The link field: the address of the link at the selection, or a new one.
  let linkOpen = $state(false);
  let address = $state('');
  let linkProblem = $state<string | null>(null);

  function openLink(open: boolean) {
    linkOpen = open;
    if (open) {
      address = (editor.getAttributes('link')['href'] as string | undefined) ?? '';
      linkProblem = null;
    }
  }

  function applyLink(event: SubmitEvent) {
    event.preventDefault();
    const href = normalizeLinkUrl(address);
    if (!href) {
      linkProblem = 'Enter a web address or an email address.';
      return;
    }

    const chain = editor.chain().focus();
    if (editor.state.selection.empty && !editor.isActive('link')) {
      chain.insertContent({ type: 'text', text: href.replace(/^mailto:/, ''), marks: [{ type: 'link', attrs: { href } }] }).run();
    } else {
      chain.extendMarkRange('link').setLink({ href }).run();
    }

    linkOpen = false;
  }

  function removeLink() {
    editor.chain().focus().extendMarkRange('link').unsetLink().run();
    linkOpen = false;
  }
</script>

<div
  bind:this={toolbar}
  role="toolbar"
  aria-label="Formatting"
  aria-controls={controls}
  tabindex="-1"
  class="flex min-w-0 items-center gap-0.5 overflow-x-auto border-b border-input p-1"
  {onkeydown}
>
  {#each formats as item, index (item.format)}
    {@const Icon = item.icon}
    <Tooltip.Root>
      <Tooltip.Trigger>
        {#snippet child({ props })}
          <Toggle
            {...mergeProps(props, { onfocus: () => (current = index), onmousedown: keepEditorFocus })}
            data-toolbar-item
            tabindex={current === index ? 0 : -1}
            aria-label={item.label}
            class="pointer-coarse:min-w-11"
            pressed={active[item.format]}
            onPressedChange={() => item.run()}
          >
            <Icon aria-hidden="true" />
          </Toggle>
        {/snippet}
      </Tooltip.Trigger>
      <Tooltip.Content>
        {item.label}
        <KbdGroup>{#each item.keys as key (key)}<Kbd>{key}</Kbd>{/each}</KbdGroup>
      </Tooltip.Content>
    </Tooltip.Root>
  {/each}

  <Popover.Root open={linkOpen} onOpenChange={openLink}>
    <Tooltip.Root>
      <Tooltip.Trigger>
        {#snippet child({ props: tooltipProps })}
          <Popover.Trigger {...tooltipProps}>
            {#snippet child({ props })}
              <Toggle
                {...mergeProps(props, { onfocus: () => (current = formats.length) })}
                data-toolbar-item
                tabindex={current === formats.length ? 0 : -1}
                aria-label="Link"
                class="pointer-coarse:min-w-11"
                pressed={active.link}
              >
                <LinkIcon aria-hidden="true" />
              </Toggle>
            {/snippet}
          </Popover.Trigger>
        {/snippet}
      </Tooltip.Trigger>
      <Tooltip.Content>Link</Tooltip.Content>
    </Tooltip.Root>
    <Popover.Content
      align="start"
      onCloseAutoFocus={(event) => {
        event.preventDefault();
        editor.commands.focus();
      }}
    >
      <form class="flex flex-col gap-2" onsubmit={applyLink}>
        <label for="{controls}-link" class="text-body font-semibold">Link address</label>
        <Input
          id="{controls}-link"
          bind:value={address}
          autocomplete="url"
          aria-invalid={linkProblem ? true : undefined}
        />
        <FailureText message={linkProblem} class="text-caption" />
        <div class="flex justify-end gap-2">
          {#if active.link}
            <Button type="button" variant="outline" size="sm" onclick={removeLink}>Remove link</Button>
          {/if}
          <Button type="submit" variant="outline" size="sm">Apply</Button>
        </div>
      </form>
    </Popover.Content>
  </Popover.Root>
</div>
