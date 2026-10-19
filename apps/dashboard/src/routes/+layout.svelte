<script lang="ts">
  import '../app.css';
  import type { Snippet } from 'svelte';

  let { children }: { children: Snippet } = $props();

  const navigation = [
    { label: 'Jobs', href: '/', shortcut: 'G J', current: true },
    { label: 'Candidates', href: '/candidates', shortcut: 'G C', current: false },
    { label: 'Templates', href: '/templates', shortcut: 'G T', current: false },
    { label: 'Insights', href: '/insights', shortcut: 'G I', current: false },
  ];
</script>

<div class="flex min-h-screen flex-wrap">
  <nav
    aria-label="Main"
    class="flex max-w-full flex-[1_1_232px] flex-col gap-3.5 border-r border-sidebar-border bg-sidebar px-3 py-3.5"
  >
    <a href="/" class="flex items-center gap-2.5 px-1.5 py-1 text-foreground no-underline">
      <span class="grid size-7 place-items-center rounded-lg bg-foreground font-display font-extrabold text-background">S</span>
      <span class="font-display text-[17px] font-bold tracking-tight">ScreenHire</span>
    </a>
    <ul class="flex flex-col gap-0.5">
      {#each navigation as item (item.href)}
        <li>
          <a
            href={item.href}
            aria-current={item.current ? 'page' : undefined}
            class={[
              'flex h-8 items-center justify-between rounded-md px-2.5 no-underline',
              item.current
                ? 'bg-sidebar-accent font-bold text-sidebar-accent-foreground'
                : 'font-semibold text-sidebar-foreground hover:bg-secondary',
            ]}
          >
            {item.label}
            <kbd class="font-mono text-[11px] font-normal text-muted-foreground">{item.shortcut}</kbd>
          </a>
        </li>
      {/each}
    </ul>
  </nav>

  <div class="min-w-0 flex-[999_1_560px]">
    {@render children()}
  </div>
</div>
