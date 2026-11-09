<script lang="ts">
  import type { Snippet } from 'svelte';

  let { children }: { children: Snippet } = $props();
</script>

<!-- Signing in and finishing an email link: each page is one centred card that carries the mark (`SignInCard`, design
     system section 7), so the `@` resets these routes to the root layout, past the (auth) layout's brand bar. The route
     ids stay `/(auth)/sign-in...`, which the auth guard reads. -->
{@render children()}
