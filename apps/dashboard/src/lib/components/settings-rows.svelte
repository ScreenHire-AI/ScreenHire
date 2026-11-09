<script lang="ts">
  import StatusBadge from '#lib/components/status-badge.svelte';
  import type { SendingDomainMark } from '#lib/sending-domain.js';
  import type { SettingsSection } from '#lib/settings.js';

  // The rows of the Settings home (design system section 11): the setting's name as a link, one line under it only where
  // the name does not explain itself (W9), and, in a search, its group after the name. Email domain says where it stands
  // while something is left to do (T3). No icons and no counts.
  let {
    list,
    withGroup = false,
    domainMark,
  }: {
    list: readonly SettingsSection[];
    /** A search's results name each row's group. */
    withGroup?: boolean;
    /** Email domain's status while something is left to do (T3). */
    domainMark?: SendingDomainMark;
  } = $props();
</script>

<ul class="flex flex-col divide-y divide-border-subtle">
  {#each list as section (section.href)}
    <li class="flex min-h-12 flex-col justify-center gap-0.5 px-4.5 py-2.5 max-md:px-4">
      <div class="flex min-w-0 items-center gap-2">
        <a href={section.href} class="py-1 font-semibold text-foreground hover:underline pointer-coarse:py-2.5">{section.label}</a>
        {#if section.href === '/settings/sending-domain' && domainMark}<StatusBadge {...domainMark} />{/if}
        {#if withGroup}<span class="ml-auto shrink-0 text-caption text-muted-foreground">{section.group}</span>{/if}
      </div>
      {#if section.description}
        <p class="text-body text-muted-foreground">{section.description}</p>
      {/if}
    </li>
  {/each}
</ul>
