<script lang="ts">
  import type { CurrentOrganization } from '@screenhire/api-client';
  import XIcon from '~icons/lucide/x';
  import IconButton from '#lib/components/icon-button.svelte';

  // What accepting an invitation did, said once on the first page after it (design system section 11, Welcome line): which
  // team this person is on, as what, and, for someone limited to their own jobs, why the list may look short. It is a plain
  // line the person can dismiss, not a problem (that is `Notice`, E2) and not a toast. Mounting it is the page's job, from the
  // `?joined=1` the accept redirect adds, so there is nothing to remember. The page keeps the `role="status"` region it sits
  // in, so a screen reader announces it as it arrives.
  let { organization, ondismiss }: { organization: CurrentOrganization; ondismiss: () => void } = $props();
</script>

<div data-slot="joined-notice" class="flex w-full items-center gap-3 rounded-panel bg-secondary py-1.5 pl-3.5 pr-1.5 text-secondary-foreground">
  <p class="min-w-0 flex-1">
    You joined {organization.name} as {organization.roleName}.{#if !organization.seesAllJobs}{' '}You see the jobs you are on.{/if}
  </p>
  <IconButton label="Dismiss" onclick={ondismiss}><XIcon aria-hidden="true" /></IconButton>
</div>
