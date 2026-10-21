<script lang="ts">
  import { page } from '$app/state';
</script>

<svelte:head>
  <title>Something went wrong · ScreenHire</title>
</svelte:head>

<main class="mx-auto flex max-w-[480px] flex-col gap-3 px-5 py-16">
  <p class="text-caption text-muted-foreground">Error {page.status}</p>
  <h1 class="font-display text-page-title">
    {page.status === 404 ? 'Page not found' : 'Something went wrong'}
  </h1>
  <p class="text-muted-foreground">{page.error?.message}</p>
  <p><a href="/" class="font-semibold text-primary underline-offset-4 hover:underline">Back to jobs</a></p>
</main>
