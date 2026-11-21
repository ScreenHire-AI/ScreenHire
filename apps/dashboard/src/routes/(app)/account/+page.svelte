<script lang="ts">
  import { goto } from '$app/navigation';
  import { accountSections } from '#lib/account.js';

  // Your account has no page of its own, as Settings has none: its address, the person's menu's Your account, opens Profile,
  // whose menu lists the rest (design system section 21).
  $effect(() => {
    void goto(accountSections[0].href, { replaceState: true });
  });
</script>

<svelte:head>
  <title>Your account · ScreenHire</title>
</svelte:head>
