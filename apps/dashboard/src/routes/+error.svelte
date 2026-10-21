<script lang="ts">
  import { page } from '$app/state';
  import BrandBar from '#lib/components/brand-bar.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // The root error page renders under the root layout only, so it brings its own bar (design system section 7).
  const notFound = $derived(page.status === 404);
  // SvelteKit's own message for an unknown address is "Not Found", which the title already says.
  const message = $derived(notFound ? 'Nothing lives at this address.' : page.error?.message || 'Try again in a minute.');
</script>

<svelte:head>
  <title>{notFound ? 'Page not found' : 'Something went wrong'} · ScreenHire</title>
</svelte:head>

<div class="flex min-h-screen flex-col">
  <BrandBar />
  <main class="flex flex-col">
    <HeaderBand form title={notFound ? 'Page not found' : 'Something went wrong'} />

    <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
      <Panel title={`Error ${page.status}`} footer={back}>
        <p>{message}</p>
      </Panel>
    </div>
  </main>
</div>

{#snippet back()}
  <div class="flex flex-wrap items-center justify-end gap-2">
    <Button href="/" size="lg">Back to jobs</Button>
  </div>
{/snippet}
