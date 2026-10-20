<script lang="ts">
  import { signOut } from '@screenhire/api-client';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import type { LayoutProps } from './$types';

  let { data, children }: LayoutProps = $props();

  const navigation = [
    { label: 'Jobs', href: '/', shortcut: 'G J' },
    { label: 'Candidates', href: '/candidates', shortcut: 'G C' },
    { label: 'Templates', href: '/templates', shortcut: 'G T' },
    { label: 'Insights', href: '/insights', shortcut: 'G I' },
  ];

  const user = $derived(data.user);
  const initials = $derived(
    (user?.displayName ?? '')
      .split(/[\s@.]+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase())
      .join(''),
  );

  async function handleSignOut() {
    await signOut();
    await goto('/sign-in', { invalidateAll: true });
  }
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
        {@const current = page.url.pathname === item.href}
        <li>
          <a
            href={item.href}
            aria-current={current ? 'page' : undefined}
            class={[
              'flex h-8 items-center justify-between rounded-md px-2.5 no-underline',
              current
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

    {#if user}
      <div class="mt-auto">
        <DropdownMenu.Root>
          <DropdownMenu.Trigger
            class="flex w-full items-center gap-2.5 rounded-md px-1.5 py-1.5 text-left hover:bg-secondary"
          >
            <span
              aria-hidden="true"
              class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary font-mono text-[11px] font-semibold text-secondary-foreground"
              >{initials}</span
            >
            <span class="flex min-w-0 flex-col">
              <span class="truncate font-semibold text-sidebar-foreground">{user.displayName}</span>
              <span class="truncate text-xs text-muted-foreground">{user.organization?.name}</span>
            </span>
          </DropdownMenu.Trigger>
          <DropdownMenu.Content align="start" class="w-60">
            <DropdownMenu.Label class="flex flex-col gap-0.5">
              <span class="font-mono text-[10.5px] tracking-[0.06em] text-muted-foreground uppercase">Signed in as</span>
              <span class="truncate font-normal">{user.email}</span>
            </DropdownMenu.Label>
            <DropdownMenu.Separator />
            <DropdownMenu.Item onSelect={handleSignOut}>Sign out</DropdownMenu.Item>
          </DropdownMenu.Content>
        </DropdownMenu.Root>
      </div>
    {/if}
  </nav>

  <div class="min-w-0 flex-[999_1_560px]">
    {@render children()}
  </div>
</div>
