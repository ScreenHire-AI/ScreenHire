<script lang="ts">
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import { Kbd, KbdGroup } from '#lib/components/ui/kbd/index.js';
  import { Label } from '#lib/components/ui/label/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { sections } from '#lib/shell.js';
  import { hasModifier, isTypingTarget, shortcutGroups } from '#lib/shortcuts.js';

  // Every keyboard shortcut, and the switch for the single-key ones (design system section 15). `?` opens it unless
  // single-key shortcuts are off; the command palette opens it either way.
  let { open = $bindable(false) }: { open?: boolean } = $props();

  const groups = shortcutGroups(sections);
  const id = $props.id();

  function onkeydown(event: KeyboardEvent) {
    if (event.key !== '?' || !preferences.shortcutsEnabled || event.defaultPrevented) return;
    if (hasModifier(event) || isTypingTarget(event.target)) return;
    event.preventDefault();
    open = true;
  }
</script>

<svelte:window {onkeydown} />

<Dialog.Root bind:open>
  <Dialog.Content class="flex max-h-[calc(100dvh-2rem)] flex-col gap-4 overflow-y-auto sm:max-w-lg">
    <Dialog.Header>
      <Dialog.Title>Keyboard shortcuts</Dialog.Title>
    </Dialog.Header>

    <div class="flex items-center justify-between gap-4 rounded-lg bg-secondary px-3 py-2.5">
      <Label for={`${id}-single-key`} class="font-semibold">Single-key shortcuts</Label>
      <Switch
        id={`${id}-single-key`}
        checked={preferences.shortcutsEnabled}
        onCheckedChange={(checked) => preferences.setShortcutsEnabled(checked)}
      />
    </div>

    {#each groups as group, groupIndex (group.label)}
      <section aria-labelledby={`${id}-group-${groupIndex}`} class="flex flex-col gap-1">
        <h3 id={`${id}-group-${groupIndex}`} class="text-caption font-semibold text-muted-foreground">{group.label}</h3>
        <dl class="flex flex-col">
          {#each group.shortcuts as shortcut (shortcut.label)}
            {@const off = shortcut.singleKey && !preferences.shortcutsEnabled}
            <div class="flex items-center justify-between gap-4 border-b border-border-subtle py-2 last:border-0">
              <dt class={off ? 'text-muted-foreground' : undefined}>
                {shortcut.label}{#if off}<span class="sr-only">, off</span>{/if}
              </dt>
              <dd class="flex shrink-0 items-center gap-1 text-caption text-muted-foreground">
                {#if shortcut.sequence}
                  {#each shortcut.keys as key, index (index)}
                    {#if index > 0}<span>then</span>{/if}
                    <Kbd>{key}</Kbd>
                  {/each}
                {:else}
                  <KbdGroup>
                    {#each shortcut.keys as key, index (index)}<Kbd>{key}</Kbd>{/each}
                  </KbdGroup>
                {/if}
              </dd>
            </div>
          {/each}
        </dl>
      </section>
    {/each}
  </Dialog.Content>
</Dialog.Root>
